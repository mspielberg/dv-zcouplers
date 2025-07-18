using DV;
using DV.CabControls;
using DV.CabControls.Spec;
using HarmonyLib;
using System.Collections.Generic;
using System.Reflection;
using DV.ThingTypes;
using UnityEngine;

namespace DvMod.ZCouplers
{
    public static class KnuckleCouplers
    {
        public static readonly bool enabled = Main.settings.couplerType != CouplerType.BufferAndChain;
        private static readonly GameObject? hookPrefab;

        static KnuckleCouplers()
        {
            if (!enabled)
            {
                // don't even try to load assets if knuckle couplers are disabled
                return;
            }
            var bundleStream = typeof(KnuckleCouplers).Assembly.GetManifestResourceStream(typeof(Main), "ZCouplers.assetbundle");
            var bundle = AssetBundle.LoadFromStream(bundleStream);
            CouplerType couplerType = Main.settings.couplerType;
            hookPrefab = bundle.LoadAsset<GameObject>(couplerType.ToString());
            bundle.Unload(false);
            ToggleBuffers(Main.settings.showBuffersWithKnuckles);
        }

        public static void ToggleBuffers(bool visible)
        {
            Main.DebugLog(() => $"Toggling buffer visibility {(visible ? "on" : "off")}");
            // Modify prefabs for any new cars that are instantiated.
            foreach (TrainCarLivery livery in Globals.G.Types.Liveries) 
                ToggleBuffers(livery.prefab, livery, visible);
            // Modify existing cars so the setting can update in real-time.
            if (CarSpawner.Instance == null) return;
            foreach (TrainCar car in CarSpawner.Instance.allCars)
                ToggleBuffers(car.gameObject, car.carLivery, visible);
        }

        private static void ToggleBuffers(GameObject root, TrainCarLivery livery, bool visible)
        {
            MeshRenderer[] renderers = root.GetComponentsInChildren<MeshRenderer>();
            foreach (MeshRenderer renderer in renderers)
                // Search for buffer pads, then buffer stems. Stems aren't named or placed consistently, so we have to generalize.
                if (renderer.name.StartsWith("Buffer_") || renderer.name.Replace("_", "").ToLowerInvariant().Contains("bufferstem"))
                    renderer.enabled = visible;

            Transform colliders = root.transform.Find("[colliders]");
            if (colliders == null)
            {
                Main.DebugLog(() => $"Failed to find [colliders] object on {livery.id}");
                return;
            }

            foreach (string name in new[] { "[walkable]", "[items]" })
            {
                Transform t = colliders.Find(name);
                if (t == null)
                    Main.DebugLog(() => $"Failed to find '{name}' object on {livery.id}");
                else
                    ToggleCapsules(livery, t, visible);
            }
        }

        private static void ToggleCapsules(TrainCarLivery livery, Transform transform, bool visible, byte limit = 4, bool checkLivery = true)
        {
            if (checkLivery && livery.id == "LocoS282A")
            {
                Transform exterior = transform.Find("Exterior");
                if (exterior == null)
                    Main.DebugLog(() => $"Failed to find 'Exterior' object on {livery.id}");
                else
                    ToggleCapsules(livery, exterior, visible, 2, false);

                return;
            }

            CapsuleCollider[] capsules = transform.GetComponentsInChildren<CapsuleCollider>();
            if (capsules.Length < limit)
            {
                Main.DebugLog(() => $"Only found {capsules.Length} capsules on {livery.id}, expected {limit}");
                return;
            }

            for (int i = 0; i < limit; i++)
                capsules[i].enabled = visible;
        }

        private static readonly HashSet<Coupler> unlockedCouplers = new HashSet<Coupler>();

        [HarmonyPatch(typeof(ChainCouplerInteraction), nameof(ChainCouplerInteraction.Entry_Enabled))]
        public static class Entry_EnabledPatch
        {
            public static void Postfix(ChainCouplerInteraction __instance)
            {
                if (!enabled)
                    return;
                CreateHook(__instance);
            }
        }

        [HarmonyPatch(typeof(ChainCouplerInteraction), nameof(ChainCouplerInteraction.Exit_Enabled))]
        public static class Exit_EnabledPatch
        {
            public static void Postfix(ChainCouplerInteraction __instance)
            {
                if (!enabled)
                    return;
                GameObject.Destroy(GetPivot(__instance)?.gameObject);
                pivots.Remove(__instance);
            }
        }

