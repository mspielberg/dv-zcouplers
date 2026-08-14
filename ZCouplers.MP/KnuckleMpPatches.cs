using System;
using System.Collections;
using DvMod.ZCouplers.Physics;
using DvMod.ZCouplers.Visuals;
using HarmonyLib;

namespace DvMod.ZCouplers
{
    /// <summary>
    /// Harmony patches that bridge ZCouplers behavior to Multiplayer without touching core files.
    /// Single-joint (tension-only) replication.
    /// </summary>
    [HarmonyPatch]
    public static class KnuckleMpPatches
    {
        [HarmonyPatch(typeof(Coupler), nameof(Coupler.Uncouple), new Type[] { typeof(bool), typeof(bool), typeof(bool), typeof(bool) })]
        private static class ReplicateUncouple
        {
            public static void Prefix(Coupler __instance, out Coupler? __state)
            {
                __state = __instance?.coupledTo;
            }

            public static void Postfix(Coupler __instance, Coupler? __state)
            {
                if (!MultiplayerIntegration.IsHost || __instance == null)
                    return;
                if (!__instance.IsCoupled() && __state != null)
                    MultiplayerIntegration.HostBroadcastJointDestroy(__instance, __state, JointKind.Tension);
            }
        }

        [HarmonyPatch(typeof(HookManager), "OnButtonPressed", MethodType.Normal)]
        private static class HookButtonPatch
        {
            public static bool Prefix(ChainCouplerInteraction chainScript)
            {
                if (chainScript?.couplerAdapter?.coupler == null)
                    return true;

                if (!MultiplayerIntegration.IsClientActive)
                    return true;

                var coupler = chainScript.couplerAdapter.coupler;
                var isParked = coupler.state == ChainCouplerInteraction.State.Parked;

                if (!isParked && coupler.IsCoupled())
                {
                    JointManager.DestroyTensionJoint(coupler);
                    JointManager.DestroyUntrackedJoints(coupler.train.gameObject);
                    if (coupler.coupledTo != null)
                        JointManager.DestroyUntrackedJoints(coupler.coupledTo.train.gameObject);
                }

                MultiplayerIntegration.SendCouplerToggleRequest(coupler, isParked);
                return false;
            }

            public static void Postfix(ChainCouplerInteraction chainScript)
            {
                if (!MultiplayerIntegration.IsHost)
                    return;

                var coupler = chainScript?.couplerAdapter?.coupler;
                if (coupler == null)
                    return;

                chainScript?.StartCoroutine(ReplicateNextFrame(chainScript));
            }

            private static IEnumerator ReplicateNextFrame(ChainCouplerInteraction chainScript)
            {
                yield return null;
                var coupler = chainScript?.couplerAdapter?.coupler;
                if (coupler != null)
                    MultiplayerIntegration.HostMaybeReplicate(coupler);
            }
        }

        [HarmonyPatch(typeof(ChainCouplerInteraction), nameof(ChainCouplerInteraction.DetermineNextState))]
        private static class DetermineNextStateReplicate
        {
            public static void Postfix(ChainCouplerInteraction __instance)
            {
                if (!MultiplayerIntegration.IsHost)
                    return;

                var coupler = __instance?.couplerAdapter?.coupler;
                if (coupler != null)
                    MultiplayerIntegration.HostMaybeReplicate(coupler);
            }
        }

        [HarmonyPatch(typeof(JointManager), nameof(JointManager.CreateTensionJoint))]
        private static class PreventTensionJointOnClient
        {
            public static bool Prefix()
            {
                return !MultiplayerIntegration.IsClientActive || MultiplayerIntegration.ClientAllowsJointOps;
            }
        }

        [HarmonyPatch(typeof(JointManager), nameof(JointManager.CreateTensionJoint))]
        private static class ReplicateTensionJointCreate
        {
            public static void Prefix(Coupler coupler, out bool __state)
            {
                __state = coupler != null && JointManager.HasTensionJoint(coupler);
            }

            public static void Postfix(Coupler coupler, bool __state)
            {
                if (!MultiplayerIntegration.IsHost || coupler == null || coupler.coupledTo == null)
                    return;
                if (!__state && JointManager.HasTensionJoint(coupler))
                    MultiplayerIntegration.HostBroadcastJointCreate(coupler, coupler.coupledTo, JointKind.Tension);
            }
        }

        [HarmonyPatch(typeof(JointManager), nameof(JointManager.DestroyTensionJoint))]
        private static class ReplicateTensionJointDestroy
        {
            public static void Prefix(Coupler coupler, out (bool had, Coupler? partner) __state)
            {
                var had = coupler != null && JointManager.HasTensionJoint(coupler);
                var partner = coupler?.coupledTo;
                __state = (had, partner);
            }

            public static void Postfix(Coupler coupler, (bool had, Coupler? partner) __state)
            {
                if (!MultiplayerIntegration.IsHost || coupler == null)
                    return;
                if (__state.had && !JointManager.HasTensionJoint(coupler))
                {
                    var other = __state.partner;
                    if (other != null)
                        MultiplayerIntegration.HostBroadcastJointDestroy(coupler, other, JointKind.Tension);
                }
            }
        }
    }
}
