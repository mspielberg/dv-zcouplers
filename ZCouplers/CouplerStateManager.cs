using System.Collections.Generic;

using UnityEngine;

namespace DvMod.ZCouplers
{
    /// <summary>
    /// Manages knuckle coupler states, synchronization, and validation
    /// </summary>
    public static class CouplerStateManager
    {
        /// <summary>
        /// Apply the appropriate coupler state based on knuckle coupler lock state and coupling status
        /// </summary>
        public static void ApplyCouplerState(Coupler coupler, bool locked)
        {
            if (coupler == null)
                return;

            Main.DebugLog(() => $"Apply knuckle coupler state: {coupler.train.ID} {coupler.Position()}, locked={locked}, coupled={coupler.IsCoupled()}, native={coupler.state}");

            // Apply the knuckle coupler state first
            KnuckleCouplers.SetCouplerLocked(coupler, locked);

            // Update the native coupler state appropriately
            if (coupler.IsCoupled())
            {
                // For knuckle couplers: if coupled, both must be ready (locked)
                if (!locked)
                {
                    // Force to locked state - knuckle couplers can't be "not ready" while coupled
                    KnuckleCouplers.SetCouplerLocked(coupler, true);
                    Main.DebugLog(() => $"Force ready: {coupler.train.ID} {coupler.Position()} (coupled but not ready)");
                }

                // Coupled knuckle couplers are always Attached_Tight
                ChainCouplerInteraction.State newState = ChainCouplerInteraction.State.Attached_Tight;

                if (coupler.state != newState)
                {
                    coupler.state = newState;
                    Main.DebugLog(() => $"Set coupled state: {coupler.train.ID} {coupler.Position()} -> {newState}");
                }

                // Force create tension joint to ensure proper physics connection
                CreateMissingTensionJoints(coupler);
            }
            else
            {
                // For uncoupled couplers, use appropriate uncoupled states
                ChainCouplerInteraction.State newState;
                if (locked)
                {
                    // Locked but uncoupled = Dangling (ready to couple)
                    newState = ChainCouplerInteraction.State.Dangling;
                }
                else
                {
                    // Unlocked and uncoupled = Parked (not ready to couple)
                    newState = ChainCouplerInteraction.State.Parked;
                }

                if (coupler.state != newState)
                {
                    coupler.state = newState;
                    Main.DebugLog(() => $"Set uncoupled state: {coupler.train.ID} {coupler.Position()} -> {newState}");
                }

                // Force trigger DetermineNextState to ensure state machine is updated correctly
                TriggerStateUpdate(coupler);
            }
        }

        /// <summary>
        /// Synchronize native coupler states for all coupling pairs across all cars
        /// </summary>
        public static void SynchronizeAllCouplingStates()
        {
            if (CarSpawner.Instance == null)
                return;

            var processedCouplers = new HashSet<Coupler>();
            int synchronized = 0;

            foreach (TrainCar car in CarSpawner.Instance.allCars)
            {
                // Process front coupler
                if (car?.frontCoupler != null && !processedCouplers.Contains(car.frontCoupler))
                {
                    if (SynchronizeCouplingPair(car.frontCoupler))
                        synchronized++;
                    processedCouplers.Add(car.frontCoupler);
                    if (car.frontCoupler.coupledTo != null)
                        processedCouplers.Add(car.frontCoupler.coupledTo);
                }

                // Process rear coupler
                if (car?.rearCoupler != null && !processedCouplers.Contains(car.rearCoupler))
                {
                    if (SynchronizeCouplingPair(car.rearCoupler))
                        synchronized++;
                    processedCouplers.Add(car.rearCoupler);
                    if (car.rearCoupler.coupledTo != null)
                        processedCouplers.Add(car.rearCoupler.coupledTo);
                }
            }

            // Removed verbose synchronization summary log
        }