        private static void AdjustPivot(Transform pivot, Transform target)
        {
            pivot.localEulerAngles = Vector3.zero;
            var offset = pivot.InverseTransformPoint(target.position);
            var angle = Mathf.Atan2(offset.x, offset.z) * Mathf.Rad2Deg;
            pivot.localEulerAngles = new Vector3(0, angle, 0);

            offset.y = 0f;
            var distance = offset.magnitude;
            var hook = pivot.Find("hook");
            hook.localPosition = distance / 2 * Vector3.forward;
        }

        [HarmonyPatch(typeof(ChainCouplerInteraction), nameof(ChainCouplerInteraction.Entry_Attached))]
        public static class Entry_AttachedPatch
        {
            public static void Postfix(ChainCouplerInteraction __instance)
            {
                if (!enabled)
                    return;

                __instance.attachedTo = __instance.couplerAdapter.coupler.coupledTo.visualCoupler.chain.GetComponent<ChainCouplerInteraction>();
                __instance.attachedTo.attachedTo = __instance;
            }
        }

        [HarmonyPatch(typeof(ChainCouplerInteraction), nameof(ChainCouplerInteraction.LateUpdate_Attached))]
        public static class LateUpdate_AttachedPatch
        {
            public static void Postfix(ChainCouplerInteraction __instance)
            {
                if (!enabled)
                    return;
                var pivot = GetPivot(__instance);
                var otherPivot = GetPivot(__instance.attachedTo);
                if (pivot != null && otherPivot != null)
                {
                    AdjustPivot(pivot, otherPivot);
                }
            }
        }

        [HarmonyPatch(typeof(ChainCouplerInteraction), nameof(ChainCouplerInteraction.Exit_Attached))]
        public static class Exit_AttachedPatch
        {
            public static void Postfix(ChainCouplerInteraction __instance)
            {
                if (!enabled)
                    return;
                var pivot = GetPivot(__instance);
                if (pivot != null && __instance.couplerAdapter.coupler != null)
                {
                    pivot.localEulerAngles = __instance.couplerAdapter.coupler.transform.localEulerAngles;
                    var hook = pivot.Find("hook");
                    hook.localPosition = PivotLength * Vector3.forward;
                }
                if (__instance.attachedTo != null)
                {
                    __instance.attachedTo.attachedTo = null;
                    __instance.attachedTo = null;
                }
            }
        }

        private static readonly Dictionary<ChainCouplerInteraction, Transform> pivots = new Dictionary<ChainCouplerInteraction, Transform>();

        private static Transform? GetPivot(ChainCouplerInteraction chainScript)
        {
            if (chainScript == null)
                return null;
            pivots.TryGetValue(chainScript, out var pivot);
            return pivot;
        }

        private const float PivotLength = 1.0f;
        private const float HeightOffset = -0.067f;
        private static void CreateHook(ChainCouplerInteraction chainScript)
        {
            var coupler = chainScript.couplerAdapter.coupler;
            var pivot = new GameObject(coupler.isFrontCoupler ? "ZCouplers pivot front" : "ZCouplers pivot rear");
            pivot.transform.SetParent(coupler.transform, false);
            pivot.transform.localPosition = new Vector3(0, HeightOffset, -PivotLength);
            pivot.transform.parent = coupler.train.interior;
            pivots.Add(chainScript, pivot.transform);

            if (hookPrefab == null)
            {
                Main.DebugLog(() => "Hook prefab is null, cannot create knuckle coupler hook");
                return;
            }

            var hook = GameObject.Instantiate(hookPrefab);
            hook.SetActive(false); // defer Awake() until all components are added and initialized
            hook.name = "hook";
            hook.layer = LayerMask.NameToLayer("Interactable");
            hook.transform.SetParent(pivot.transform, false);
            hook.transform.localPosition = PivotLength * Vector3.forward;

            var interactionCollider = hook.GetComponent<BoxCollider>();
            interactionCollider.isTrigger = true;

            var colliderHost = new GameObject("walkable");
            colliderHost.layer = LayerMask.NameToLayer("Train_Walkable");
            colliderHost.transform.SetParent(hook.transform, worldPositionStays: false);
            var walkableCollider = colliderHost.AddComponent<BoxCollider>();
            walkableCollider.center = interactionCollider.center;
            walkableCollider.size = interactionCollider.size;

            var buttonSpec = hook.AddComponent<Button>();
            buttonSpec.createRigidbody = false;
            buttonSpec.useJoints = false;
            buttonSpec.colliderGameObjects = new GameObject[] { hook };

            var infoArea = hook.AddComponent<InfoArea>();
            infoArea.infoType = unlockedCouplers.Contains(coupler) ? KnuckleCouplerLock : KnuckleCouplerUnlock;
            hook.SetActive(true); // this should create an actual Button through excuting
            hook.GetComponent<ButtonBase>().Used += () => OnButtonPressed(chainScript);
        }

