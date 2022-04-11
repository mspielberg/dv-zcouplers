using System.Collections.Generic;
using System.IO;
using System.Reflection;
using DV.CabControls;
using DV.CabControls.Spec;
using HarmonyLib;
using UnityEngine;

namespace DvMod.ZCouplers
{
    public static class KnuckleCouplers
    {
        public static readonly bool enabled = Main.settings.couplerType == CouplerType.JanneyKnuckle;
        private static readonly GameObject hookPrefab;

        static KnuckleCouplers()
        {
            var bundle = AssetBundle.LoadFromFile(Path.Combine(Main.mod!.Path, "knucklecoupler"));
            hookPrefab = bundle.LoadAsset<GameObject>("hook");
            bundle.Unload(false);
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
                if (pivot != null)
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

            var pivot = new GameObject("ZCouplers pivot");
            pivot.transform.SetParent(coupler.transform, false);
            pivot.transform.localPosition = new Vector3(0, HeightOffset, -PivotLength);
            pivot.transform.parent = coupler.train.interior;
            pivots.Add(chainScript, pivot.transform);

            var hook = GameObject.Instantiate(hookPrefab);
            hook.name = "hook";
            hook.layer = LayerMask.NameToLayer("Interactable");
            hook.transform.SetParent(pivot.transform, false);
            hook.transform.localPosition = PivotLength * Vector3.forward;

            var collider = hook.GetComponent<MeshCollider>();
            collider.convex = true;
            collider.isTrigger = true;

            var buttonSpec = hook.AddComponent<Button>();
            buttonSpec.createRigidbody = false;
            buttonSpec.useJoints = false;
            hook.GetComponent<ButtonBase>().Used = () => OnButtonPressed(chainScript);

            var infoArea = hook.AddComponent<InfoArea>();
            infoArea.infoType = unlockedCouplers.Contains(coupler) ? KnuckleCouplerLock : KnuckleCouplerUnlock;
        }

        private static void OnButtonPressed(ChainCouplerInteraction chainScript)
        {
            var coupler = chainScript.couplerAdapter.coupler;
            if (unlockedCouplers.Contains(coupler))
            {
                unlockedCouplers.Remove(coupler);
                chainScript.PlaySound(chainScript.parkSound, chainScript.transform.position);
            }
            else
            {
                unlockedCouplers.Add(coupler);
                chainScript.PlaySound(chainScript.attachSound, chainScript.transform.position);
            }

            Main.DebugLog(() => "Searching for InfoArea");
            if (GetPivot(chainScript)?.Find("hook")?.GetComponent<InfoArea>() is InfoArea infoArea)
            {
                infoArea.infoType = infoArea.infoType == KnuckleCouplerLock ? KnuckleCouplerUnlock : KnuckleCouplerLock;
                Main.DebugLog(() => $"Found hook. Set infoType to {infoArea.infoType} on {coupler.train.ID} {coupler.isFrontCoupler}");
            }

            coupler.Uncouple(
                playAudio: true,
                calledOnOtherCoupler: false,
                dueToBrokenCouple: false,
                viaChainInteraction: true);
        }

        public static InteractionInfoType KnuckleCouplerUnlock = (InteractionInfoType)23000;
        public static InteractionInfoType KnuckleCouplerLock = (InteractionInfoType)23001;

        public static bool IsReadyToCouple(Coupler coupler)
        {
            return !unlockedCouplers.Contains(coupler);
        }

        [HarmonyPatch(typeof(InteractionTextControllerNonVr), nameof(InteractionTextControllerNonVr.GetText))]
        public static class GetTextPatch
        {
            public static bool Prefix(InteractionInfoType infoType, ref string __result)
            {
                if (infoType == KnuckleCouplerUnlock)
                {
                    __result = $"Press {InteractionTextControllerNonVr.Btn_Use} to unlock coupler";
                    return false;
                }
                if (infoType == KnuckleCouplerLock)
                {
                    __result = $"Press {InteractionTextControllerNonVr.Btn_Use} to ready coupler";
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