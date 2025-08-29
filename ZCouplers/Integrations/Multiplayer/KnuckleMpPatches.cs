using HarmonyLib;
using DV;
using DV.CabControls;
using System.Collections;

namespace DvMod.ZCouplers.Integrations.Multiplayer
{
    /// <summary>
    /// Harmony patches that bridge ZCouplers behavior to Multiplayer without touching core files.
    /// </summary>
    [HarmonyPatch]
    public static class KnuckleMpPatches
    {
        // Intercept button press path to route through MP when client
        [HarmonyPatch(typeof(HookManager), methodName: "OnButtonPressed")]
        private static class HookButtonPatch
        {
            public static bool Prefix(ChainCouplerInteraction chainScript)
            {
                if (chainScript?.couplerAdapter?.coupler == null)
                    return true;

                if (!MultiplayerIntegration.IsClientActive)
                    return true; // let core handle SP/Host

                var coupler = chainScript.couplerAdapter.coupler;
                bool isParked = coupler.state == ChainCouplerInteraction.State.Parked;
                bool desiredLocked = isParked; // parked -> lock; else unlock

                MultiplayerIntegration.SendCouplerToggleRequest(coupler, desiredLocked);
                return false; // handled on client
            }

                // After the host runs the original button logic, replicate to clients.
                public static void Postfix(ChainCouplerInteraction chainScript)
                {
                    if (!MultiplayerIntegration.IsHost)
                        return;

                    var coupler = chainScript?.couplerAdapter?.coupler;
                    if (coupler == null)
                        return;

                    // For unlocks, state flips to Parked after Uncouple completes; wait one frame.
                    chainScript.StartCoroutine(ReplicateNextFrame(chainScript));
                }

                private static IEnumerator ReplicateNextFrame(ChainCouplerInteraction chainScript)
                {
                    yield return null; // wait a frame so state reflects the final result
                    var coupler = chainScript?.couplerAdapter?.coupler;
                    if (coupler != null)
                        MultiplayerIntegration.HostMaybeReplicate(coupler);
                }
        }

        // Replicate on host when the game asks for next state
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

        // Prevent joint creation on client unless we're replaying a host packet
        [HarmonyPatch(typeof(JointManager), nameof(JointManager.CreateTensionJoint))]
        private static class PreventTensionJointOnClient
        {
            public static bool Prefix()
            {
                return !MultiplayerIntegration.IsClientActive || MultiplayerIntegration.ClientAllowsJointOps;
            }
        }

        [HarmonyPatch(typeof(JointManager), nameof(JointManager.CreateCompressionJoint))]
        private static class PreventCompressionJointOnClient
        {
            public static bool Prefix()
            {
                return !MultiplayerIntegration.IsClientActive || MultiplayerIntegration.ClientAllowsJointOps;
            }
        }

        // Host broadcast when creating joints
        [HarmonyPatch(typeof(JointManager), nameof(JointManager.CreateTensionJoint))]
        private static class ReplicateTensionJointCreate
        {
            // capture pre state
            public static void Prefix(Coupler coupler, out bool __state)
            {
                __state = coupler != null && JointManager.HasTensionJoint(coupler);
            }
            public static void Postfix(Coupler coupler, bool __state)
            {
                if (!MultiplayerIntegration.IsHost || coupler == null || coupler.coupledTo == null)
                    return;
                // only broadcast if it didn't exist before and now exists
                if (!__state && JointManager.HasTensionJoint(coupler))
                    MultiplayerIntegration.HostBroadcastJointCreate(coupler, coupler.coupledTo, JointKind.Tension);
            }
        }

        [HarmonyPatch(typeof(JointManager), nameof(JointManager.CreateCompressionJoint))]
        private static class ReplicateCompressionJointCreate
        {
            public static void Prefix(Coupler a, out bool __state)
            {
                __state = a != null && JointManager.HasCompressionJoint(a);
            }
            public static void Postfix(Coupler a, Coupler b, bool __state)
            {
                if (!MultiplayerIntegration.IsHost || a == null || b == null)
                    return;
                if (!__state && JointManager.HasCompressionJoint(a))
                    MultiplayerIntegration.HostBroadcastJointCreate(a, b, JointKind.Compression);
            }
        }

        // Host broadcast when destroying joints
        [HarmonyPatch(typeof(JointManager), nameof(JointManager.DestroyTensionJoint))]
        private static class ReplicateTensionJointDestroy
        {
            public static void Prefix(Coupler coupler, out bool __state)
            {
                __state = coupler != null && JointManager.HasTensionJoint(coupler);
            }
            public static void Postfix(Coupler coupler, bool __state)
            {
                if (!MultiplayerIntegration.IsHost || coupler == null)
                    return;
                if (__state && !JointManager.HasTensionJoint(coupler))
                {
                    var other = coupler.coupledTo;
                    if (other != null)
                        MultiplayerIntegration.HostBroadcastJointDestroy(coupler, other, JointKind.Tension);
                }
            }
        }

        [HarmonyPatch(typeof(JointManager), nameof(JointManager.DestroyCompressionJoint))]
        private static class ReplicateCompressionJointDestroy
        {
            public static void Prefix(Coupler coupler, out Coupler __state)
            {
                __state = null;
                if (coupler != null && JointManager.bufferJoints.TryGetValue(coupler, out var tuple))
                    __state = tuple.otherCoupler;
            }
            public static void Postfix(Coupler coupler, Coupler __state)
            {
                if (!MultiplayerIntegration.IsHost || coupler == null)
                    return;
                // if it was paired before and now it's gone, broadcast destroy
                bool had = __state != null;
                bool hasNow = JointManager.bufferJoints.ContainsKey(coupler);
                if (had && !hasNow && __state != null)
                    MultiplayerIntegration.HostBroadcastJointDestroy(coupler, __state, JointKind.Compression);
            }
        }
    }
}
