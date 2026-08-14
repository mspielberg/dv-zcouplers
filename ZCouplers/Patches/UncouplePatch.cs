using System.Collections.Generic;
using DvMod.ZCouplers.Core;
using DvMod.ZCouplers.Core.Helpers;
using DvMod.ZCouplers.Core.Utils;
using DvMod.ZCouplers.Physics;
using DvMod.ZCouplers.Visuals;
using HarmonyLib;
using UnityEngine;

namespace DvMod.ZCouplers.Patches;

[HarmonyPatch(typeof(Coupler), "Uncouple")]
public static class UncouplePatch
{
    private static readonly Dictionary<Coupler, Coroutine> coros = new();
    private static readonly Dictionary<Coupler, Coupler> partnerCouplers = new();
    private static readonly Dictionary<Coupler, bool> wasActuallyCoupled = new();

    public static void Cleanup()
    {
        coros.Clear();
        partnerCouplers.Clear();
        wasActuallyCoupled.Clear();
    }

    private static System.Collections.IEnumerator DelayedVisualStateUpdate(Coupler coupler)
    {
        yield return null;
        HookManager.UpdateHookVisualStateFromCouplerState(coupler);
        Main.DebugLog(() => "Updated visual state for uncoupled coupler: " + coupler.train.ID + " " + coupler.Position());
    }

    public static void Prefix(Coupler __instance)
    {
        wasActuallyCoupled[__instance] = __instance.IsCoupled();

        Main.DebugLog(() => "Uncoupling " + __instance.train.ID + " from " + __instance.coupledTo?.train.ID + " (was coupled: " + wasActuallyCoupled[__instance] + ")");
        if (__instance.coupledTo != null)
            partnerCouplers[__instance] = __instance.coupledTo;

        SaveManager.ClearPendingStatesForCar(__instance.train);
        if (__instance.coupledTo?.train != null)
            SaveManager.ClearPendingStatesForCar(__instance.coupledTo.train);

        coros[__instance] = __instance.jointCoroRigid;
        __instance.jointCoroRigid = null;
        __instance.rigidCJ = null;

        if (__instance.coupledTo != null)
        {
            coros[__instance.coupledTo] = __instance.coupledTo.jointCoroRigid;
            __instance.coupledTo.jointCoroRigid = null;
            __instance.coupledTo.rigidCJ = null;
        }

        JointManager.DestroyTensionJoint(__instance);

        if (__instance.coupledTo?.train != null)
            CollisionHandler.DestroyJointsBetweenCars(__instance.train, __instance.coupledTo.train);

        CollisionHandler.LogRemainingJoints(__instance.train, "after uncoupling");
        if (__instance.coupledTo?.train != null)
            CollisionHandler.LogRemainingJoints(__instance.coupledTo.train, "after uncoupling");

        CouplingScannerPatches.RestartCouplingScanner(__instance);
        if (__instance.coupledTo != null)
        {
            CouplingScannerPatches.RestartCouplingScanner(__instance.coupledTo);
            RecouplingPrevention.RecordUncoupling(__instance, __instance.coupledTo);
            CollisionHandler.EnableFakeBufferColliders(__instance, __instance.coupledTo);
        }

        Main.DebugLog(() => "Completed uncoupling cleanup for " + __instance.train.ID);
    }

    public static void Postfix(Coupler __instance)
    {
        try
        {
            if (partnerCouplers.TryGetValue(__instance, out Coupler partner) && partner != null)
            {
                AirSystemAutomation.TryAutoDisconnect(__instance, partner);
                AirSystemAutomation.TryAutoDisconnectMU(__instance, partner);
            }
        }
        catch { }

        if (coros.TryGetValue(__instance, out Coroutine value2))
        {
            if (__instance.IsCoupled())
            {
                __instance.jointCoroRigid = value2;
                Main.ErrorLog(() => "Restored jointCoroRigid for " + __instance.train.ID + " - uncoupling failed");
            }
            coros.Remove(__instance);
        }

        if (partnerCouplers.TryGetValue(__instance, out Coupler partnerCoupler))
        {
            if (coros.TryGetValue(partnerCoupler, out Coroutine value4))
            {
                if (partnerCoupler.IsCoupled())
                    partnerCoupler.jointCoroRigid = value4;
                coros.Remove(partnerCoupler);
            }

            if (!partnerCoupler.IsCoupled())
            {
                partnerCoupler.state = ChainCouplerInteraction.State.Parked;
                Main.DebugLog(() => "Reset partner coupler state to Parked: " + partnerCoupler.train.ID + " " + partnerCoupler.Position());
            }

            if (partnerCoupler.visualCoupler?.chainAdapter?.chainScript != null &&
                partnerCoupler.visualCoupler.chainAdapter.chainScript.gameObject.activeInHierarchy)
            {
                partnerCoupler.visualCoupler.chainAdapter.chainScript.StartCoroutine(DelayedVisualStateUpdate(partnerCoupler));
            }

            partnerCouplers.Remove(__instance);
        }

        if (!__instance.IsCoupled())
        {
            if (wasActuallyCoupled.TryGetValue(__instance, out bool wasCoupled) && wasCoupled)
            {
                __instance.state = ChainCouplerInteraction.State.Dangling;
                Main.DebugLog(() => "Reset coupler state to Dangling: " + __instance.train.ID);
            }
            else
            {
                __instance.state = ChainCouplerInteraction.State.Parked;
                Main.DebugLog(() => "Reset coupler state to Parked: " + __instance.train.ID);
            }
        }

        wasActuallyCoupled.Remove(__instance);
        if (__instance.visualCoupler?.chainAdapter?.chainScript != null &&
            __instance.visualCoupler.chainAdapter.chainScript.gameObject.activeInHierarchy)
        {
            __instance.visualCoupler.chainAdapter.chainScript.StartCoroutine(DelayedVisualStateUpdate(__instance));
        }
    }
}
