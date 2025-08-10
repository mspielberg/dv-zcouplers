using DV;
using UnityEngine;
using System.Collections.Generic;

namespace DvMod.ZCouplers
{
    /// <summary>
    /// Manages knuckle coupler lock/unlock states and visual interactions
    /// </summary>
    public static class KnuckleCouplerState
    {
        private static readonly HashSet<Coupler> unlockedCouplers = new HashSet<Coupler>();

        public static bool IsUnlocked(Coupler coupler)
        {
            // Base the unlocked state on the actual coupler state instead of internal tracking
            return coupler != null && coupler.state == ChainCouplerInteraction.State.Parked;
        }

        public static bool IsReadyToCouple(Coupler coupler)
        {
            // Ready to couple means not parked (so Dangling, Being_Dragged, Attached_Loose, or Attached_Tight)
            return coupler != null && coupler.state != ChainCouplerInteraction.State.Parked;
        }

        public static void UnlockCoupler(Coupler coupler, bool viaChainInteraction)
        {
            if (coupler == null)
                return;
                
            // Check if the coupler is actually in an unlocked state
            if (coupler.state == ChainCouplerInteraction.State.Parked)
                return; // Already unlocked
                
            var chainScript = coupler.visualCoupler.chainAdapter.chainScript;
            if (unlockedCouplers.Add(coupler)) // Add to HashSet for consistency
                chainScript.PlaySound(chainScript.attachSound, chainScript.transform.position);

            coupler.Uncouple(
                playAudio: true,
                calledOnOtherCoupler: false,
                dueToBrokenCouple: false,
                viaChainInteraction);

            // Start coroutine to update visual state after uncoupling completes
            if (chainScript != null)
                chainScript.StartCoroutine(DelayedVisualUpdate(coupler));
        }

        private static System.Collections.IEnumerator DelayedVisualUpdate(Coupler coupler)
        {
            // Wait a frame for the uncoupling to complete
            yield return null;
            
            // Update visual state after uncoupling (state should now be Parked)
            HookManager.UpdateHookVisualStateFromCouplerState(coupler);
        }

        public static void ReadyCoupler(Coupler coupler)
        {
            if (coupler == null)
                return;
                
            // Check if the coupler is actually in a locked/ready state
            if (coupler.state != ChainCouplerInteraction.State.Parked)
                return; // Already ready/locked
                
            var chainScript = coupler.visualCoupler.chainAdapter.chainScript;
            if (unlockedCouplers.Remove(coupler)) // Remove from HashSet for consistency
                chainScript.PlaySound(chainScript.parkSound, chainScript.transform.position);
                
            // Update the native coupler state to reflect the new ready status
            if (!coupler.IsCoupled())
            {
                coupler.state = ChainCouplerInteraction.State.Dangling;
                Main.DebugLog(() => $"Updated {coupler.train.ID} {coupler.Position()} to Dangling state after making ready");
            }
            
            // Update visual state after changing the state
            HookManager.UpdateHookVisualStateFromCouplerState(coupler);
        }

        public static void SetCouplerLocked(Coupler coupler, bool locked)
        {
            if (coupler == null)
                return;
                
            if (locked)
            {
                // Remove from unlocked set to make it locked/ready
                if (unlockedCouplers.Contains(coupler))
                {
                    unlockedCouplers.Remove(coupler);
                }
                
                // Update visual state
                var chainScript = coupler.visualCoupler?.chainAdapter?.chainScript;
                if (chainScript != null)
                    HookManager.UpdateHookVisualStateFromCouplerState(coupler);
                    
                // Update the native coupler state if uncoupled
                if (!coupler.IsCoupled())
                {
                    coupler.state = ChainCouplerInteraction.State.Dangling;
                }
            }
            else
            {
                // Add to unlocked set
                if (!unlockedCouplers.Contains(coupler))
                {
                    unlockedCouplers.Add(coupler);
                }
                
                // Update visual state
                var chainScript = coupler.visualCoupler?.chainAdapter?.chainScript;
                if (chainScript != null)
                    HookManager.UpdateHookVisualStateFromCouplerState(coupler);
                    
                // Update the native coupler state if uncoupled
                if (!coupler.IsCoupled())
                {
                    coupler.state = ChainCouplerInteraction.State.Parked;
                }
            }
        }

        // Update visual state only without triggering actual uncoupling
        public static void UpdateCouplerVisualState(Coupler coupler, bool locked)
        {
            var chainScript = coupler?.visualCoupler?.chainAdapter?.chainScript;
            if (chainScript == null || coupler == null)
                return;
                
            // Synchronize the internal tracking with the actual coupler state
            // instead of using the locked parameter
            if (coupler.state == ChainCouplerInteraction.State.Parked)
            {
                // Parked = coupler is unlocked
                if (!unlockedCouplers.Contains(coupler))
                    unlockedCouplers.Add(coupler);
            }
            else
            {
                // All other states = coupler is ready/locked
                unlockedCouplers.Remove(coupler);
            }
            
            // Use the new state-based visual update method
            HookManager.UpdateHookVisualStateFromCouplerState(coupler);
        }

        public static bool HasUnlockedCoupler(Trainset trainset)
        {
            if (trainset?.cars == null)
                return false;
                
            foreach (var car in trainset.cars)
            {
                if (unlockedCouplers.Contains(car.frontCoupler) || unlockedCouplers.Contains(car.rearCoupler))
                    return true;
            }

            return false;
        }
    }
}
