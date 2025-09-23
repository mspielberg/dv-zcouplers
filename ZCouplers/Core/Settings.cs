using DvMod.ZCouplers.Core.Helpers;
using DvMod.ZCouplers.Core.Profiles;
using UnityModManagerNet;

namespace DvMod.ZCouplers.Core
{
    public enum strengthPreset { Custom, Recommended }
    public class Settings : UnityModManager.ModSettings, IDrawable
    {
        [Draw("Coupler type (requires restart)")]
        public CouplerType couplerType = CouplerType.AARKnuckle;

        [Draw("Toggle Buffers Visuals", Tooltip = "Also modifies the physics to account for buffer absence")]
        public bool showBuffersWithKnuckles = false;
        [Draw(DrawType.ToggleGroup)]
        public strengthPreset strengthValues = strengthPreset.Recommended;
        [Draw("Knuckle strength (Mn)", VisibleOn = "strengthValues|Custom", Min = 0.1f)]
        public float knuckleStrength = 1.78f;
        [Draw("Tension spring rate (Mn/m)", VisibleOn = "strengthValues|Custom", Min = 0f)]
        public float drawgearSpringRate = 2f; // 2 MN/m = 2e6 N/m
        [Draw("Compression damper rate (kN*s/m)", VisibleOn = "strengthValues|Custom", Min = 0f)]
        public float drawgearDamperRate = 100f;
        [Draw("Auto couple threshold (mm)", Min = 0f)]
        public float autoCoupleThreshold = 20f;
        [Draw("Minimum separation distance (m)", Min = 0.1f, Tooltip = "Minimum distance couplers must separate before they can recouple again")]
        public float minimumSeparationDistance = 1.0f;

        [Draw("Auto Air & MU Mode", Tooltip = "Automatically connect air hoses, open brake valves, and connect MU cables when coupling. Enforced by Scharfenberg couplers.")]
        public bool autoAirAndMuMode = false;

        [Draw("Auto Coupling Mode", Tooltip = "Automatically couple even when couplers are not ready. Enforced by Scharfenberg couplers.")]
        public bool autoCouplingMode = false;

        /// <summary>
        /// Gets the effective Auto Air & MU Mode setting, considering Scharfenberg coupler requirements.
        /// Scharfenberg couplers automatically force Auto Air & MU Mode to be active.
        /// </summary>
        public bool EffectiveAutoAirAndMuMode => autoAirAndMuMode || couplerType == CouplerType.Scharfenberg;

        [Draw("Disable Front Couplers on S282")]
        public bool disableFrontCouplersOnSteamLocos = false;

        [Draw("Enable debug logging")]
        public bool enableLogging = false;

        [Draw("Enable error logging")]
        public bool enableErrorLogging = true;
        public readonly string? version = Main.mod?.Info.Version;

        public override void Save(UnityModManager.ModEntry entry)
        {
            Save(this, entry);
        }

        public void OnChange()
        {
            Couplers.UpdateAllCompressionJoints();
            KnuckleCouplers.OnSettingsChanged();
        }

        public float GetCouplerStrength()
        {
            if (strengthValues == strengthPreset.Recommended)
            {
                // Use profile default values
                var profile = CouplerProfiles.Get(couplerType);
                return profile?.Options.CouplerStrength ?? 1.78e6f;
            }
            else
            {
                // Use custom override value
                return knuckleStrength * 1e6f; // Convert MN to N
            }
        }

        public float GetSpringRate()
        {
            if (strengthValues == strengthPreset.Recommended)
            {
                // Use profile default values
                var profile = CouplerProfiles.Get(couplerType);
                return profile?.Options.SpringRate ?? 2e6f;
            }
            else
            {
                // Use custom override value
                return drawgearSpringRate * 1e6f; // Convert MN/m to N/m
            }
        }

        public float GetDamperRate()
        {
            if (strengthValues == strengthPreset.Recommended)
            {
                // Use profile default values
                var profile = CouplerProfiles.Get(couplerType);
                return profile?.Options.DamperRate ?? 100e3f;
            }
            else
            {
                // Use custom override value
                return drawgearDamperRate * 1e3f; // Convert kN*s/m to N*s/m
            }
        }
    }
}
