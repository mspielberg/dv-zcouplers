using System.Collections.Generic;
using System.Linq;
using DvMod.ZCouplers.Core.Helpers;

namespace DvMod.ZCouplers.Core.Profiles
{
    /// <summary>
    /// Simple registry and helpers for coupler profiles.
    /// </summary>
    public static class CouplerProfiles
    {
        // New string-based registry for modular system
        private static readonly Dictionary<string, ICouplerProfile> registryById = new Dictionary<string, ICouplerProfile>();

        public static void Register(ICouplerProfile profile)
        {
            // Register by ID for modular access
            registryById[profile.ProfileId] = profile;
        }

        /// <summary>
        /// Get a profile by its string ID (modular system)
        /// </summary>
        public static ICouplerProfile? GetById(string profileId)
        {
            return registryById.TryGetValue(profileId, out var p) ? p : null;
        }

        /// <summary>
        /// Get all registered profile IDs
        /// </summary>
        public static IEnumerable<string> GetAllProfileIds()
        {
            return registryById.Keys;
        }

        /// <summary>
        /// Get all registered profiles
        /// </summary>
        public static IEnumerable<ICouplerProfile> GetAllProfiles()
        {
            return registryById.Values;
        }

        public static ICouplerProfile? Current => Main.settings.couplerProfile;

        /// <summary>
        /// Clean up the registry.
        /// Called during mod unload.
        /// </summary>
        public static void Cleanup()
        {
            registryById.Clear();
        }
    }
}
