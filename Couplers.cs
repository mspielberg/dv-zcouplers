using HarmonyLib;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace DvMod.ZCouplers
{
    public static class Couplers
    {
        // Custom tension joint management
        private static readonly Dictionary<Coupler, ConfigurableJoint> customTensionJoints = new Dictionary<Coupler, ConfigurableJoint>();
        
        // Track when joints were last created to prevent rapid recreation
        private static readonly Dictionary<Coupler, float> lastJointCreationTime = new Dictionary<Coupler, float>();
        private const float MinJointCreationInterval = 2.0f; // Seconds between joint creation attempts
        
        // Track when couplers were manually uncoupled to prevent immediate recoupling
        private static readonly Dictionary<Coupler, float> lastUncouplingTime = new Dictionary<Coupler, float>();
        private const float UncouplingCooldown = 5.0f; // Seconds before allowing recoupling after manual uncoupling
        
        private const float ChainSpring = 2e7f; // ~1,200,000 lb/in
        private const float LooseChainLength = 1.1f;
        private const float TightChainLength = 1.0f;
        private const float TightenSpeed = 0.1f;
        private const float BufferTravel = 0.25f;

        private static CouplingScanner? GetScanner(Coupler coupler)
        {
            return coupler.visualCoupler?.GetComponent<CouplingScanner>();
        }

        private static void KillCouplingScanner(Coupler coupler)
        {
            if (coupler == null)
                return;
                
            try
            {
                var scanner = GetScanner(coupler);
                if (scanner?.masterCoro != null)
                {
                    scanner.StopCoroutine(scanner.masterCoro);
                    scanner.masterCoro = null;
                }
            }
            catch (System.Exception ex)
            {
                Main.DebugLog(() => $"Error killing coupling scanner: {ex.Message}");
            }
        }

        private static void RestartCouplingScanner(Coupler coupler)
        {
            if (coupler == null)
                return;
                
            try
            {
                var scanner = GetScanner(coupler);
                if (scanner != null && scanner.masterCoro == null && scanner.isActiveAndEnabled)
                {
                    scanner.masterCoro = scanner.StartCoroutine(scanner.MasterCoro());
                }
            }
            catch (System.Exception ex)
            {
                Main.DebugLog(() => $"Error restarting coupling scanner: {ex.Message}");
            }
        }
        
        private static void SeparateCarsAfterUncoupling(Coupler coupler1, Coupler coupler2)
        {
            if (coupler1?.train?.gameObject == null || coupler2?.train?.gameObject == null)
                return;
                
            try
            {
                var rb1 = coupler1.train.GetComponent<Rigidbody>();
                var rb2 = coupler2.train.GetComponent<Rigidbody>();
                
                if (rb1 != null && rb2 != null)
                {
                    // Calculate separation direction
                    var direction = (coupler1.transform.position - coupler2.transform.position).normalized;
                    var separationForce = 2000f; // Increased force significantly
                    
                    // Apply opposing forces to separate the cars
                    rb1.AddForce(direction * separationForce, ForceMode.Impulse);
                    rb2.AddForce(-direction * separationForce, ForceMode.Impulse);
                    
                    Main.DebugLog(() => $"Applied separation force between {coupler1.train.ID} and {coupler2.train.ID}");
                }
                
                // Temporarily disable coupling scanners to prevent immediate recoupling
                var scanner1 = GetScanner(coupler1);
                var scanner2 = GetScanner(coupler2);
                
                if (scanner1 != null)
                {
                    scanner1.enabled = false;
                    scanner1.StartCoroutine(ReEnableScanner(scanner1, coupler1.train.ID, 3.0f));
                }
                if (scanner2 != null)
                {
                    scanner2.enabled = false;
                    scanner2.StartCoroutine(ReEnableScanner(scanner2, coupler2.train.ID, 3.0f));
                }
                
            }
            catch (System.Exception ex)
            {
                Main.DebugLog(() => $"Error separating cars after uncoupling: {ex.Message}");
            }
        }
        
        private static IEnumerator ReEnableScanner(CouplingScanner scanner, string trainId, float delay)
        {
            yield return new WaitForSeconds(delay);
            
            if (scanner != null)
            {
                scanner.enabled = true;
                Main.DebugLog(() => $"Re-enabled coupling scanner for {trainId}");
            }
        }
        
        private static void DestroyAllJoints(Coupler coupler)
        {
            if (coupler?.train?.gameObject == null)
                return;
                
            try
            {
                var trainCar = coupler.train;
                Main.DebugLog(() => $"Destroying all joints on {trainCar.ID}");
                
                // Destroy ALL ConfigurableJoints on this car
                var allJoints = trainCar.GetComponents<ConfigurableJoint>();
                foreach (var joint in allJoints)
                {
                    if (joint != null)
                        Component.Destroy(joint);
                }
                
                // Destroy ALL FixedJoints on this car
                var fixedJoints = trainCar.GetComponents<FixedJoint>();
                foreach (var joint in fixedJoints)
                {
                    if (joint != null)
                        Component.Destroy(joint);
                }
                
                // Destroy ALL SpringJoints on this car
                var springJoints = trainCar.GetComponents<SpringJoint>();
                foreach (var joint in springJoints)
                {
                    if (joint != null)
                        Component.Destroy(joint);
                }
                
                // Destroy ALL HingeJoints on this car
                var hingeJoints = trainCar.GetComponents<HingeJoint>();
                foreach (var joint in hingeJoints)
                {
                    if (joint != null)
                        Component.Destroy(joint);
                }
                
                // Clear the rigidCJ and jointCoroRigid from both couplers on this car
                if (trainCar.frontCoupler != null)
                {
                    trainCar.frontCoupler.rigidCJ = null;
                    trainCar.frontCoupler.jointCoroRigid = null;
                }
                if (trainCar.rearCoupler != null)
                {
                    trainCar.rearCoupler.rigidCJ = null;
                    trainCar.rearCoupler.jointCoroRigid = null;
                }
                
                Main.DebugLog(() => $"Completed joint destruction for {trainCar.ID}");
            }
            catch (System.Exception ex)
            {
                Main.DebugLog(() => $"Error in nuclear joint cleanup: {ex.Message}");
            }
        }

        [HarmonyPatch(typeof(Coupler), nameof(Coupler.CreateJoints))]
        public static class CreateJointsPatch
        {
            public static bool Prefix(Coupler __instance)
            {
                // ignore tender joint
                if (__instance.train.GetComponent<DV.SteamTenderAutoCoupleMechanism>() != null && !__instance.isFrontCoupler)
                {
                    return true;
                }

                // Prevent joint creation during save loading to avoid physics instability
                if (SaveManager.IsLoadingFromSave)
                {
                    Main.DebugLog(() => $"Skipping joint creation during save loading: {__instance.train.ID}");
                    return true;
                }

                // Safety check: don't create joints if train is derailed or moving too fast
                if (__instance.train.derailed || __instance.coupledTo?.train.derailed == true)
                {
                    Main.DebugLog(() => $"Skipping joint creation - train derailed: {__instance.train.ID}");
                    return true;
                }

                // Safety check: don't create joints if cars are moving too fast (loading/teleporting)
                var velocity1 = __instance.train.GetComponent<Rigidbody>()?.velocity.magnitude ?? 0f;
                var velocity2 = __instance.coupledTo?.train.GetComponent<Rigidbody>()?.velocity.magnitude ?? 0f;
                if (velocity1 > 5f || velocity2 > 5f)
                {
                    Main.DebugLog(() => $"Skipping joint creation - cars moving too fast: {__instance.train.ID} ({velocity1:F1} m/s) to {__instance.coupledTo?.train.ID} ({velocity2:F1} m/s)");
                    return true;
                }

                // Safety check: don't create joints too frequently (prevent load/teleport spam)
                var currentTime = Time.time;
                if (lastJointCreationTime.TryGetValue(__instance, out var lastTime) && (currentTime - lastTime) < MinJointCreationInterval)
                {
                    Main.DebugLog(() => $"Skipping joint creation - too soon after last creation: {__instance.train.ID}");
                    return true;
                }
                
                // Safety check: don't recouple immediately after manual uncoupling
                if (lastUncouplingTime.TryGetValue(__instance, out var lastUncoupling) && (currentTime - lastUncoupling) < UncouplingCooldown)
                {
                    Main.DebugLog(() => $"Skipping joint creation - uncoupling cooldown active: {__instance.train.ID} ({(UncouplingCooldown - (currentTime - lastUncoupling)):F1}s remaining)");
                    return true;
                }
                if (__instance.coupledTo != null && lastUncouplingTime.TryGetValue(__instance.coupledTo, out var partnerLastUncoupling) && (currentTime - partnerLastUncoupling) < UncouplingCooldown)
                {
                    Main.DebugLog(() => $"Skipping joint creation - partner uncoupling cooldown active: {__instance.coupledTo.train.ID}");
                    return true;
                }

                // Safety check: don't create joints if we already have custom tension joints
                if (customTensionJoints.ContainsKey(__instance) || (__instance.coupledTo != null && customTensionJoints.ContainsKey(__instance.coupledTo)))
                {
                    Main.DebugLog(() => $"Skipping joint creation - joints already exist: {__instance.train.ID}");
                    return false;
                }

                // Safety check: ensure we have a valid coupled partner
                if (__instance.coupledTo == null)
                {
                    Main.DebugLog(() => $"Skipping joint creation - no coupled partner: {__instance.train.ID}");
                    return true;
                }

                Main.DebugLog(() => $"Creating tension joint between {__instance.train.ID} and {__instance.coupledTo.train.ID}");
                
                // Record the time of joint creation
                lastJointCreationTime[__instance] = currentTime;
                lastJointCreationTime[__instance.coupledTo] = currentTime;
                
                CreateTensionJoint(__instance);
                var breaker = __instance.gameObject.AddComponent<CouplerBreaker>();
                if (customTensionJoints.TryGetValue(__instance, out var tensionJoint))
                    breaker.joint = tensionJoint;
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
                Main.DebugLog(() => $"Uncoupling {__instance.train.ID} from {__instance.coupledTo?.train.ID}");
                
                // Record uncoupling time to prevent immediate recoupling
                var currentTime = Time.time;
                lastUncouplingTime[__instance] = currentTime;
                if (__instance.coupledTo != null)
                    lastUncouplingTime[__instance.coupledTo] = currentTime;
                
                // Clear any pending coupler states for these cars
                SaveManager.ClearPendingStatesForCar(__instance.train);
                if (__instance.coupledTo?.train != null)
                    SaveManager.ClearPendingStatesForCar(__instance.coupledTo.train);
                
                // Prevent Uncouple from destroying compression joint
                compressionJoints[__instance] = __instance.rigidCJ;
                __instance.rigidCJ = null;
                coros[__instance] = __instance.jointCoroRigid;
                __instance.jointCoroRigid = null;

                // Destroy tension joints on both couplers - be more aggressive about cleanup
                DestroyTensionJoint(__instance);
                if (__instance.coupledTo != null)
                {
                    DestroyTensionJoint(__instance.coupledTo);
                }

                // Also destroy compression joints to ensure complete disconnection
                DestroyCompressionJoint(__instance);
                if (__instance.coupledTo != null)
                {
                    DestroyCompressionJoint(__instance.coupledTo);
                }
                
                // Destroy all Unity joints on both cars to ensure complete physical disconnection
                DestroyAllJoints(__instance);
                if (__instance.coupledTo != null)
                {
                    DestroyAllJoints(__instance.coupledTo);
                }

                // Update knuckle coupler visual state to show uncoupled
                KnuckleCouplers.UnlockCoupler(__instance, viaChainInteraction: false);
                if (__instance.coupledTo != null)
                {
                    KnuckleCouplers.UnlockCoupler(__instance.coupledTo, viaChainInteraction: false);
                }

                // Restart coupling scanners and apply separation force
                RestartCouplingScanner(__instance);
                if (__instance.coupledTo != null)
                {
                    RestartCouplingScanner(__instance.coupledTo);
                    SeparateCarsAfterUncoupling(__instance, __instance.coupledTo);
                }
                    
                Main.DebugLog(() => $"Completed uncoupling cleanup for {__instance.train.ID}");
            }

            public static void Postfix(Coupler __instance)
            {
                __instance.rigidCJ = compressionJoints[__instance];
                compressionJoints.Remove(__instance);
                __instance.jointCoroRigid = coros[__instance];
                coros.Remove(__instance);
            }
        }

        [HarmonyPatch(typeof(TrainCar), nameof(TrainCar.UncoupleSelf))]
        public static class UncoupleSelfPatch
        {
            public static void Postfix(TrainCar __instance)
            {
                Main.DebugLog(() => "TrainCar.UncoupleSelf.Postfix");
                // remove pre-coupling joints, if any, before car is teleported
                DestroyCompressionJoint(__instance.frontCoupler);
                DestroyCompressionJoint(__instance.rearCoupler);
                
                // remove tension joints
                DestroyTensionJoint(__instance.frontCoupler);
                DestroyTensionJoint(__instance.rearCoupler);
                
                KillCouplingScanner(__instance.frontCoupler);
                KillCouplingScanner(__instance.rearCoupler);
            }
        }

        [HarmonyPatch(typeof(CarSpawner), nameof(CarSpawner.PrepareTrainCarForDeleting))]
        public static class PrepareTrainCarForDeletingPatch
        {
            public static void Postfix(TrainCar trainCar)
            {
                try
                {
                    if (trainCar == null)
                        return;
                        
                    Main.DebugLog(() => $"TrainCar.PrepareTrainCarForDeleting.Postfix for {trainCar.ID}");
                    
                    // Safely remove joints - check for null and validity before cleanup
                    SafeCleanupCoupler(trainCar.frontCoupler);
                    SafeCleanupCoupler(trainCar.rearCoupler);
                }
                catch (System.Exception ex)
                {
                    Main.DebugLog(() => $"Error during car deletion cleanup: {ex.Message}");
                    // Don't rethrow - let the game continue its deletion process
                }
            }
            
            private static void SafeCleanupCoupler(Coupler coupler)
            {
                if (coupler == null || coupler.gameObject == null)
                    return;
                    
                try
                {
                    // Only cleanup if the objects are still valid
                    DestroyCompressionJoint(coupler);
                    DestroyTensionJoint(coupler);
                    KillCouplingScanner(coupler);
                }
                catch (System.Exception ex)
                {
                    Main.DebugLog(() => $"Error cleaning up coupler: {ex.Message}");
                }
            }
        }

        private static void CreateTensionJoint(Coupler coupler)
        {
            // Enhanced logging for tension joint creation
            var coupledTo = coupler.coupledTo;
            Main.DebugLog(() => $"TENSION JOINT: Creating for {coupler.train.ID} {coupler.Position()} -> {coupledTo?.train.ID} {coupledTo?.Position()}");
            
            var anchorOffset =  Vector3.forward * TightChainLength * (coupler.isFrontCoupler ? -1f : 1f);

            var cj = coupler.train.gameObject.AddComponent<ConfigurableJoint>();
            cj.autoConfigureConnectedAnchor = false;
            cj.anchor = coupler.transform.localPosition + anchorOffset;
            cj.connectedBody = coupler.coupledTo.train.gameObject.GetComponent<Rigidbody>();
            cj.connectedAnchor = coupler.coupledTo.transform.localPosition;

            cj.xMotion = ConfigurableJointMotion.Limited;
            cj.yMotion = ConfigurableJointMotion.Limited;
            cj.zMotion = ConfigurableJointMotion.Limited;
            cj.angularXMotion = ConfigurableJointMotion.Limited;
            cj.angularYMotion = ConfigurableJointMotion.Limited;
            cj.angularZMotion = ConfigurableJointMotion.Limited;

            cj.lowAngularXLimit = new SoftJointLimit { limit = 5f };
            cj.highAngularXLimit = new SoftJointLimit { limit = 5f };
            cj.angularYLimit = new SoftJointLimit { limit = 30f };
            cj.angularZLimit = new SoftJointLimit { limit = 5 };

            cj.angularXLimitSpring = new SoftJointLimitSpring { spring = ChainSpring };
            cj.angularYZLimitSpring = new SoftJointLimitSpring { spring = ChainSpring };

            cj.linearLimit = new SoftJointLimit { limit = LooseChainLength };
            cj.linearLimitSpring = new SoftJointLimitSpring { spring = ChainSpring };
            cj.enableCollision = false;
            cj.breakForce = float.PositiveInfinity;
            cj.breakTorque = float.PositiveInfinity;

            // Store tension joint
            customTensionJoints[coupler] = cj;
            Main.DebugLog(() => $"TENSION JOINT: Created successfully for {coupler.train.ID} {coupler.Position()}, total tension joints: {customTensionJoints.Count}");
            
            if (!LooseChain.enabled)
                TightenChain(coupler);
        }

        public static void TightenChain(Coupler coupler)
        {
            if (!customTensionJoints.TryGetValue(coupler, out var tensionJoint))
            {
                if (coupler.coupledTo != null && customTensionJoints.ContainsKey(coupler.coupledTo))
                    TightenChain(coupler.coupledTo);
                return;
            }
            tensionJoint.linearLimit = new SoftJointLimit { limit = TightChainLength };
        }

        public static void LoosenChain(Coupler coupler)
        {
            if (!customTensionJoints.TryGetValue(coupler, out var tensionJoint))
            {
                if (coupler.coupledTo != null && customTensionJoints.ContainsKey(coupler.coupledTo))
                    LoosenChain(coupler.coupledTo);
                return;
            }
            tensionJoint.linearLimit = new SoftJointLimit { limit = LooseChainLength };
        }

        // Ensure CouplingScanners start active
        [HarmonyPatch(typeof(Coupler), nameof(Coupler.AutoCouple))]
        public static class AutoCouplePatch
        {
            public static void Postfix(Coupler __instance, ref IEnumerator __result)
            {
                var scanner = GetScanner(__instance);
                if (scanner == null)
                    return;

                scanner.enabled = false;

                __result = new EnumeratorWrapper(__result, () => scanner.enabled = true);
            }
        }

        // Ensure CouplingScanners stay active when not in view
        [HarmonyPatch(typeof(ChainCouplerVisibilityOptimizer), nameof(ChainCouplerVisibilityOptimizer.Disable))]
        public static class ChainCouplerVisibilityOptimizerDisablePatch
        {
            public static bool Prefix(ChainCouplerVisibilityOptimizer __instance)
            {
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
                if (coupler.IsCoupled() || coupler.train.derailed)
                    return;
                var otherCoupler = coupler.GetFirstCouplerInRange();
                if (otherCoupler == null || otherCoupler.train.derailed)
                    return;
                coupler.CoupleTo(otherCoupler, viaChainInteraction: true);
            }

            private const float StaticOffset = 0.5f;
            private static IEnumerator ReplacementCoro(CouplingScanner __instance)
            {
                yield return null;
                var coupler = GetCoupler(__instance);
                if (coupler.IsCoupled())
                {
                    Main.DebugLog(() => $"{coupler.train.ID} {coupler.Position()}: MasterCoro exiting immediately");
                    __instance.masterCoro = null;
                    yield break;
                }
                else
                {
                    Main.DebugLog(() =>
                    {
                        var otherCoupler = GetCoupler(__instance.nearbyScanner);
                        return $"{coupler.train.ID} {coupler.Position()}: MasterCoro started with {otherCoupler.train.ID} {otherCoupler.Position()}";
                    });
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
                        var compression = StaticOffset - offset.z;
                        if (KnuckleCouplers.enabled
                            && __instance.nearbyScanner.isActiveAndEnabled
                            && compression > Main.settings.autoCoupleThreshold * 1e-3f
                            && KnuckleCouplers.IsReadyToCouple(coupler)
                            && KnuckleCouplers.IsReadyToCouple(GetCoupler(__instance.nearbyScanner)))
                        {
                            Main.DebugLog(() => $"{coupler.train.ID} {coupler.Position()}: auto coupling due to compression={compression}");
                            TryCouple(coupler);
                        }
                    }
                }
                __instance.Unpair(true);
            }
        }

        internal static readonly Dictionary<Coupler, (Coupler otherCoupler, ConfigurableJoint joint)> bufferJoints =
            new Dictionary<Coupler, (Coupler otherCoupler, ConfigurableJoint joint)>();

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
                spring = Main.settings.GetSpringRate(),
                damper = Main.settings.GetDamperRate(),
            };
            bufferCj.enableCollision = false;
            bufferCj.breakForce = float.PositiveInfinity;
            bufferCj.breakTorque = float.PositiveInfinity;

            bufferJoints.Add(a, (b, bufferCj));
            bufferJoints.Add(b, (a, bufferCj));
        }

        private static void DestroyCompressionJoint(Coupler coupler)
        {
            if (coupler == null || !bufferJoints.TryGetValue(coupler, out var result))
                return;

            try
            {
                Main.DebugLog(() => $"Destroying compression joint between {TrainCar.Resolve(coupler.gameObject)?.ID} and {TrainCar.Resolve(result.otherCoupler.gameObject)?.ID}");
                
                // Safely destroy the joint
                if (result.joint != null)
                    Component.Destroy(result.joint);

                foreach (var c in new Coupler[]{ coupler, result.otherCoupler })
                {
                    if (c != null)
                    {
                        try
                        {
                            if (c.jointCoroRigid != null)
                            {
                                c.StopCoroutine(c.jointCoroRigid);
                                c.jointCoroRigid = null;
                            }
                            if (c.rigidCJ != null)
                            {
                                Component.Destroy(c.rigidCJ);
                                c.rigidCJ = null;
                            }
                        }
                        catch (System.Exception ex)
                        {
                            Main.DebugLog(() => $"Error cleaning up coupler {c?.train?.ID}: {ex.Message}");
                        }
                    }
                }

                bufferJoints.Remove(coupler);
                bufferJoints.Remove(result.otherCoupler);
            }
            catch (System.Exception ex)
            {
                Main.DebugLog(() => $"Error destroying compression joint: {ex.Message}");
                // Still try to remove from dictionary to prevent memory leaks
                bufferJoints.Remove(coupler);
                if (result.otherCoupler != null)
                    bufferJoints.Remove(result.otherCoupler);
            }
        }

        private static void DestroyTensionJoint(Coupler coupler)
        {
            if (coupler == null)
                return;
                
            try
            {
                if (customTensionJoints.TryGetValue(coupler, out var tensionJoint))
                {
                    if (tensionJoint != null)
                    {
                        Main.DebugLog(() => $"TENSION JOINT: Destroying for {coupler.train.ID} {coupler.Position()}");
                        UnityEngine.Object.Destroy(tensionJoint);
                    }
                    customTensionJoints.Remove(coupler);
                    lastJointCreationTime.Remove(coupler); // Clean up timing tracking
                    Main.DebugLog(() => $"TENSION JOINT: Destroyed and removed from dictionary for {coupler.train.ID} {coupler.Position()}, remaining joints: {customTensionJoints.Count}");
                }
                else
                {
                    Main.DebugLog(() => $"TENSION JOINT: No tension joint found to destroy for {coupler.train.ID} {coupler.Position()}");
                }
            }
            catch (System.Exception ex)
            {
                Main.DebugLog(() => $"Error destroying tension joint: {ex.Message}");
                // Still try to remove from dictionaries to prevent memory leaks
                customTensionJoints.Remove(coupler);
                lastJointCreationTime.Remove(coupler);
            }
        }

        // Public method to check if tension joint exists
        public static bool HasTensionJoint(Coupler coupler)
        {
            return coupler != null && customTensionJoints.ContainsKey(coupler);
        }

        // Public method to force create tension joint (used by SaveManager)
        public static void ForceCreateTensionJoint(Coupler coupler)
        {
            if (coupler == null || !coupler.IsCoupled() || coupler.coupledTo == null)
                return;
                
            if (customTensionJoints.ContainsKey(coupler))
                return; // Already exists
                
            Main.DebugLog(() => $"FORCE CREATING tension joint for {coupler.train.ID} {coupler.Position()} -> {coupler.coupledTo.train.ID} {coupler.coupledTo.Position()}");
            CreateTensionJoint(coupler);
            
            // Also create compression joint if needed
            if (coupler.rigidCJ == null && coupler.coupledTo.rigidCJ == null)
                CreateCompressionJoint(coupler, coupler.coupledTo);
        }

        // Public method to check joint states for debugging
        public static void LogJointStates(string context)
        {
            Main.DebugLog(() => $"JOINT STATES ({context}): Tension joints: {customTensionJoints.Count}");
            foreach (var kvp in customTensionJoints.ToList())
            {
                var coupler = kvp.Key;
                var joint = kvp.Value;
                if (coupler?.train != null && joint != null)
                {
                    var distance = joint.connectedBody != null ? 
                        Vector3.Distance(coupler.transform.position, joint.connectedBody.transform.TransformPoint(joint.connectedAnchor)) : 0f;
                    Main.DebugLog(() => $"  {coupler.train.ID} {coupler.Position()} -> {coupler.coupledTo?.train.ID}, distance: {distance:F2}m, joint valid: {joint != null}");
                }
            }
        }

        private static Vector3 JointDelta(Joint joint, bool isFrontCoupler)
        {
            var delta = joint.transform.InverseTransformPoint(joint.connectedBody.transform.TransformPoint(joint.connectedAnchor)) - joint.anchor;
            return isFrontCoupler ? delta : -delta;
        }

        public static void UpdateAllCompressionJoints()
        {
            if (bufferJoints.Count == 0)
                return;

            var springRate = Main.settings.GetSpringRate();
            var damperRate = Main.settings.GetDamperRate();

            var firstJoint = bufferJoints.Values.FirstOrDefault().joint;
            if (firstJoint == null || (firstJoint.linearLimitSpring.spring == springRate && firstJoint.linearLimitSpring.damper == damperRate))
                return;

            foreach (var joint in bufferJoints.Values.Select(x => x.joint))
            {
                joint.linearLimitSpring = new SoftJointLimitSpring
                {
                    spring = springRate,
                    damper = damperRate,
                };
            }
        }

        public static string Position(this Coupler coupler) => coupler.isFrontCoupler ? "front" : "rear";
    }
}
