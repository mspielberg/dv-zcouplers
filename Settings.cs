using UnityModManagerNet;

namespace DvMod.ZCouplers
{
    public class Settings : UnityModManager.ModSettings, IDrawable
    {
        [Draw("Use custom couplers (changes require restart)")] public bool enableCustomCouplers = true;
        [Draw("Couple on chain hooked", VisibleOn = "enableCustomCouplers|true")] public bool coupleOnChainHooked = true;
        [Draw("Coupler strength", Min = 0.1f, VisibleOn = "enableCustomCouplers|true")] public float couplerStrength = 0.85f;
        [Draw("Coupler stress smoothing", Min = 0, Max = 1, VisibleOn = "enableCustomCouplers|true")] public float couplerStressSmoothing = 0.9f;
        [Draw("Buffer spring rate", VisibleOn = "enableCustomCouplers|true")] public float bufferSpringRate = 2f;
        [Draw("Buffer damper rate", VisibleOn = "enableCustomCouplers|true")] public float bufferDamperRate = 8f;

        [Draw("Enable logging")] public bool enableLogging = false;
        public readonly string? version = Main.mod?.Info.Version;

        override public void Save(UnityModManager.ModEntry entry)
        {
            Save<Settings>(this, entry);
        }

        public void OnChange()
        {
        }
    }
}
