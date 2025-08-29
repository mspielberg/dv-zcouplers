using System.Collections.Generic;

using HarmonyLib;

using UnityEngine;
using DvMod.ZCouplers.Integrations.Multiplayer;

namespace DvMod.ZCouplers
{
    /// <summary>
    /// Main coupler behavior patches and coordination
    /// </summary>
    public static class CouplerPatches
    {

        /// <summary>
        /// Patch for CreateJoints to use custom joint system
        /// </summary>
        [HarmonyPatch(typeof(Coupler), nameof(Coupler.CreateJoints))]
        public static class CreateJointsPatch
        {
            public static bool Prefix(Coupler __instance)
            {
                // In MP client, never create joints locally unless we're replaying host commands.
                if (Integrations.Multiplayer.MultiplayerIntegration.IsClientActive && !Integrations.Multiplayer.MultiplayerIntegration.ClientAllowsJointOps)
                {
                    return true; // let vanilla run, but our other client guards will prevent joint creation in JointManager
                }
                // Allow tender joints to use original behavior
                if (__instance.train.GetComponent<DV.SteamTenderAutoCoupleMechanism>() != null && !__instance.isFrontCoupler)
                {
                    return true;
                }

                // Prevent joint creation during save loading to avoid physics instability
                if (SaveManager.IsLoadingFromSave)
                {
                    Main.DebugLog(() => $"Skip joint creation during save loading: {__instance.train.ID}");
                    return true;
                }

                // Safety checks to prevent joint creation in unstable conditions
                if (__instance.train.derailed || __instance.coupledTo?.train.derailed == true)
                {
                    Main.DebugLog(() => $"Skip joint creation - train derailed: {__instance.train.ID}");
                    return true;
                }

                var velocity1 = __instance.train.GetComponent<Rigidbody>()?.velocity.magnitude ?? 0f;
                var velocity2 = __instance.coupledTo?.train.GetComponent<Rigidbody>()?.velocity.magnitude ?? 0f;
                if (velocity1 > 5f || velocity2 > 5f)
                {
                    Main.DebugLog(() => $"Skip joint creation - cars moving too fast: {__instance.train.ID} ({velocity1:F1} m/s) to {__instance.coupledTo?.train.ID} ({velocity2:F1} m/s)");
                    return true;
                }

                // Prevent rapid joint recreation
                if (!JointManager.CanCreateJoint(__instance))
                {
                    Main.DebugLog(() => $"Skip joint creation - too soon after last creation: {__instance.train.ID}");
                    return true;
                }

                // Prevent duplicate joint creation
                if (JointManager.HasTensionJoint(__instance) || (__instance.coupledTo != null && JointManager.HasTensionJoint(__instance.coupledTo)))
                {
                    Main.DebugLog(() => $"Skip joint creation - joints already exist: {__instance.train.ID}");
                    return false;
                }

                // Ensure we have a valid coupled partner
                if (__instance.coupledTo == null)
                {
                    Main.DebugLog(() => $"Skip joint creation - no coupled partner: {__instance.train.ID}");
                    return true;
                }

                Main.DebugLog(() => $"Create tension joint: {__instance.train.ID} <-> {__instance.coupledTo.train.ID}");

                // Record the time of joint creation
                JointManager.RecordJointCreation(__instance);

                // Create custom joints
                JointManager.CreateTensionJoint(__instance);
                var breaker = __instance.gameObject.AddComponent<CouplerBreaker>();
                var tensionJoint = JointManager.GetTensionJoint(__instance);
                if (tensionJoint != null)
                    breaker.joint = tensionJoint;
                if (!JointManager.HasCompressionJoint(__instance) && !JointManager.HasCompressionJoint(__instance.coupledTo))
                    JointManager.CreateCompressionJoint(__instance, __instance.coupledTo);
                CouplingScannerPatches.KillCouplingScanner(__instance);
                CouplingScannerPatches.KillCouplingScanner(__instance.coupledTo);
                return false;
            }
        }
    }
}