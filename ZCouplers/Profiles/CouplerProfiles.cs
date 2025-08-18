using System.Collections.Generic;

namespace DvMod.ZCouplers
{
    /// <summary>
    /// Simple registry and helpers for coupler profiles.
    /// </summary>
    public static class CouplerProfiles
    {
    private static readonly Dictionary<CouplerType, ICouplerProfile> registry = new Dictionary<CouplerType, ICouplerProfile>();

        public static void Register(ICouplerProfile profile)
        {
            registry[profile.Type] = profile;
        }

        public static ICouplerProfile? Get(CouplerType type)
        {
            return registry.TryGetValue(type, out var p) ? p : null;
        }

        public static ICouplerProfile? Current => Get(Main.settings.couplerType);
    }
}
