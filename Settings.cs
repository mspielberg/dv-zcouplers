using UnityModManagerNet;

namespace DvMod.ZCouplers
{
    public enum CouplerType
    {
        BufferAndChain = 1,
        JanneyKnuckle = 2,
    }
    public class Settings : UnityModManager.ModSettings, IDrawable
    {
        [Draw("Coupler type (requires restart)")] public CouplerType couplerType = CouplerType.JanneyKnuckle;
        [Draw("Chain strength", Min = 0.1f, VisibleOn = "couplerType|BufferAndChain")] public float chainStrength = 0.85f;
        [Draw("Knuckle strength", Min = 0.1f, VisibleOn = "couplerType|JanneyKnuckle")] public float knuckleStrength = 1.45f;
        [Draw("Coupler stress smoothing", Min = 0, Max = 1)] public float couplerStressSmoothing = 0.9f;

        [Draw("Compression spring rate")] public float bufferSpringRate = 2f;
        [Draw("Compression damper rate")] public float bufferDamperRate = 8f;

        [Draw("Enable logging")] public bool enableLogging = false;
        public readonly string? version = Main.mod?.Info.Version;

        override public void Save(UnityModManager.ModEntry entry)
        {
            Save<Settings>(this, entry);
        }

        public void OnChange()
        {
        }

        public float GetCouplerStrength()
        {
            return Main.settings.couplerType switch
            {
                CouplerType.BufferAndChain => chainStrength,
                CouplerType.JanneyKnuckle => knuckleStrength,
                _ => 0f,
            };
        }
    }
}
