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
            return coupler != null && unlockedCouplers.Contains(coupler);
        }

        public static bool IsReadyToCouple(Coupler coupler)
        {
            return !IsUnlocked(coupler);
        }

        public static void UnlockCoupler(Coupler coupler, bool viaChainInteraction)
        {
            if (coupler == null)
                return;
                
            if (unlockedCouplers.Contains(coupler))
                return;
                
            var chainScript = coupler.visualCoupler.chainAdapter.chainScript;
            if (unlockedCouplers.Add(coupler))
                chainScript.PlaySound(chainScript.attachSound, chainScript.transform.position);

            // Update visual state
            HookManager.UpdateHookVisualState(chainScript, locked: false);

            coupler.Uncouple(
                playAudio: true,
                calledOnOtherCoupler: false,
                dueToBrokenCouple: false,
                viaChainInteraction);
        }

        public static void ReadyCoupler(Coupler coupler)
        {
            if (coupler == null)
                return;
                
            if (!unlockedCouplers.Contains(coupler))
                return;
                
            var chainScript = coupler.visualCoupler.chainAdapter.chainScript;
            if (unlockedCouplers.Remove(coupler))
                chainScript.PlaySound(chainScript.parkSound, chainScript.transform.position);
                
            // Update visual state
            HookManager.UpdateHookVisualState(chainScript, locked: true);
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
                    HookManager.UpdateHookVisualState(chainScript, locked: true);
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
                    HookManager.UpdateHookVisualState(chainScript, locked: false);
            }
        }

        // Update visual state only without triggering actual uncoupling
        public static void UpdateCouplerVisualState(Coupler coupler, bool locked)
        {
            var chainScript = coupler?.visualCoupler?.chainAdapter?.chainScript;
            if (chainScript == null || coupler == null)
                return;
                
            if (locked)
            {
                // Coupler should show as locked (ready to unlock)
                unlockedCouplers.Remove(coupler);
            }
            else
            {
                // Coupler should show as unlocked (ready to couple)
                unlockedCouplers.Add(coupler);
            }
            
            HookManager.UpdateHookVisualState(chainScript, locked);
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
