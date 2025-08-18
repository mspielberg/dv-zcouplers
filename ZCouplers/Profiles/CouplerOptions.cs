using UnityEngine;

namespace DvMod.ZCouplers
{
    /// <summary>
    /// Tunable options describing a coupler family (visual and behavior tweaks).
    /// </summary>
    public sealed class CouplerOptions
    {
        /// Display name for diagnostics.
        public string Name { get; set; } = "Unknown";

        /// Optional lateral offset for the hook head (in local X, meters).
        public float HookLateralOffsetX { get; set; } = 0f;

        /// Whether the hook pivot supports pitch (vertical) articulation.
        public bool SupportsVerticalArticulation { get; set; } = false;

        /// When true, hide air hoses on all trains/couplers regardless of other settings.
        public bool AlwaysHideAirHoses { get; set; } = false;

        /// Optional additional local position offset applied to the hook head.
        public Vector3 HookAdditionalOffset { get; set; } = Vector3.zero;

        /// Names used for instantiated hook children (used for lookups during swaps/updates).
        public string HookClosedChildName { get; set; } = "hook";
        public string HookOpenChildName { get; set; } = "hook_open";

        /// Whether this coupler has distinct open/closed prefabs.
        public bool HasOpenVariant { get; set; } = true;
    }
}