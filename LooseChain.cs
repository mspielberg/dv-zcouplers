using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace DvMod.ZCouplers
{
    public static class LooseChain
    {
        public static readonly bool enabled = Main.settings.coupleOnChainHooked;
        private static readonly HashSet<Coupler> looseCouplers = new HashSet<Coupler>();

        [HarmonyPatch(typeof(ChainCouplerInteraction), nameof(ChainCouplerInteraction.Entry_Attached_Loose))]
        public static class EntryAttachedLoosePatch
        {
            public static void Postfix(ChainCouplerInteraction __instance)
            {
                if (!enabled)
                    return;
                __instance.couplerAdapter.TryCouple();
            }
        }

        [HarmonyPatch(typeof(ChainCouplerInteraction), nameof(ChainCouplerInteraction.Entry_Being_Dragged))]
        public static class EntryBeingDraggedPatch
        {
            public static void Prefix(ChainCouplerInteraction __instance)
            {
                if (!enabled)
                    return;
                looseCouplers.Add(__instance.couplerAdapter.coupler);
                __instance.couplerAdapter.TryUncouple();
            }
        }

        [HarmonyPatch(typeof(ChainCouplerInteraction), nameof(ChainCouplerInteraction.Update_Attached_Loose))]
        public static class UpdateAttachedLoosePatch
        {
            public static bool Prefix()
            {
                return !enabled;
            }
        }

        [HarmonyPatch(typeof(ChainCouplerInteraction), nameof(ChainCouplerInteraction.Entry_Attached_Tight))]
        public static class EntryAttachedTightPatch
        {
            public static void Postfix(ChainCouplerInteraction __instance)
            {
                if (!enabled)
                    return;
                looseCouplers.Remove(__instance.couplerAdapter.coupler);
                Couplers.TightenChain(__instance.couplerAdapter.coupler);
            }
        }

        [HarmonyPatch(typeof(ChainCouplerInteraction), nameof(ChainCouplerInteraction.Entry_Attached_Loose))]
        public static class EntryAttachedLooseningUncouplePatch
        {
            public static void Postfix(ChainCouplerInteraction __instance)
            {
                if (!enabled)
                    return;
                looseCouplers.Add(__instance.couplerAdapter.coupler);
                Couplers.LoosenChain(__instance.couplerAdapter.coupler);
            }
        }

        [HarmonyPatch]
        public static class ChainCouplerInteractionPatch
        {
            public static IEnumerable<MethodBase> TargetMethods()
            {
                if (!enabled)
                    yield break;
                yield return AccessTools.Method(typeof(ChainCouplerInteraction), nameof(ChainCouplerInteraction.Entry_Attached_Tightening_Couple));
                yield return AccessTools.Method(typeof(ChainCouplerInteraction), nameof(ChainCouplerInteraction.Entry_Attached_Loosening_Uncouple));
            }

            public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                var insts = new List<CodeInstruction>(instructions);
                var index = insts.FindIndex(ci => ci.LoadsField(AccessTools.DeclaredField(typeof(ChainCouplerInteraction), nameof(ChainCouplerInteraction.couplerAdapter))));
                if (index >= 0)
                {
                    insts.RemoveRange(index - 1, 3);
                }
                return insts;
            }
        }

        [HarmonyPatch(typeof(ChainCouplerInteraction), nameof(ChainCouplerInteraction.Exit_Attached))]
        public static class ExitAttachedPatch
        {
            public static bool Prefix(ChainCouplerInteraction __instance)
            {
                __instance.attachedIK.solver.target = null;
                __instance.attachedTo = null;
                __instance.closestAttachPoint.SetAttachState(attached: false);
                __instance.screwButtonBase.Used -= __instance.OnScrewButtonUsed;
                __instance.screwButtonBase = null;
                __instance.screwButton.SetActive(value: false);
                __instance.GetComponent<HackIK>().target = 1f;
                return false;
            }
        }

        [HarmonyPatch(typeof(ChainCouplerInteraction), nameof(ChainCouplerInteraction.DetermineNextState))]
        public static class DetermineNextStatePatch
        {
            public static bool Prefix(ChainCouplerInteraction __instance, ref ChainCouplerInteraction.State __result)
            {
                if (!enabled)
                    return true;
                bool flag = Vector3.Distance(__instance.chainRingAnchor.position, __instance.parkedAnchor.position) < 0.2f;
                bool flag2 = (bool)__instance.closestAttachPoint &&
                    __instance.closestAttachPoint.isActiveAndEnabled &&
                    Vector3.SqrMagnitude(__instance.ownAttachPoint.transform.position - __instance.closestAttachPoint.transform.position) < ChainCouplerInteraction.AUTO_DETACH_DISTANCE_SQR &&
                    (Vector3.Distance(__instance.chainRingAnchor.position, __instance.closestAttachPoint.transform.position) < 0.1f ||
                        Vector3.Dot(__instance.transform.forward, __instance.chainRingAnchor.position - __instance.closestAttachPoint.transform.position) > 0f);
                if (__instance.couplerAdapter.IsCoupled())
                {
                    ChainCouplerInteraction chainCouplerInteraction = __instance.couplerAdapter.coupler.coupledTo?.visualCoupler?.chain?.GetComponent<ChainCouplerInteraction>()!;
                    if (!chainCouplerInteraction)
                    {
                        __result = ChainCouplerInteraction.State.Disabled;
                        return false;
                    }
                    __instance.closestAttachPoint = chainCouplerInteraction.ownAttachPoint;
                    if (chainCouplerInteraction.fsm?.IsInState(ChainCouplerInteraction.State.Attached) == true
                        && __instance.attachedTo?.attachedTo == __instance)
                    {
                        __result = ChainCouplerInteraction.State.Other_Attached_Parked;
                        return false;
                    }
                    if (__instance.couplerAdapter.coupler.springyCJ != null)
                    {
                        __result = looseCouplers.Contains(__instance.couplerAdapter.coupler)
                            ? ChainCouplerInteraction.State.Attached_Loose
                            : ChainCouplerInteraction.State.Attached_Tight;
                        return false;
                    }
                }
                if (flag)
                {
                    __result = ChainCouplerInteraction.State.Parked;
                    return false;
                }
                if (flag2)
                {
                    __result = ChainCouplerInteraction.State.Attached_Loose;
                    return false;
                }
                __result = ChainCouplerInteraction.State.Dangling;
                return false;
            }
        }

        [HarmonyPatch(typeof(ChainCouplerCouplerAdapter), nameof(ChainCouplerCouplerAdapter.OnCoupled))]
        public static class OnCoupledPatch
        {
            public static bool Prefix(ChainCouplerCouplerAdapter __instance, CoupleEventArgs e)
            {
                if (!enabled)
                    return true;
                Main.DebugLog(() => $"OnCoupled: {e.thisCoupler.train.ID}<=>{e.otherCoupler.train.ID},viaChain={e.viaChainInteraction}");
                if (!e.viaChainInteraction)
                {
                    __instance.chainScript.CoupledExternally(e.otherCoupler);
                }
                return false;
            }
        }

        [HarmonyPatch(typeof(ChainCouplerCouplerAdapter), nameof(ChainCouplerCouplerAdapter.OnUncoupled))]
        public static class OnUncoupledPatch
        {
            public static bool Prefix(ChainCouplerCouplerAdapter __instance, UncoupleEventArgs e)
            {
                if (!enabled)
                    return true;
                if (e.dueToBrokenCouple)
                    __instance.chainScript.CoupleBrokenExternally();
                else if (!e.viaChainInteraction)
                    __instance.chainScript.UncoupledExternally();
                return false;
            }
        }

        [HarmonyPatch(typeof(ChainCouplerInteraction), nameof(ChainCouplerInteraction.MakeFSM))]
        public static class MakeFSMPatch
        {
            public static void Postfix(
                ChainCouplerInteraction __instance,
                ref Stateless.StateMachine<ChainCouplerInteraction.State, ChainCouplerInteraction.Trigger> __result)
            {
                if (!enabled)
                    return;
                var car = TrainCar.Resolve(__instance.gameObject);
                var frontRear = __instance.couplerAdapter.coupler.isFrontCoupler ? "front" : "rear";
                __result.OnTransitioned((t) => Main.DebugLog(() => $"{car.ID} ({frontRear}): {t.Source} -> {t.Destination} ({t.Trigger})"));
            }
        }
    }
}