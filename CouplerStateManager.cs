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
                
            Main.DebugLog(() => $"Applying knuckle coupler state for {coupler.train.ID} {coupler.Position()}: locked={locked}, currently coupled={coupler.IsCoupled()}, native state={coupler.state}");
                
            // Apply the knuckle coupler state first
            KnuckleCouplers.SetCouplerLocked(coupler, locked);
            
            // Update the native coupler state appropriately
            if (coupler.IsCoupled())
            {
                // For coupled couplers, set appropriate attached state based on knuckle coupler lock state
                ChainCouplerInteraction.State newState = locked ? 
                    ChainCouplerInteraction.State.Attached_Tight : 
                    ChainCouplerInteraction.State.Attached_Loose;
                    
                if (coupler.state != newState)
                {
                    coupler.state = newState;
                    Main.DebugLog(() => $"Updated native state for coupled {coupler.train.ID} {coupler.Position()} to {newState}");
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
                    // Locked but uncoupled = Parked (ready to couple)
                    newState = ChainCouplerInteraction.State.Parked;
                }
                else
                {
                    // Unlocked and uncoupled = Dangling (not ready to couple)
                    newState = ChainCouplerInteraction.State.Dangling;
                }
                
                if (coupler.state != newState)
                {
                    coupler.state = newState;
                    Main.DebugLog(() => $"Updated native state for uncoupled {coupler.train.ID} {coupler.Position()} to {newState}");
                }
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
            
            // Determine the appropriate state based on both couplers' lock states
            // Only use Attached_* states for actually coupled couplers
            ChainCouplerInteraction.State desiredState;
            if (thisLocked && partnerLocked)
            {
                desiredState = ChainCouplerInteraction.State.Attached_Tight;
            }
            else
            {
                desiredState = ChainCouplerInteraction.State.Attached_Loose;
            }
            
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
    }
}
