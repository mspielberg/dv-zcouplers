using HarmonyLib;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace DvMod.ZCouplers
{
    public static class Couplers
    {
        private static readonly bool enabled = true;

        private const float ChainSpring = 2e7f; // ~1,200,000 lb/in
        private const float LooseChainLength = 1.1f;
        private const float TightChainLength = 1.0f;
        private const float TightenSpeed = 0.1f;
        private const float BufferTravel = 0.25f;

        private static CouplingScanner? GetScanner(Coupler coupler)
        {
            var scanners = coupler.train.transform.Find("[buffers]")?.GetComponentsInChildren<CouplingScanner>();
            if (scanners == null)
                return null;
            var scanner = coupler.isFrontCoupler
                ? System.Array.Find(scanners, scanner => scanner.transform.localPosition.z > 0)
                : System.Array.Find(scanners, scanner => scanner.transform.localPosition.z < 0);
            if (scanner == null)
                Debug.Log($"Could not find scanner for {(coupler.isFrontCoupler ? "front" : "rear")} coupler on {coupler.train.ID}");
            return scanner;
        }

        private static void KillCouplingScanner(Coupler coupler)
        {
            var scanner = GetScanner(coupler);
            if (scanner?.masterCoro != null)
            {
                Main.DebugLog(() => $"{coupler.train.ID}: killing masterCoro for {(coupler.isFrontCoupler ? "front" : "rear")}");
                scanner.StopCoroutine(scanner.masterCoro);
                scanner.masterCoro = null;
            }
        }

        private static void RestartCouplingScanner(Coupler coupler)
        {
            if (coupler == null)
                return;
            var scanner = GetScanner(coupler);
            if (scanner != null && scanner.masterCoro == null && scanner.isActiveAndEnabled)
            {
                Main.DebugLog(() => $"{coupler.train.ID}: restarting masterCoro for {(coupler.isFrontCoupler ? "front" : "rear")}");
                scanner.masterCoro = scanner.StartCoroutine(scanner.MasterCoro());
            }
        }

        [HarmonyPatch(typeof(Coupler), nameof(Coupler.CreateJoints))]
        public static class CreateJointsPatch
        {
            public static bool Prefix(Coupler __instance)
            {
                // ignore tender joint
                if (!enabled ||
                    (CarTypes.IsSteamLocomotive(__instance.train.carType) && !__instance.isFrontCoupler))
                {
                    return true;
                }

                Main.DebugLog(() => $"Creating tension joint between {__instance.train.ID} and {__instance.coupledTo.train.ID}");
                CreateTensionJoint(__instance);
                var breaker = __instance.gameObject.AddComponent<CouplerBreaker>();
                breaker.joint = __instance.springyCJ;
                if (__instance.rigidCJ == null && __instance.coupledTo.rigidCJ == null)
                    CreateCompressionJoint(__instance, __instance.coupledTo);
                KillCouplingScanner(__instance);
                KillCouplingScanner(__instance.coupledTo);
                return false;
            }
        }

        [HarmonyPatch(typeof(Coupler), nameof(Coupler.Uncouple))]
        public static class UncouplePatch
        {
            private static readonly Dictionary<Coupler, ConfigurableJoint> compressionJoints = new Dictionary<Coupler, ConfigurableJoint>();
            private static readonly Dictionary<Coupler, Coroutine> coros = new Dictionary<Coupler, Coroutine>();

            public static void Prefix(Coupler __instance)
            {
                if (!enabled)
                    return;
                // Prevent Uncouple from destroying compression joint
                compressionJoints[__instance] = __instance.rigidCJ;
                __instance.rigidCJ = null;
                coros[__instance] = __instance.jointCoroRigid;
                __instance.jointCoroRigid = null;

                RestartCouplingScanner(__instance);
                RestartCouplingScanner(__instance.coupledTo);
            }

            public static void Postfix(Coupler __instance)
            {
                if (!enabled)
                    return;
                __instance.rigidCJ = compressionJoints[__instance];
                compressionJoints.Remove(__instance);
                __instance.jointCoroRigid = coros[__instance];
                coros.Remove(__instance);
            }
        }

        [HarmonyPatch(typeof(CarSpawner), nameof(CarSpawner.PrepareTrainCarForDeleting))]
        public static class PrepareTrainCarForDeletingPatch
        {
            public static void Postfix(TrainCar trainCar)
            {
                if (!enabled)
                    return;
                // remove pre-coupling joints, if any
                DestroyCompressionJoint(trainCar.frontCoupler);
                DestroyCompressionJoint(trainCar.rearCoupler);
            }
        }

        private static void CreateTensionJoint(Coupler coupler)
        {
            var anchorOffset =  Vector3.forward * TightChainLength * (coupler.isFrontCoupler ? -1f : 1f);

            var cj = coupler.train.gameObject.AddComponent<ConfigurableJoint>();
            cj.autoConfigureConnectedAnchor = false;
            cj.anchor = coupler.transform.localPosition + anchorOffset;
            cj.connectedBody = coupler.coupledTo.train.gameObject.GetComponent<Rigidbody>();
            cj.connectedAnchor = coupler.coupledTo.transform.localPosition;

            cj.xMotion = ConfigurableJointMotion.Limited;
            cj.yMotion = ConfigurableJointMotion.Limited;
            cj.zMotion = ConfigurableJointMotion.Limited;
            cj.angularYMotion = ConfigurableJointMotion.Limited;

            cj.angularYLimit = new SoftJointLimit { limit = 30f };

            cj.linearLimit = new SoftJointLimit { limit = LooseChainLength };
            cj.linearLimitSpring = new SoftJointLimitSpring { spring = ChainSpring };
            cj.enableCollision = false;
            cj.breakForce = float.PositiveInfinity;
            cj.breakTorque = 1e3f;

            coupler.springyCJ = cj;
            if (!LooseChain.enabled)
                TightenChain(coupler);
        }

        public static void TightenChain(Coupler coupler)
        {
            if (coupler.springyCJ == null)
            {
                if (coupler.coupledTo?.springyCJ != null)
                    TightenChain(coupler.coupledTo);
                return;
            }
            if (coupler.jointCoroSpringy != null)
                coupler.StopCoroutine(coupler.jointCoroSpringy);
            coupler.jointCoroSpringy = coupler.StartCoroutine(TightenChainCoro(coupler.springyCJ));
        }

        private static IEnumerator TightenChainCoro(ConfigurableJoint cj)
        {
            while (cj.linearLimit.limit > TightChainLength)
            {
                yield return WaitFor.FixedUpdate;
                var tightenAmount = Time.deltaTime * TightenSpeed;
                cj.linearLimit = new SoftJointLimit { limit = Mathf.Max(TightChainLength, cj.linearLimit.limit - tightenAmount) };
            }
        }

        public static void LoosenChain(Coupler coupler)
        {
            if (coupler.springyCJ == null)
            {
                if (coupler.coupledTo?.springyCJ != null)
                    LoosenChain(coupler.coupledTo);
                return;
            }
            if (coupler.jointCoroSpringy != null)
                coupler.StopCoroutine(coupler.jointCoroSpringy);
            coupler.jointCoroSpringy = coupler.StartCoroutine(LoosenChainCoro(coupler.springyCJ));
        }

        private static IEnumerator LoosenChainCoro(ConfigurableJoint cj)
        {
            while (cj.linearLimit.limit < LooseChainLength)
            {
                yield return WaitFor.FixedUpdate;
                var tightenAmount = Time.deltaTime * TightenSpeed;
                cj.linearLimit = new SoftJointLimit { limit = Mathf.Min(LooseChainLength, cj.linearLimit.limit + tightenAmount) };
            }
        }

        // Ensure CouplingScanners stay active when not in view
        [HarmonyPatch(typeof(ChainCouplerVisibilityOptimizer), nameof(ChainCouplerVisibilityOptimizer.Disable))]
        public static class ChainCouplerVisibilityOptimizerDisablePatch
        {
            public static bool Prefix(ChainCouplerVisibilityOptimizer __instance)
            {
                if (!enabled)
                    return true;
                if (!__instance.enabled)
                    return false;
                __instance.enabled = false;
                __instance.chain.SetActive(false);
                return false;
            }
        }

        [HarmonyPatch(typeof(CouplingScanner), nameof(CouplingScanner.Start))]
        public static class CouplingScannerStartPatch
        {
            public static void Postfix(CouplingScanner __instance)
            {
                if (!enabled)
                    return;
                var scanner = __instance;
                __instance.ScanStateChanged += (CouplingScanner otherScanner) =>
                {
                    if (scanner == null)
                        return;
                    var car = TrainCar.Resolve(scanner.gameObject);
                    if (car == null)
                        return;
                    var coupler = scanner.transform.localPosition.z > 0 ? car.frontCoupler : car.rearCoupler;
                    if (coupler == null)
                        return;

                    if (otherScanner != null)
                    {
                        var otherCar = TrainCar.Resolve(otherScanner.gameObject);
                        var otherCoupler = otherScanner.transform.localPosition.z > 0 ? otherCar.frontCoupler : otherCar.rearCoupler;
                        if (coupler.rigidCJ == null && otherCoupler.rigidCJ == null)
                        {
                            CreateCompressionJoint(coupler, otherCoupler);
                        }
                    }
                    else
                    {
                        DestroyCompressionJoint(coupler);
                    }
                };
            }
        }

        [HarmonyPatch(typeof(CouplingScanner), nameof(CouplingScanner.MasterCoro))]
        public static class CouplerScannerMasterCoroPatch
        {
            public static bool Prefix(CouplingScanner __instance, ref IEnumerator __result)
            {
                if (!enabled)
                    return true;
                __result = ReplacementCoro(__instance);
                return false;
            }

            private static Coupler GetCoupler(CouplingScanner scanner)
            {
                var car = TrainCar.Resolve(scanner.gameObject);
                return scanner.transform.localPosition.z > 0 ? car.frontCoupler : car.rearCoupler;
            }

            private static void TryCouple(Coupler coupler)
            {
                if (coupler.IsCoupled())
                    return;
                var otherCoupler = coupler.GetFirstCouplerInRange();
                if (otherCoupler == null)
                    return;
                bool hoseWasConnected = coupler.hoseAndCock.IsHoseConnected;
                bool thisCockOpen = coupler.IsCockOpen;
                bool otherCockOpen = otherCoupler.IsCockOpen;
                coupler.CoupleTo(otherCoupler);
                if (!hoseWasConnected)
                    coupler.DisconnectAirHose(true);
                coupler.IsCockOpen = thisCockOpen;
                otherCoupler.IsCockOpen = otherCockOpen;
            }

            private const float AutoCoupleRange = 0.495f;
            private static IEnumerator ReplacementCoro(CouplingScanner __instance)
            {
                yield return null;
                var coupler = GetCoupler(__instance);
                if (coupler.IsCoupled())
                {
                    Main.DebugLog(() => $"{coupler.train.ID}: MasterCoro exiting immediately");
                    __instance.masterCoro = null;
                    yield break;
                }
                else
                {
                    Main.DebugLog(() => $"{coupler.train.ID}: MasterCoro started");
                }

                var wait = WaitFor.Seconds(0.1f);
                while (true)
                {
                    yield return wait;
                    var offset = __instance.transform.InverseTransformPoint(__instance.nearbyScanner.transform.position);
                    if (Mathf.Abs(offset.x) > 1.6f || Mathf.Abs(offset.z) > 2f)
                    {
                        break;
                    }
                    else
                    {
                        Main.DebugLog(coupler.train, () => $"{coupler.train.ID}: offset.z = {offset.z}");
                        if (Main.settings.couplerType == CouplerType.JanneyKnuckle && Mathf.Abs(offset.z) < AutoCoupleRange)
                            TryCouple(coupler);
                    }
                }
                __instance.Unpair(true);
            }
        }

        internal static readonly ConditionalWeakTable<Coupler, ConfigurableJoint> bufferJoints = new ConditionalWeakTable<Coupler, ConfigurableJoint>();

        private static void CreateCompressionJoint(Coupler a, Coupler b)
        {
            Main.DebugLog(() => $"Creating compression joint between {TrainCar.Resolve(a.gameObject)?.ID} and {TrainCar.Resolve(b.gameObject)?.ID}");

            // create fully rigid (bottoming out) joint
            var bottomedCj = a.train.gameObject.AddComponent<ConfigurableJoint>();
            bottomedCj.autoConfigureConnectedAnchor = false;
            bottomedCj.anchor = a.transform.localPosition + (2 * (a.isFrontCoupler ? Vector3.forward : Vector3.back));
            bottomedCj.connectedBody = b.train.gameObject.GetComponent<Rigidbody>();
            bottomedCj.connectedAnchor = b.transform.localPosition;
            bottomedCj.zMotion = ConfigurableJointMotion.Limited;

            bottomedCj.linearLimit = new SoftJointLimit { limit = BufferTravel + 2f };
            bottomedCj.linearLimitSpring = new SoftJointLimitSpring { spring = ChainSpring };
            bottomedCj.enableCollision = false;
            bottomedCj.breakForce = float.PositiveInfinity;
            bottomedCj.breakTorque = float.PositiveInfinity;

            a.rigidCJ = bottomedCj;

            // create buffer joint
            var bufferCj = a.train.gameObject.AddComponent<ConfigurableJoint>();
            bufferCj.autoConfigureConnectedAnchor = false;
            bufferCj.anchor = a.transform.localPosition + (2 * (a.isFrontCoupler ? Vector3.forward : Vector3.back));
            bufferCj.connectedBody = b.train.gameObject.GetComponent<Rigidbody>();
            bufferCj.connectedAnchor = b.transform.localPosition;
            bufferCj.zMotion = ConfigurableJointMotion.Limited;

            bufferCj.linearLimit = new SoftJointLimit { limit = 2f };
            bufferCj.linearLimitSpring = new SoftJointLimitSpring
            {
                spring = Main.settings.bufferSpringRate * 1e6f,
                damper = Main.settings.bufferDamperRate * 1e6f,
            };
            bufferCj.enableCollision = false;
            bufferCj.breakForce = float.PositiveInfinity;
            bufferCj.breakTorque = float.PositiveInfinity;

            bufferJoints.Add(a, bufferCj);
        }

        private static void DestroyCompressionJoint(Coupler coupler)
        {
            if (coupler.rigidCJ == null)
                return;
            Main.DebugLog(() => $"Destroying compression joint between {TrainCar.Resolve(coupler.gameObject)?.ID} and {TrainCar.Resolve(coupler.rigidCJ.connectedBody.gameObject)?.ID}");
            if (coupler.jointCoroRigid != null)
            {
                coupler.StopCoroutine(coupler.jointCoroRigid);
                coupler.jointCoroRigid = null;
            }
            Component.Destroy(coupler.rigidCJ);
            coupler.rigidCJ = null;
            if (bufferJoints.TryGetValue(coupler, out var bufferJoint))
                Component.Destroy(bufferJoint);
            bufferJoints.Remove(coupler);
        }

        private static Vector3 JointDelta(Joint joint, bool isFrontCoupler)
        {
            var delta = joint.transform.InverseTransformPoint(joint.connectedBody.transform.TransformPoint(joint.connectedAnchor)) - joint.anchor;
            return isFrontCoupler ? delta : -delta;
        }
    }
}
