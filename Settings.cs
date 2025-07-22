using UnityModManagerNet;

namespace DvMod.ZCouplers
{
    public enum CouplerType
    {
        AARKnuckle = 1,
        SA3Knuckle = 2
    }

    public class Settings : UnityModManager.ModSettings, IDrawable
    {
        [Draw("Coupler type (requires restart)")]
        public CouplerType couplerType = CouplerType.AARKnuckle;

        [Draw("Show Buffers With Knuckles")]
        public bool showBuffersWithKnuckles = false;
        [Draw("Knuckle strength (Mn)", Min = 0.1f)]
        public float knuckleStrength = 1.78f;
        [Draw("Compression spring rate", Min = 0f)]
        public float drawgearSpringRate = 1f;
        [Draw("Compression damper rate", Min = 0f)]
        public float drawgearDamperRate = 100f;
        [Draw("Auto couple threshold (mm)", Min = 0f)]
        public float autoCoupleThreshold = 20f;

        [Draw("Enable logging")]
        public bool enableLogging = false;
        public readonly string? version = Main.mod?.Info.Version;

        public override void Save(UnityModManager.ModEntry entry)
        {
            Save(this, entry);
        }

        public void OnChange()
        {
            Couplers.UpdateAllCompressionJoints();
            if (KnuckleCouplers.enabled)
                KnuckleCouplers.ToggleBuffers(showBuffersWithKnuckles);
        }

        public float GetCouplerStrength()
        {
            return couplerType switch {
                CouplerType.AARKnuckle => knuckleStrength * 1e6f,
                CouplerType.SA3Knuckle => knuckleStrength * 1e6f,
                _ => knuckleStrength * 1e6f // Default to knuckle strength
            };
        }

        public float GetSpringRate()
        {
            return couplerType switch {
                CouplerType.AARKnuckle => drawgearSpringRate * 1e3f,
                CouplerType.SA3Knuckle => drawgearSpringRate * 1e3f,
                _ => drawgearSpringRate * 1e3f // Default to drawgear spring rate
            };
        }

        public float GetDamperRate()
        {
            return couplerType switch {
                CouplerType.AARKnuckle => drawgearDamperRate * 1e3f,
                CouplerType.SA3Knuckle => drawgearDamperRate * 1e3f,
                _ => drawgearDamperRate * 1e3f // Default to drawgear damper rate
            };
        }
    }
}
