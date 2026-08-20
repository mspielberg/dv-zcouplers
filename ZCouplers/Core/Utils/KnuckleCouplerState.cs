using DvMod.ZCouplers.Core.Helpers;
using DvMod.ZCouplers.Visuals;

namespace DvMod.ZCouplers.Core.Utils
{
    /// <summary>
    /// Manages knuckle coupler lock/unlock states and visual interactions
    /// </summary>
    public static class KnuckleCouplerState
    {
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

            // If already unlocked, nothing to do
            if (coupler.state == ChainCouplerInteraction.State.Parked)
                return;

            var chainScript = coupler.visualCoupler?.chainAdapter?.chainScript;
            chainScript?.PlaySound(chainScript.attachSound, chainScript.transform.position);

            coupler.Uncouple(
                playAudio: true,
                calledOnOtherCoupler: false,
                dueToBrokenCouple: false,
                viaChainInteraction);

            // Update visual state after uncoupling completes
            HookManager.UpdateHookVisualStateFromCouplerState(coupler);
        }

        public static void ReadyCoupler(Coupler coupler)
        {
            if (coupler == null)
                return;

            Main.DebugLog(() => $"ReadyCoupler: {coupler.train.ID} {coupler.Position()}, state={coupler.state}");
            if (coupler.state != ChainCouplerInteraction.State.Parked)
            {
                Main.DebugLog(() => $"Already ready/locked: {coupler.train.ID} {coupler.Position()} (state={coupler.state})");
                return; // Already ready/locked
            }

            var chainScript = coupler.visualCoupler?.chainAdapter?.chainScript;
            chainScript?.PlaySound(chainScript.parkSound, chainScript.transform.position);

            // Update the native coupler state to reflect the new ready status
            if (!coupler.IsCoupled())
            {
                coupler.state = ChainCouplerInteraction.State.Dangling;
            }

            // User manually readied this coupler - lift any recoupling blocks
            RecouplingPrevention.RemoveBlockingFor(coupler);

            // Update visual state after changing the state
            HookManager.UpdateHookVisualStateFromCouplerState(coupler);
        }

        public static void SetCouplerLocked(Coupler coupler, bool locked)
        {
            if (coupler == null)
                return;

            Main.DebugLog(() => $"SetCouplerLocked: {coupler.train.ID} {coupler.Position()}, locked={locked}, state={coupler.state}");

            if (locked)
            {
                // Update the native coupler state if uncoupled
                if (!coupler.IsCoupled())
                {
                    coupler.state = ChainCouplerInteraction.State.Dangling;
                }

                // Coupler was readied/locked - lift any recoupling blocks
                RecouplingPrevention.RemoveBlockingFor(coupler);
            }
            else
            {
                // Update the native coupler state if uncoupled
                if (!coupler.IsCoupled())
                {
                    coupler.state = ChainCouplerInteraction.State.Parked;
                }
            }
            // Update visual state
            HookManager.UpdateHookVisualStateFromCouplerState(coupler);
        }

        public static bool HasUnlockedCoupler(Trainset trainset)
        {
            if (trainset?.cars == null)
                return false;

            foreach (var car in trainset.cars)
            {
                var fc = car.frontCoupler;
                var rc = car.rearCoupler;
                if ((fc != null && fc.state == ChainCouplerInteraction.State.Parked) ||
                    (rc != null && rc.state == ChainCouplerInteraction.State.Parked))
                    return true;
            }

            return false;
        }
    }
}
