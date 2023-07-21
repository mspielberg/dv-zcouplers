using UnityModManagerNet;

namespace DvMod.ZCouplers
{
    public enum CouplerType
    {
        BufferAndChain = 0,
        AARKnuckle = 1,
        SA3Knuckle = 2
    }

    public class Settings : UnityModManager.ModSettings, IDrawable
    {
        [Draw("Coupler type (requires restart)")]
        public CouplerType couplerType = CouplerType.SA3Knuckle;

        [Draw("Chain strength (Mn)", Min = 0.1f, VisibleOn = "couplerType|BufferAndChain")]
        public float chainStrength = 0.85f;
        [Draw("Compression spring rate", Min = 0f, VisibleOn = "couplerType|BufferAndChain")]
        public float bufferSpringRate = 2f;
        [Draw("Compression damper rate", Min = 0f, VisibleOn = "couplerType|BufferAndChain")]
        public float bufferDamperRate = 8f;

        [Draw("Show Buffers With Knuckles", InvisibleOn = "couplerType|BufferAndChain")]
        public bool showBuffersWithKnuckles = false;
        [Draw("Knuckle strength (Mn)", Min = 0.1f, InvisibleOn = "couplerType|BufferAndChain")]
        public float knuckleStrength = 1.78f;
        [Draw("Compression spring rate", Min = 0f, InvisibleOn = "couplerType|BufferAndChain")]
        public float drawgearSpringRate = 0.1f;
        [Draw("Compression damper rate", Min = 0f, InvisibleOn = "couplerType|BufferAndChain")]
        public float drawgearDamperRate = 100f;
        [Draw("Auto couple threshold (mm)", Min = 0f, InvisibleOn = "couplerType|BufferAndChain")]
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
                CouplerType.BufferAndChain => chainStrength,
                CouplerType.AARKnuckle => knuckleStrength,
                CouplerType.SA3Knuckle => knuckleStrength,
                _ => 0f
            };
        }

        public float GetSpringRate()
        {
            return couplerType switch {
                CouplerType.BufferAndChain => bufferSpringRate * 1e6f,
                CouplerType.AARKnuckle => drawgearSpringRate * 1e6f,
                CouplerType.SA3Knuckle => drawgearSpringRate * 1e6f,
                _ => 0f
            };
        }

        public float GetDamperRate()
        {
            return couplerType switch {
                CouplerType.BufferAndChain => bufferDamperRate,
                CouplerType.AARKnuckle => drawgearDamperRate,
                CouplerType.SA3Knuckle => drawgearDamperRate,
                _ => 0f
            };
        }
    }
}
