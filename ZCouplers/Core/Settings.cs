using UnityModManagerNet;

namespace DvMod.ZCouplers
{
    public class Settings : UnityModManager.ModSettings, IDrawable
    {
        [Draw("Coupler type (requires restart)")]
        public CouplerType couplerType = CouplerType.AARKnuckle;

        [Draw("Toggle Buffers Visuals", Tooltip = "Also modifies the physics to account for buffer absence")]
        public bool showBuffersWithKnuckles = false;
        [Draw("Knuckle strength (Mn)", Min = 0.1f)]
        public float knuckleStrength = 1.78f;
        [Draw("Tension spring rate (Mn/m)", Min = 0f)]
        public float drawgearSpringRate = 2f; // 2 MN/m = 2e6 N/m
        [Draw("Compression damper rate (kN*s/m)", Min = 0f)]
        public float drawgearDamperRate = 100f;
        [Draw("Auto couple threshold (mm)", Min = 0f)]
        public float autoCoupleThreshold = 20f;

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
            return couplerType switch
            {
                CouplerType.AARKnuckle => knuckleStrength * 1e6f,
                CouplerType.SA3Knuckle => knuckleStrength * 1e6f,
                CouplerType.Scharfenberg => knuckleStrength * 1e6f,
                _ => knuckleStrength * 1e6f // Default to knuckle strength
            };
        }

        public float GetSpringRate()
        {
            return couplerType switch
            {
                CouplerType.AARKnuckle => drawgearSpringRate * 1e6f, // Convert MN/m to N/m
                CouplerType.SA3Knuckle => drawgearSpringRate * 1e6f, // Convert MN/m to N/m
                CouplerType.Scharfenberg => drawgearSpringRate * 1e6f, // Convert MN/m to N/m
                _ => drawgearSpringRate * 1e6f // Default to drawgear spring rate
            };
        }

        public float GetDamperRate()
        {
            return couplerType switch
            {
                CouplerType.AARKnuckle => drawgearDamperRate * 1e3f, // Convert kN*s/m to N*s/m
                CouplerType.SA3Knuckle => drawgearDamperRate * 1e3f, // Convert kN*s/m to N*s/m
                CouplerType.Scharfenberg => drawgearDamperRate * 1e3f, // Convert kN*s/m to N*s/m
                _ => drawgearDamperRate * 1e3f // Default to drawgear damper rate
            };
        }
    }
}