        public static InteractionInfoType KnuckleCouplerUnlock = (InteractionInfoType)23000;
        public static InteractionInfoType KnuckleCouplerLock = (InteractionInfoType)23001;

        public static void UnlockCoupler(Coupler coupler, bool viaChainInteraction)
        {
            if (!enabled)
                return;
            if (unlockedCouplers.Contains(coupler))
                return;
            var chainScript = coupler.visualCoupler.chainAdapter.chainScript;
            if (unlockedCouplers.Add(coupler))
                chainScript.PlaySound(chainScript.attachSound, chainScript.transform.position);
            if (GetPivot(chainScript)?.Find("hook")?.GetComponent<InfoArea>() is InfoArea infoArea)
                infoArea.infoType = KnuckleCouplerLock;

            coupler.Uncouple(
                playAudio: true,
                calledOnOtherCoupler: false,
                dueToBrokenCouple: false,
                viaChainInteraction);
        }

        public static void ReadyCoupler(Coupler coupler)
        {
            if (!enabled)
                return;
            if (!unlockedCouplers.Contains(coupler))
                return;
            var chainScript = coupler.visualCoupler.chainAdapter.chainScript;
            if (unlockedCouplers.Remove(coupler))
                chainScript.PlaySound(chainScript.parkSound, chainScript.transform.position);
            if (GetPivot(chainScript)?.Find("hook")?.GetComponent<InfoArea>() is InfoArea infoArea)
                infoArea.infoType = KnuckleCouplerUnlock;
        }

        // Update visual state only without triggering actual uncoupling
        public static void UpdateCouplerVisualState(Coupler coupler, bool locked)
        {
            if (!enabled)
                return;
                
            var chainScript = coupler.visualCoupler?.chainAdapter?.chainScript;
            if (chainScript == null)
                return;
                
            if (locked)
            {
                // Coupler should show as locked (ready to unlock)
                unlockedCouplers.Remove(coupler);
                if (GetPivot(chainScript)?.Find("hook")?.GetComponent<InfoArea>() is InfoArea infoArea)
                    infoArea.infoType = KnuckleCouplerUnlock;
            }
            else
            {
                // Coupler should show as unlocked (ready to couple)
                unlockedCouplers.Add(coupler);
                if (GetPivot(chainScript)?.Find("hook")?.GetComponent<InfoArea>() is InfoArea infoArea)
                    infoArea.infoType = KnuckleCouplerLock;
                    
                // Manually trigger visual disconnection for knuckle couplers
                var pivot = GetPivot(chainScript);
                if (pivot != null)
                {
                    pivot.localEulerAngles = coupler.transform.localEulerAngles;
                    var hook = pivot.Find("hook");
                    if (hook != null)
                    {
                        hook.localPosition = PivotLength * Vector3.forward;
                        Main.DebugLog(() => $"Reset knuckle coupler hook position for {coupler.train.ID} {coupler.Position()}");
                    }
                }
                
                // Clear the attached reference if it exists
                if (chainScript.attachedTo != null)
                {
                    chainScript.attachedTo.attachedTo = null;
                    chainScript.attachedTo = null;
                }
            }
        }

        private static void OnButtonPressed(ChainCouplerInteraction chainScript)
        {
            var coupler = chainScript.couplerAdapter.coupler;
            if (unlockedCouplers.Contains(coupler))
                ReadyCoupler(coupler);
            else
                UnlockCoupler(coupler, viaChainInteraction: true);
        }

        public static bool IsReadyToCouple(Coupler coupler)
        {
            return !unlockedCouplers.Contains(coupler);
        }

        public static bool HasUnlockedCoupler(Trainset trainset)
        {
            if (!enabled)
                return false;
            foreach (var car in trainset.cars)
            {
                if (unlockedCouplers.Contains(car.frontCoupler) || unlockedCouplers.Contains(car.rearCoupler))
                    return true;
            }

            return false;
       }

