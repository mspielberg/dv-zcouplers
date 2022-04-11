using UnityModManagerNet;

namespace DvMod.ZCouplers
{
    public enum CouplerType
    {
        BufferAndChain = 0,
        JanneyKnuckle = 1,
    }
    public class Settings : UnityModManager.ModSettings, IDrawable
    {
        [Draw("Coupler type (requires restart)")] public CouplerType couplerType = CouplerType.JanneyKnuckle;

        [Draw("Chain strength (Mn)", Min = 0.1f, VisibleOn = "couplerType|BufferAndChain")] public float chainStrength = 0.85f;
        [Draw("Compression spring rate", Min = 0f, VisibleOn = "couplerType|BufferAndChain")] public float bufferSpringRate = 2f;
        [Draw("Compression damper rate", Min = 0f, VisibleOn = "couplerType|BufferAndChain")] public float bufferDamperRate = 8f;

        [Draw("Knuckle strength (Mn)", Min = 0.1f, VisibleOn = "couplerType|JanneyKnuckle")] public float knuckleStrength = 1.78f;
        [Draw("Compression spring rate", Min = 0f, VisibleOn = "couplerType|JanneyKnuckle")] public float drawgearSpringRate = 0.1f;
        [Draw("Compression damper rate", Min = 0f, VisibleOn = "couplerType|JanneyKnuckle")] public float drawgearDamperRate = 100f;
        [Draw("Auto couple threshold (mm)", Min = 0f, VisibleOn = "couplerType|JanneyKnuckle")] public float autoCoupleThreshold = 20f;

        [Draw("Enable logging")] public bool enableLogging = false;
        public readonly string? version = Main.mod?.Info.Version;

        override public void Save(UnityModManager.ModEntry entry)
        {
            Save<Settings>(this, entry);
        }

        public void OnChange()
        {
            Couplers.UpdateAllCompressionJoints();
        }

        public float GetCouplerStrength()
        {
            return couplerType switch
            {
                CouplerType.BufferAndChain => chainStrength,
                CouplerType.JanneyKnuckle => knuckleStrength,
                _ => 0f,
            };
        }

        public float GetSpringRate()
        {
            return couplerType switch
            {
                CouplerType.BufferAndChain => bufferSpringRate * 1e6f,
                CouplerType.JanneyKnuckle => drawgearSpringRate * 1e6f,
                _ => 0f,
            };
        }

        public float GetDamperRate()
        {
            return couplerType switch
            {
                CouplerType.BufferAndChain => bufferDamperRate,
                CouplerType.JanneyKnuckle => drawgearDamperRate,
                _ => 0f,
            };
        }
    }
}
