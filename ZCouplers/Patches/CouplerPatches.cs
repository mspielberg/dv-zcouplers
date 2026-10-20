using DV.ThingTypes;
using DvMod.ZCouplers.Core;
using DvMod.ZCouplers.Core.Utils;
using DvMod.ZCouplers.Physics;
using HarmonyLib;
using UnityEngine;

namespace DvMod.ZCouplers.Patches
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
			// In MP client, prevent vanilla joint creation when custom joints will be
			// created by our MP packet handlers. This prevents vanilla+custom joint conflict
			// that causes loose couplers.
			if (MpShim.IsClientActive && !MpShim.ClientAllowsJointOps)
			{
				// Check if we already have custom joints for this coupler - if so, skip vanilla
				if (JointManager.HasTensionJoint(__instance) ||
				    (__instance.coupledTo != null && JointManager.HasTensionJoint(__instance.coupledTo)))
				{
					return false; // custom joints exist, skip vanilla
				}
				// No custom joints yet - let vanilla run so DV-MP coupling works
				// Our ApplyJointCreate will replace these with custom ones when the packet arrives
				return true;
			}
				// Allow tender joints to use original behavior
				if (__instance.train.GetComponent<DV.SteamTenderAutoCoupleMechanism>() != null && !__instance.isFrontCoupler)
				{
					return true;
				}

                if (Main.IsCCLLoaded)
                {
	                var cclType = System.Type.GetType("CCL.Importer.Components.CarAutoCouplerInternal, CCL.Importer");
	                if (cclType != null && __instance.train.GetComponent(cclType) != null && !__instance.isFrontCoupler && CarTypes.IsLocomotive(__instance.train.carLivery))
	                {
		                Main.DebugLog(() => $"{__instance.train.ID} is a CCL Loco with CarAutoCouplerInternal, skipping");
		                return true;
	                }
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

                if (__instance.coupledTo != null)
                {
                    var safeSpeed = Main.settings.GetSafeCouplingSpeed();
                    var closingSpeed = RecouplingPrevention.GetClosingSpeed(__instance, __instance.coupledTo);
                    if (closingSpeed > safeSpeed)
                    {
                        Main.DebugLog(() => $"Skip joint creation - cars closing too fast: {__instance.train.ID} <-> {__instance.coupledTo.train.ID} at {closingSpeed:F1} m/s (safe {safeSpeed:F1} m/s)");
                        return true;
                    }
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
                CouplingScannerPatches.KillCouplingScanner(__instance);
                CouplingScannerPatches.KillCouplingScanner(__instance.coupledTo);
                return false;
            }
        }
    }
}
