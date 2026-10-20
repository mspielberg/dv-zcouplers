using System;

namespace DvMod.ZCouplers.Core.Utils
{
    /// <summary>
    /// ZCouplers changes coupler states through SetState, which raises Changed so other code can react,
    /// such as the multiplayer layer sending the host's states to clients. The game's own changes raise
    /// its Coupled, Uncoupled and ChainCouplerInteraction events instead.
    /// </summary>
    public static class CouplerStateChanges
    {
        /// <summary>Raised after SetState changes a coupler's state.</summary>
        public static event Action<Coupler>? Changed;

        public static void SetState(this Coupler coupler, ChainCouplerInteraction.State state)
        {
            if (coupler.state == state)
                return;
            coupler.state = state;
            Changed?.Invoke(coupler);
        }
    }
}