        /// <summary>
        /// Synchronize the native states of a coupling pair based on their knuckle coupler lock states
        /// </summary>
        private static bool SynchronizeCouplingPair(Coupler coupler)
        {
            if (coupler == null || !coupler.IsCoupled() || coupler.coupledTo == null)
                return false;

            bool thisLocked = KnuckleCouplers.IsReadyToCouple(coupler);
            bool partnerLocked = KnuckleCouplers.IsReadyToCouple(coupler.coupledTo);

            // For knuckle couplers: if coupled, force both to be ready
            if (!thisLocked)
            {
                KnuckleCouplers.SetCouplerLocked(coupler, true);
                Main.DebugLog(() => $"Forced {coupler.train.ID} {coupler.Position()} to ready state during sync");
            }
            if (!partnerLocked)
            {
                KnuckleCouplers.SetCouplerLocked(coupler.coupledTo, true);
                Main.DebugLog(() => $"Forced {coupler.coupledTo.train.ID} {coupler.coupledTo.Position()} to ready state during sync");
            }

            // Coupled knuckle couplers are always Attached_Tight
            ChainCouplerInteraction.State desiredState = ChainCouplerInteraction.State.Attached_Tight;

            bool changed = false;

            // Update both couplers to the same state, but only if they're actually coupled
            if (coupler.IsCoupled() && coupler.state != desiredState)
            {
                coupler.state = desiredState;
                changed = true;
                // Only log when debug logging is explicitly enabled
                Main.DebugLog(() => $"Synchronized {coupler.train.ID} {coupler.Position()} to {desiredState}");
            }

            if (coupler.coupledTo.IsCoupled() && coupler.coupledTo.state != desiredState)
            {
                coupler.coupledTo.state = desiredState;
                changed = true;
                // Only log when debug logging is explicitly enabled
                Main.DebugLog(() => $"Synchronized {coupler.coupledTo.train.ID} {coupler.coupledTo.Position()} to {desiredState}");
            }

            return changed;
        }

        /// <summary>
        /// Validate and create missing tension joints for all coupled cars after save loading
        /// </summary>
        public static void ValidateAllJointsAfterLoading()
        {
            if (CarSpawner.Instance != null)
            {
                int missingJoints = 0;
                int totalCoupled = 0;

                foreach (TrainCar car in CarSpawner.Instance.allCars)
                {
                    if (car?.frontCoupler?.IsCoupled() == true)
                    {
                        totalCoupled++;
                        if (!Couplers.HasTensionJoint(car.frontCoupler))
                        {
                            missingJoints++;
                            Main.DebugLog(() => $"Creating missing tension joint for {car.ID} front coupler");
                            CreateMissingTensionJoints(car.frontCoupler);
                        }
                    }

                    if (car?.rearCoupler?.IsCoupled() == true)
                    {
                        totalCoupled++;
                        if (!Couplers.HasTensionJoint(car.rearCoupler))
                        {
                            missingJoints++;
                            Main.DebugLog(() => $"Creating missing tension joint for {car.ID} rear coupler");
                            CreateMissingTensionJoints(car.rearCoupler);
                        }
                    }
                }

                // Removed verbose validation summary log
            }
        }

        /// <summary>
        /// Create missing tension joints for a coupled coupler
        /// </summary>
        private static void CreateMissingTensionJoints(Coupler coupler)
        {
            if (coupler == null || !coupler.IsCoupled() || coupler.coupledTo == null)
                return;

            // Check if tension joint already exists
            if (Couplers.HasTensionJoint(coupler))
            {
                // Removed verbose tension joint exists log
                return;
            }

            // Create tension joint using the Couplers system
            Main.DebugLog(() => $"Creating missing tension joint for {coupler.train.ID} {coupler.Position()} -> {coupler.coupledTo.train.ID} {coupler.coupledTo.Position()}");

            try
            {
                Couplers.ForceCreateTensionJoint(coupler);
            }
            catch (System.Exception ex)
            {
                Main.ErrorLog(() => $"Error creating tension joint for {coupler.train.ID}: {ex.Message}");
            }
        }

        /// <summary>
        /// Trigger a state update for the coupler to ensure the ChainCouplerInteraction state machine is synchronized
        /// </summary>
        private static void TriggerStateUpdate(Coupler coupler)
        {
            if (coupler?.visualCoupler?.chainAdapter?.chainScript == null)
                return;

            try
            {
                var chainScript = coupler.visualCoupler.chainAdapter.chainScript;

                // Force the ChainCouplerInteraction to re-evaluate its state by calling DetermineNextState
                var newState = chainScript.DetermineNextState();
                if (coupler.state != newState)
                {
                    // Update the coupler state to match the determined state
                    coupler.state = newState;
                    Main.DebugLog(() => $"Triggered state update for {coupler.train.ID} {coupler.Position()}: -> {newState}");
                }
            }
            catch (System.Exception ex)
            {
                Main.ErrorLog(() => $"Error triggering state update for {coupler.train.ID}: {ex.Message}");
            }
        }
    }
}