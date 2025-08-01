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

    /// <summary>
    /// Patch for Uncouple to handle cleanup properly
    /// </summary>
    [HarmonyPatch(typeof(Coupler), nameof(Coupler.Uncouple))]
    public static class UncouplePatch
    {
        private static readonly Dictionary<Coupler, ConfigurableJoint> compressionJoints = new Dictionary<Coupler, ConfigurableJoint>();
        private static readonly Dictionary<Coupler, Coroutine> coros = new Dictionary<Coupler, Coroutine>();
        private static readonly Dictionary<Coupler, Coupler> partnerCouplers = new Dictionary<Coupler, Coupler>();

        public static void Prefix(Coupler __instance)
        {
            Main.DebugLog(() => $"Uncoupling {__instance.train.ID} from {__instance.coupledTo?.train.ID}");

            // Store partner coupler reference and clear pending states
            if (__instance.coupledTo != null)
            {
                partnerCouplers[__instance] = __instance.coupledTo;
            }

            // Clear any pending coupler states for these cars
            SaveManager.ClearPendingStatesForCar(__instance.train);
            if (__instance.coupledTo?.train != null)
                SaveManager.ClearPendingStatesForCar(__instance.coupledTo.train);

            // Store compression joints to prevent game from destroying them prematurely
            compressionJoints[__instance] = __instance.rigidCJ;
            __instance.rigidCJ = null;
            coros[__instance] = __instance.jointCoroRigid;
            __instance.jointCoroRigid = null;

            // Handle partner coupler's joints as well
            if (__instance.coupledTo != null)
            {
                compressionJoints[__instance.coupledTo] = __instance.coupledTo.rigidCJ;
                __instance.coupledTo.rigidCJ = null;
                coros[__instance.coupledTo] = __instance.coupledTo.jointCoroRigid;
                __instance.coupledTo.jointCoroRigid = null;
                Main.DebugLog(() => $"Stored partner coupler joints for {__instance.coupledTo.train.ID}");
            }

            // Destroy tension joints to remove coupling connection
            JointManager.DestroyTensionJoint(__instance);

            // Convert compression joint to buffer-only joint to maintain physics while removing coupling
            JointManager.ConvertCompressionJointToBufferOnly(__instance);

            // Additional cleanup: destroy any remaining coupling joints between these specific cars (but preserve buffer joints)
            if (__instance.coupledTo?.train != null)
            {
                CollisionHandler.DestroyJointsBetweenCars(__instance.train, __instance.coupledTo.train);
            }

            // Debug: Log any remaining joints on both cars
            CollisionHandler.LogRemainingJoints(__instance.train, "after uncoupling");
            if (__instance.coupledTo?.train != null)
                CollisionHandler.LogRemainingJoints(__instance.coupledTo.train, "after uncoupling");

            // Restart coupling scanners and apply separation
            CouplingScannerPatches.RestartCouplingScanner(__instance);
            if (__instance.coupledTo != null)
            {
                CouplingScannerPatches.RestartCouplingScanner(__instance.coupledTo);
                CouplingScannerPatches.SeparateCarsAfterUncoupling(__instance, __instance.coupledTo);
            }

            Main.DebugLog(() => $"Completed uncoupling cleanup for {__instance.train.ID}");
        }

        public static void Postfix(Coupler __instance)
        {
            // Conditionally restore compression joint based on coupling success
            if (compressionJoints.TryGetValue(__instance, out var storedJoint))
            {
                if (__instance.IsCoupled())
                {
                    // Uncoupling failed, restore the joint
                    __instance.rigidCJ = storedJoint;
                    Main.DebugLog(() => $"Restored rigidCJ for {__instance.train.ID} - uncoupling failed, still coupled to {__instance.coupledTo?.train.ID}");
                }
                else
                {
                    // Uncoupling succeeded, destroy the stored joint (no need to preserve - using collision system)
                    if (storedJoint != null)
                        Component.Destroy(storedJoint);
                    Main.DebugLog(() => $"Destroyed stored rigidCJ for {__instance.train.ID} - uncoupling succeeded");
                }
                compressionJoints.Remove(__instance);
            }

            // Conditionally restore coroutine based on coupling success
            if (coros.TryGetValue(__instance, out var storedCoro))
            {
                if (__instance.IsCoupled())
                {
                    __instance.jointCoroRigid = storedCoro;
                    Main.DebugLog(() => $"Restored jointCoroRigid for {__instance.train.ID} - uncoupling failed");
                }
                else
                {
                    Main.DebugLog(() => $"Did not restore jointCoroRigid for {__instance.train.ID} - uncoupling succeeded");
                }
                coros.Remove(__instance);
            }

            // Handle partner coupler's joints and visual state
            if (partnerCouplers.TryGetValue(__instance, out var partnerCoupler))
            {
                // Handle partner's compression joint
                if (compressionJoints.TryGetValue(partnerCoupler, out var partnerStoredJoint))
                {
                    if (partnerCoupler.IsCoupled())
                    {
                        partnerCoupler.rigidCJ = partnerStoredJoint;
                        Main.DebugLog(() => $"Restored partner rigidCJ for {partnerCoupler.train.ID} - uncoupling failed, still coupled to {partnerCoupler.coupledTo?.train.ID}");
                    }
                    else
                    {
                        // Uncoupling succeeded, destroy the stored joint (no need to preserve - using collision system)
                        if (partnerStoredJoint != null)
                            Component.Destroy(partnerStoredJoint);
                        Main.DebugLog(() => $"Destroyed stored partner rigidCJ for {partnerCoupler.train.ID} - uncoupling succeeded");
                    }
                    compressionJoints.Remove(partnerCoupler);
                }

                // Handle partner's coroutine
                if (coros.TryGetValue(partnerCoupler, out var partnerStoredCoro))
                {
                    if (partnerCoupler.IsCoupled())
                    {
                        partnerCoupler.jointCoroRigid = partnerStoredCoro;
                        Main.DebugLog(() => $"Restored partner jointCoroRigid for {partnerCoupler.train.ID} - uncoupling failed");
                    }
                    else
                    {
                        Main.DebugLog(() => $"Did not restore partner jointCoroRigid for {partnerCoupler.train.ID} - uncoupling succeeded");
                    }
                    coros.Remove(partnerCoupler);
                }

                // Update visual state for both couplers
                KnuckleCouplers.UpdateCouplerVisualState(partnerCoupler, locked: false);
                
                // Reset coupler states to prevent re-coupling
                if (!partnerCoupler.IsCoupled())
                {
                    partnerCoupler.state = ChainCouplerInteraction.State.Parked;
                    Main.DebugLog(() => $"Reset partner coupler state to Parked: {partnerCoupler.train.ID} {partnerCoupler.Position()}");
                }
                
                partnerCouplers.Remove(__instance);
                Main.DebugLog(() => $"Updated visual state for both uncoupled couplers: {__instance.train.ID} and {partnerCoupler.train.ID}");
            }

            // Update knuckle coupler visual state
            KnuckleCouplers.UpdateCouplerVisualState(__instance, locked: false);
            
            // Reset coupler state to prevent re-coupling
            if (!__instance.IsCoupled())
            {
                __instance.state = ChainCouplerInteraction.State.Parked;
                Main.DebugLog(() => $"Reset coupler state to Parked: {__instance.train.ID} {__instance.Position()}");
            }

            // Log final status
            if (!partnerCouplers.ContainsKey(__instance))
            {
                Main.DebugLog(() => $"Updated visual state for uncoupled coupler: {__instance.train.ID} {__instance.Position()}");
            }
            else
            {
                Main.DebugLog(() => $"Updated visual state for uncoupled coupler: {__instance.train.ID} {__instance.Position()}");
            }
        }
    }

    /// <summary>
    /// Patch for TrainCar.UncoupleSelf
    /// </summary>
    [HarmonyPatch(typeof(TrainCar), nameof(TrainCar.UncoupleSelf))]
    public static class UncoupleSelfPatch
    {
        public static void Postfix(TrainCar __instance)
        {
            Main.DebugLog(() => "TrainCar.UncoupleSelf.Postfix");
            // Remove joints before car is teleported
            JointManager.DestroyCompressionJoint(__instance.frontCoupler, "UncoupleSelf");
            JointManager.DestroyCompressionJoint(__instance.rearCoupler, "UncoupleSelf");

            JointManager.DestroyTensionJoint(__instance.frontCoupler);
            JointManager.DestroyTensionJoint(__instance.rearCoupler);

            CouplingScannerPatches.KillCouplingScanner(__instance.frontCoupler);
            CouplingScannerPatches.KillCouplingScanner(__instance.rearCoupler);
        }
    }

    /// <summary>
    /// Patch for car deletion preparation
    /// </summary>
    [HarmonyPatch(typeof(CarSpawner), nameof(CarSpawner.PrepareTrainCarForDeleting))]
    public static class PrepareTrainCarForDeletingPatch
    {
        public static void Postfix(TrainCar trainCar)
        {
            try
            {
                if (trainCar == null)
                    return;

                Main.DebugLog(() => $"TrainCar.PrepareTrainCarForDeleting.Postfix for {trainCar.ID}");

                // Clean up joints and scanners before deletion
                SafeCleanupCoupler(trainCar.frontCoupler);
                SafeCleanupCoupler(trainCar.rearCoupler);
            }
            catch (System.Exception ex)
            {
                Main.DebugLog(() => $"Error during car deletion cleanup: {ex.Message}");
                // Don't rethrow - let the game continue its deletion process
            }
        }
        
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
    }

    /// <summary>
    /// Extension methods for coupler functionality
    /// </summary>
    public static class CouplerExtensions
    {
        public static string Position(this Coupler coupler) => coupler.isFrontCoupler ? "front" : "rear";
    }
}
