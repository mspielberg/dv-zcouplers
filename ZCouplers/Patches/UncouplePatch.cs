using System.Collections.Generic;

using HarmonyLib;

using UnityEngine;

namespace DvMod.ZCouplers;

[HarmonyPatch(typeof(Coupler), "Uncouple")]
public static class UncouplePatch
{
    private static readonly Dictionary<Coupler, ConfigurableJoint> compressionJoints = new Dictionary<Coupler, ConfigurableJoint>();

    private static readonly Dictionary<Coupler, Coroutine> coros = new Dictionary<Coupler, Coroutine>();

    private static readonly Dictionary<Coupler, Coupler> partnerCouplers = new Dictionary<Coupler, Coupler>();

    public static void Prefix(Coupler __instance)
    {
        Main.DebugLog(() => "Uncoupling " + __instance.train.ID + " from " + __instance.coupledTo?.train.ID);
        if (__instance.coupledTo != null)
        {
            partnerCouplers[__instance] = __instance.coupledTo;
        }
        SaveManager.ClearPendingStatesForCar(__instance.train);
        if (__instance.coupledTo?.train != null)
        {
            SaveManager.ClearPendingStatesForCar(__instance.coupledTo.train);
        }
        compressionJoints[__instance] = __instance.rigidCJ;
        __instance.rigidCJ = null;
        coros[__instance] = __instance.jointCoroRigid;
        __instance.jointCoroRigid = null;
        if (__instance.coupledTo != null)
        {
            compressionJoints[__instance.coupledTo] = __instance.coupledTo.rigidCJ;
            __instance.coupledTo.rigidCJ = null;
            coros[__instance.coupledTo] = __instance.coupledTo.jointCoroRigid;
            __instance.coupledTo.jointCoroRigid = null;
            Main.DebugLog(() => "Stored partner coupler joints for " + __instance.coupledTo.train.ID);
        }
        JointManager.DestroyTensionJoint(__instance);
        JointManager.ConvertCompressionJointToBufferOnly(__instance);
        if (__instance.coupledTo?.train != null)
        {
            CollisionHandler.DestroyJointsBetweenCars(__instance.train, __instance.coupledTo.train);
        }
        CollisionHandler.LogRemainingJoints(__instance.train, "after uncoupling");
        if (__instance.coupledTo?.train != null)
        {
            CollisionHandler.LogRemainingJoints(__instance.coupledTo.train, "after uncoupling");
        }
        CouplingScannerPatches.RestartCouplingScanner(__instance);
        if (__instance.coupledTo != null)
        {
            CouplingScannerPatches.RestartCouplingScanner(__instance.coupledTo);
            CouplingScannerPatches.SeparateCarsAfterUncoupling(__instance, __instance.coupledTo);
        }
        Main.DebugLog(() => "Completed uncoupling cleanup for " + __instance.train.ID);
    }

    public static void Postfix(Coupler __instance)
    {
        // Auto air-hose disconnection for Full Automatic Mode (Schaku forces this on)
        try
        {
            if (partnerCouplers.TryGetValue(__instance, out Coupler partner) && partner != null)
            {
                AirSystemAutomation.TryAutoDisconnect(__instance, partner);
                AirSystemAutomation.TryAutoDisconnectMU(__instance, partner);
            }
        }
        catch { }

        if (compressionJoints.TryGetValue(__instance, out ConfigurableJoint value))
        {
            if (__instance.IsCoupled())
            {
                __instance.rigidCJ = value;
                Main.DebugLog(() => "Restored rigidCJ for " + __instance.train.ID + " - uncoupling failed, still coupled to " + __instance.coupledTo?.train.ID);
            }
            else
            {
                if (value != null)
                {
                    Object.Destroy(value);
                }
                Main.DebugLog(() => "Destroyed stored rigidCJ for " + __instance.train.ID + " - uncoupling succeeded");
            }
            compressionJoints.Remove(__instance);
        }
        if (coros.TryGetValue(__instance, out Coroutine value2))
        {
            if (__instance.IsCoupled())
            {
                __instance.jointCoroRigid = value2;
                Main.DebugLog(() => "Restored jointCoroRigid for " + __instance.train.ID + " - uncoupling failed");
            }
            else
            {
                Main.DebugLog(() => "Did not restore jointCoroRigid for " + __instance.train.ID + " - uncoupling succeeded");
            }
            coros.Remove(__instance);
        }
        if (partnerCouplers.TryGetValue(__instance, out Coupler partnerCoupler))
        {
            if (compressionJoints.TryGetValue(partnerCoupler, out ConfigurableJoint value3))
            {
                if (partnerCoupler.IsCoupled())
                {
                    partnerCoupler.rigidCJ = value3;
                    Main.DebugLog(() => "Restored partner rigidCJ for " + partnerCoupler.train.ID + " - uncoupling failed, still coupled to " + partnerCoupler.coupledTo?.train.ID);
                }
                else
                {
                    if (value3 != null)
                    {
                        Object.Destroy(value3);
                    }
                    Main.DebugLog(() => "Destroyed stored partner rigidCJ for " + partnerCoupler.train.ID + " - uncoupling succeeded");
                }
                compressionJoints.Remove(partnerCoupler);
            }
            if (coros.TryGetValue(partnerCoupler, out Coroutine value4))
            {
                if (partnerCoupler.IsCoupled())
                {
                    partnerCoupler.jointCoroRigid = value4;
                    Main.DebugLog(() => "Restored partner jointCoroRigid for " + partnerCoupler.train.ID + " - uncoupling failed");
                }
                else
                {
                    Main.DebugLog(() => "Did not restore partner jointCoroRigid for " + partnerCoupler.train.ID + " - uncoupling succeeded");
                }
                coros.Remove(partnerCoupler);
            }
            KnuckleCouplers.UpdateCouplerVisualState(partnerCoupler, locked: false);
            if (!partnerCoupler.IsCoupled())
            {
                partnerCoupler.state = ChainCouplerInteraction.State.Parked;
                Main.DebugLog(() => "Reset partner coupler state to Parked: " + partnerCoupler.train.ID + " " + partnerCoupler.Position());
            }
            partnerCouplers.Remove(__instance);
            Main.DebugLog(() => "Updated visual state for both uncoupled couplers: " + __instance.train.ID + " and " + partnerCoupler.train.ID);
        }
        KnuckleCouplers.UpdateCouplerVisualState(__instance, locked: false);
        if (!__instance.IsCoupled())
        {
            __instance.state = ChainCouplerInteraction.State.Parked;
            Main.DebugLog(() => "Reset coupler state to Parked: " + __instance.train.ID + " " + __instance.Position());
        }
        if (!partnerCouplers.ContainsKey(__instance))
        {
            Main.DebugLog(() => "Updated visual state for uncoupled coupler: " + __instance.train.ID + " " + __instance.Position());
        }
        else
        {
            Main.DebugLog(() => "Updated visual state for uncoupled coupler: " + __instance.train.ID + " " + __instance.Position());
        }
    }
}