using HarmonyLib;
using DV;

namespace DvMod.ZCouplers.Integrations.Multiplayer
{
    /// <summary>
    /// Minimal patches to enforce client-side behavior without littering core code.
    /// </summary>
    public static class Patches
    {
        [HarmonyPatch(typeof(JointManager), nameof(JointManager.ForceCreateTensionJoint))]
        public static class PreventForceCreateOnClient
        {
            public static bool Prefix()
            {
                return !MultiplayerIntegration.IsClientActive; // skip on client
            }
        }

            // Prevent the client from running deferred coupler state/joint application,
            // which is intended for SP/host after save load and causes duplicate joint creation when joining MP.
            [HarmonyPatch(typeof(DeferredStateApplicator), nameof(DeferredStateApplicator.StartDeferredCouplerApplication))]
            public static class PreventDeferredStateOnClient
            {
                public static bool Prefix()
                {
                    return !MultiplayerIntegration.IsClientActive; // skip entire routine on client
                }
            }
    }
}
