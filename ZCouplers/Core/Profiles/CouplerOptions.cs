using UnityEngine;
using DV;

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

        /// Whether the hook pivot supports yaw (horizontal) articulation.
        public bool SupportsHorizontalArticulation { get; set; } = false;

        /// When true, hide air hoses on all trains/couplers regardless of other settings.
        public bool AlwaysHideAirHoses { get; set; } = false;

        /// Optional additional local position offset applied to the hook head.
        public Vector3 HookAdditionalOffset { get; set; } = Vector3.zero;

        /// Names used for instantiated hook children (used for lookups during swaps/updates).
        public string HookClosedChildName { get; set; } = "hook_closed";
        public string HookOpenChildName { get; set; } = "hook_open";

        /// Whether this coupler has distinct open/closed prefabs.
        public bool HasOpenVariant { get; set; } = true;

        /// Whether this coupler type uses socket plates.
        public bool HasSocketPlates { get; set; } = false;

        /// Coupler strength in Newtons (converted from MN in profiles).
        public float CouplerStrength { get; set; } = 1.78e6f; // Default 1.78 MN

        /// Spring rate in N/m (converted from MN/m in profiles).
        public float SpringRate { get; set; } = 2e6f; // Default 2 MN/m

        /// Damper rate in N*s/m (converted from kN*s/m in profiles).
        public float DamperRate { get; set; } = 100e3f; // Default 100 kN*s/m

        /// Text to show when the coupler is ready.
        public string CouplerReadyText { get; set; } = "Coupler is ready\nPress [KEY] to unlock coupler";

        /// Text to show when the coupler is parked.
        public string CouplerParkedText { get; set; } = "Coupler is parked\nPress [KEY] to ready coupler";

        /// Gets the ready text with the actual key binding substituted.
        public string GetCouplerReadyText() => CouplerReadyText.Replace("[KEY]", InteractionText.Instance?.BtnUse ?? "[USE KEY]");

        /// Gets the parked text with the actual key binding substituted.
        public string GetCouplerParkedText() => CouplerParkedText.Replace("[KEY]", InteractionText.Instance?.BtnUse ?? "[USE KEY]");

        // If Auto Coupling is enforced
        public bool EnforceAutoCoupling { get; set; } = false;
    }
}
