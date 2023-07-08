using BepInEx.Configuration;
using System;

namespace DvMod.ZCouplers
{
    public enum CouplerType
    {
        BufferAndChain = 0,
        AARKnuckle = 1,
        SA3Knuckle = 2,
    }

    public class Settings
    {
        public ConfigEntry<CouplerType> couplerType;

        public ConfigEntry<float> chainStrength;
        public ConfigEntry<float> bufferSpringRate;
        public ConfigEntry<float> bufferDamperRate;

        public ConfigEntry<bool> showBuffersWithKnuckles;
        public ConfigEntry<float> knuckleStrength;
        public ConfigEntry<float> drawgearSpringRate;
        public ConfigEntry<float> drawgearDamperRate;
        public ConfigEntry<float> autoCoupleThreshold;

        public ConfigEntry<bool> enableLogging;
        public readonly string? version;

        private static readonly AcceptableValueRange<float> POSITIVE = new AcceptableValueRange<float>(0.1f, float.PositiveInfinity);

        public Settings(ConfigFile configFile)
        {
            var couplerTypeDescription = new ConfigDescription($"One of: {string.Join(", ", Enum.GetNames(typeof(CouplerType)))}");
            couplerType = configFile.Bind("general", "couplerType", CouplerType.SA3Knuckle, couplerTypeDescription);

            chainStrength = configFile.Bind("chain", "strength", 0.85f, new ConfigDescription("Chain strength (Mn)", POSITIVE));
            bufferSpringRate = configFile.Bind("chain", "spring", 2f, new ConfigDescription("Compression spring rate", POSITIVE));
            bufferDamperRate = configFile.Bind("chain", "damper", 8f, new ConfigDescription("Compression damper rate", POSITIVE));

            showBuffersWithKnuckles = configFile.Bind("knuckle", "showBuffers", false, "Whether to show buffers when knuckles are in use");
            showBuffersWithKnuckles.SettingChanged += (sender, args) =>
            {
                if (KnuckleCouplers.enabled)
                    KnuckleCouplers.ToggleBuffers(showBuffersWithKnuckles.Value);
            };
            knuckleStrength = configFile.Bind("knuckle", "strength", 1.78f, new ConfigDescription("Knuckle strength (Mn)", POSITIVE));
            drawgearSpringRate = configFile.Bind("knuckle", "spring", 0.1f, new ConfigDescription("Compression spring rate", POSITIVE));
            drawgearDamperRate = configFile.Bind("knuckle", "damper", 100f, new ConfigDescription("Compression damper rate", POSITIVE));

            autoCoupleThreshold = configFile.Bind("knuckle", "autoCoupleThreshold", 20f, new ConfigDescription("Auto couple threshold (mm)", POSITIVE));

            configFile.SettingChanged += (sender, e) => Couplers.UpdateAllCompressionJoints();

            enableLogging = configFile.Bind("logging", "enable", true);
        }

        public float GetCouplerStrength()
        {
            return couplerType.Value switch
            {
                CouplerType.BufferAndChain => chainStrength.Value * 1e6f,
                CouplerType.AARKnuckle => knuckleStrength.Value * 1e6f,
                CouplerType.SA3Knuckle => knuckleStrength.Value * 1e6f,
                _ => 0f,
            };
        }

        public float GetSpringRate()
        {
            return couplerType.Value switch
            {
                CouplerType.BufferAndChain => bufferSpringRate.Value * 1e6f,
                CouplerType.AARKnuckle => drawgearSpringRate.Value * 1e6f,
                CouplerType.SA3Knuckle => drawgearSpringRate.Value * 1e6f,
                _ => 0f,
            };
        }

        public float GetDamperRate()
        {
            return couplerType.Value switch
            {
                CouplerType.BufferAndChain => bufferDamperRate.Value,
                CouplerType.AARKnuckle => drawgearDamperRate.Value,
                CouplerType.SA3Knuckle => drawgearDamperRate.Value,
                _ => 0f,
            };
        }
    }
}
