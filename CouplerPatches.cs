using HarmonyLib;
using System.Collections.Generic;
using UnityEngine;

namespace DvMod.ZCouplers
{
    /// <summary>
    /// Main coupler behavior patches and coordination
    /// </summary>
    public static class CouplerPatches
    {
        /// <summary>
        /// Destroy all joints on a car while preserving buffer joints
        /// </summary>
        public static void DestroyAllJoints(Coupler coupler)
        {
            if (coupler?.train?.gameObject == null)
                return;

            try
            {
                var trainCar = coupler.train;
                Main.DebugLog(() => $"Destroying coupling joints on {trainCar.ID})");

                // Get all buffer joints that should be preserved
                var preservedBufferJoints = new HashSet<ConfigurableJoint>();
                foreach (var kvp in JointManager.bufferJoints)
                {
                    if (kvp.Key?.train == trainCar && kvp.Value.joint != null)
                    {
                        preservedBufferJoints.Add(kvp.Value.joint);
                        Main.DebugLog(() => $"Preserving buffer joint for {trainCar.ID}");
                    }
                }

                // Destroy all Unity joints except preserved buffer joints
                var allJoints = trainCar.GetComponents<ConfigurableJoint>();
                foreach (var joint in allJoints)
                {
                    if (joint != null && !preservedBufferJoints.Contains(joint))
                    {
                        Main.DebugLog(() => $"Destroying ConfigurableJoint on {trainCar.ID}");
                        UnityEngine.Object.DestroyImmediate(joint);
                    }
                }

                var fixedJoints = trainCar.GetComponents<FixedJoint>();
                foreach (var joint in fixedJoints)
                {
                    if (joint != null)
                    {
                        Main.DebugLog(() => $"Destroying FixedJoint on {trainCar.ID}");
                        UnityEngine.Object.DestroyImmediate(joint);
                    }
                }

                var springJoints = trainCar.GetComponents<SpringJoint>();
                foreach (var joint in springJoints)
                {
                    if (joint != null)
                    {
                        Main.DebugLog(() => $"Destroying SpringJoint on {trainCar.ID}");
                        UnityEngine.Object.DestroyImmediate(joint);
                    }
                }

                var hingeJoints = trainCar.GetComponents<HingeJoint>();
                foreach (var joint in hingeJoints)
                {
                    if (joint != null)
                    {
                        Main.DebugLog(() => $"Destroying HingeJoint on {trainCar.ID}");
                        UnityEngine.Object.DestroyImmediate(joint);
                    }
                }

                // Clear the rigidCJ and jointCoroRigid from both couplers on this car
                // but only if they're not buffer joints we want to preserve
                if (trainCar.frontCoupler != null)
                {
                    if (trainCar.frontCoupler.rigidCJ != null && !preservedBufferJoints.Contains(trainCar.frontCoupler.rigidCJ))
                    {
                        trainCar.frontCoupler.rigidCJ = null;
                    }
                    trainCar.frontCoupler.jointCoroRigid = null;
                }
                if (trainCar.rearCoupler != null)
                {
                    if (trainCar.rearCoupler.rigidCJ != null && !preservedBufferJoints.Contains(trainCar.rearCoupler.rigidCJ))
                    {
                        trainCar.rearCoupler.rigidCJ = null;
                    }
                    trainCar.rearCoupler.jointCoroRigid = null;
                }

                Main.DebugLog(() => $"Completed joint destruction for {trainCar.ID} (preserved {preservedBufferJoints.Count} buffer joints)");
            }
            catch (System.Exception ex)
            {
                Main.DebugLog(() => $"Error in joint cleanup: {ex.Message}");
            }
        }

        /// <summary>
        /// Safe cleanup for a coupler during car deletion
        /// </summary>
        private static void SafeCleanupCoupler(Coupler coupler)
        {
            if (coupler == null || coupler.gameObject == null)
                return;

            try
            {
                // Clean up coupler components if objects are still valid
                JointManager.DestroyCompressionJoint(coupler, "PrepareForDeleting");
                JointManager.DestroyTensionJoint(coupler);
                CouplingScannerPatches.KillCouplingScanner(coupler);
            }
            catch (System.Exception ex)
            {
                Main.DebugLog(() => $"Error cleaning up coupler: {ex.Message}");
            }
        }

        /// <summary>
        /// Patch for CreateJoints to use custom joint system
        /// </summary>
        [HarmonyPatch(typeof(Coupler), nameof(Coupler.CreateJoints))]
        public static class CreateJointsPatch
        {
            public static bool Prefix(Coupler __instance)
            {
                // Allow tender joints to use original behavior
                if (__instance.train.GetComponent<DV.SteamTenderAutoCoupleMechanism>() != null && !__instance.isFrontCoupler)
                {
                    return true;
                }

                // Prevent joint creation during save loading to avoid physics instability
                if (SaveManager.IsLoadingFromSave)
                {
                    Main.DebugLog(() => $"Skipping joint creation during save loading: {__instance.train.ID}");
                    return true;
                }

                // Safety checks to prevent joint creation in unstable conditions
                if (__instance.train.derailed || __instance.coupledTo?.train.derailed == true)
                {
                    Main.DebugLog(() => $"Skipping joint creation - train derailed: {__instance.train.ID}");
                    return true;
                }

                var velocity1 = __instance.train.GetComponent<Rigidbody>()?.velocity.magnitude ?? 0f;
                var velocity2 = __instance.coupledTo?.train.GetComponent<Rigidbody>()?.velocity.magnitude ?? 0f;
                if (velocity1 > 5f || velocity2 > 5f)
                {
                    Main.DebugLog(() => $"Skipping joint creation - cars moving too fast: {__instance.train.ID} ({velocity1:F1} m/s) to {__instance.coupledTo?.train.ID} ({velocity2:F1} m/s)");
                    return true;
                }

                // Prevent rapid joint recreation
                if (!JointManager.CanCreateJoint(__instance))
                {
                    Main.DebugLog(() => $"Skipping joint creation - too soon after last creation: {__instance.train.ID}");
                    return true;
                }

                // Prevent duplicate joint creation
                if (JointManager.HasTensionJoint(__instance) || (__instance.coupledTo != null && JointManager.HasTensionJoint(__instance.coupledTo)))
                {
                    Main.DebugLog(() => $"Skipping joint creation - joints already exist: {__instance.train.ID}");
                    return false;
                }

                // Ensure we have a valid coupled partner
                if (__instance.coupledTo == null)
                {
                    Main.DebugLog(() => $"Skipping joint creation - no coupled partner: {__instance.train.ID}");
                    return true;
                }

                Main.DebugLog(() => $"Creating tension joint between {__instance.train.ID} and {__instance.coupledTo.train.ID}");

                // Record the time of joint creation
                JointManager.RecordJointCreation(__instance);

                // Create custom joints
                JointManager.CreateTensionJoint(__instance);
                var breaker = __instance.gameObject.AddComponent<CouplerBreaker>();
                var tensionJoint = JointManager.GetTensionJoint(__instance);
                if (tensionJoint != null)
                    breaker.joint = tensionJoint;
                if (__instance.rigidCJ == null && __instance.coupledTo.rigidCJ == null)
                    JointManager.CreateCompressionJoint(__instance, __instance.coupledTo);
                CouplingScannerPatches.KillCouplingScanner(__instance);
                CouplingScannerPatches.KillCouplingScanner(__instance.coupledTo);
                return false;
            }
        }
    }
}