        [HarmonyPatch(typeof(InteractionText), nameof(InteractionText.GetText))]
        public static class GetTextPatch
        {
            public static bool Prefix(InteractionInfoType infoType, ref string __result)
            {
                if (infoType == KnuckleCouplerUnlock)
                {
                    __result = $"Press {InteractionText.Instance.BtnUse} to unlock coupler";
                    return false;
                }
                if (infoType == KnuckleCouplerLock)
                {
                    __result = $"Press {InteractionText.Instance.BtnUse} to ready coupler";
                    return false;
                }
                return true;
            }
        }

        [HarmonyPatch(typeof(ChainCouplerVisibilityOptimizer), nameof(ChainCouplerVisibilityOptimizer.Enable))]
        public static class ChainCouplerVisibilityOptimizerEnablePatch
        {
            public static void Postfix(ChainCouplerVisibilityOptimizer __instance)
            {
                if (!enabled)
                    return;
                var chainTransform = __instance.chain.transform;
                for (int i = 0; i < chainTransform.childCount; i++)
                    chainTransform.GetChild(i).gameObject.SetActive(false);
            }
        }

        [HarmonyPatch(typeof(ChainCouplerInteraction), nameof(ChainCouplerInteraction.CoupleBrokenExternally))]
        public static class CoupleBrokenExternallyPatch
        {
            public static bool Prefix(ChainCouplerInteraction __instance)
            {
                __instance.UncoupledExternally();
                return false;
            }
        }

        [HarmonyPatch]
        public static class ChainCouplerInteractionSkipPatches
        {
            private static readonly string[] MethodNames = new string[]
            {
                nameof(ChainCouplerInteraction.Entry_Enabled),
                nameof(ChainCouplerInteraction.Exit_Enabled),

                nameof(ChainCouplerInteraction.Entry_Attached),
                nameof(ChainCouplerInteraction.Exit_Attached),
                nameof(ChainCouplerInteraction.LateUpdate_Attached),

                nameof(ChainCouplerInteraction.Entry_Attached_Tight),
                nameof(ChainCouplerInteraction.Exit_Attached_Tight),

                nameof(ChainCouplerInteraction.Entry_Parked),
                nameof(ChainCouplerInteraction.Exit_Parked),
                nameof(ChainCouplerInteraction.Update_Parked),
            };
            public static IEnumerable<MethodBase> TargetMethods()
            {
                foreach (var name in MethodNames)
                    yield return AccessTools.Method(typeof(ChainCouplerInteraction), name);
            }

            public static bool Prefix()
            {
                return !enabled;
            }
        }

        [HarmonyPatch(typeof(ChainCouplerCouplerAdapter), nameof(ChainCouplerCouplerAdapter.OnCoupled))]
        public static class OnCoupledPatch
        {
            public static void Postfix(ChainCouplerCouplerAdapter __instance, CoupleEventArgs e)
            {
                if (!enabled)
                    return;
                    
                Main.DebugLog(() => $"Knuckle OnCoupled: {e.thisCoupler.train.ID}<=>{e.otherCoupler.train.ID},viaChain={e.viaChainInteraction}");
                
                // Update knuckle coupler visual state to show coupled (locked) without triggering uncoupling
                UpdateCouplerVisualState(e.thisCoupler, locked: true);
                UpdateCouplerVisualState(e.otherCoupler, locked: true);
            }
        }

        [HarmonyPatch(typeof(ChainCouplerInteraction), nameof(ChainCouplerInteraction.DetermineNextState))]
        public static class DetermineNextStatePatch
        {
            public static bool Prefix(ChainCouplerInteraction __instance, ref ChainCouplerInteraction.State __result)
            {
                if (!enabled)
                    return true;

                if (__instance.couplerAdapter.IsCoupled())
                {
                    var partner = __instance.couplerAdapter.coupler?.coupledTo?.visualCoupler?.chain?.GetComponent<ChainCouplerInteraction>();
                    if (partner == null)
                    {
                        __result = ChainCouplerInteraction.State.Disabled;
                        return false;
                    }
                    __result = ChainCouplerInteraction.State.Attached_Tight;
                }
                else
                {
                    __result = ChainCouplerInteraction.State.Parked;
                }

                return false;
            }
        }
    }
}
