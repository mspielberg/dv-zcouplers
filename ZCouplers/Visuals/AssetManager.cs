using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using DvMod.ZCouplers.Core;
using DvMod.ZCouplers.Core.Profiles;
using UnityEngine;

namespace DvMod.ZCouplers.Visuals
{
    /// <summary>
    /// Loads and manages knuckle coupler assets.
    /// </summary>
    public static class AssetManager
    {
        // New modular asset cache system
        private static readonly Dictionary<string, Dictionary<string, GameObject>> profileAssetCache = new();

        private static readonly string assetsFolder = GetAssetsFolder();

        /// <summary>
        /// Gets the path to the Assets folder next to the DLL.
        /// </summary>
        private static string GetAssetsFolder()
        {
            string assemblyLocation = Assembly.GetExecutingAssembly().Location;
            string? dllDirectory = Path.GetDirectoryName(assemblyLocation);
            return Path.Combine(dllDirectory ?? string.Empty, "Assets");
        }

        // New modular API
        /// <summary>
        /// Get a prefab for a specific profile and type (modular system)
        /// </summary>
        /// <param name="profile">The coupler profile</param>
        /// <param name="prefabType">"closed", "open", "socket", "link", etc.</param>
        public static GameObject? GetPrefabForProfile(ICouplerProfile? profile, string prefabType)
        {
            // Check if we have it cached
            if (profile != null && profileAssetCache.TryGetValue(profile.ProfileId, out var assetDict))
            {
                if (assetDict.TryGetValue(prefabType, out var prefab))
                {
                    return prefab;
                }
            }

            // Not loaded yet, try to load assets for this profile
            if (profile != null)
            {
	            LoadAssetsForProfile(profile);

	            // Try again after loading
	            if (!profileAssetCache.TryGetValue(profile.ProfileId, out assetDict)) return null;
	            if (assetDict.TryGetValue(prefabType, out var prefab))
	            {
		            return prefab;
	            }
            }

            return null;
        }

        /// <summary>
        /// Load assets for a specific profile (modular system)
        /// </summary>
        public static void LoadAssetsForProfile(ICouplerProfile profile)
        {
            // Check if already loaded
            if (profileAssetCache.ContainsKey(profile.ProfileId))
            {
                Main.DebugLog(() => $"Assets for profile '{profile.ProfileId}' already loaded");
                return;
            }

            if (!Directory.Exists(assetsFolder))
            {
                Main.ErrorLog(() => $"Assets folder not found: {assetsFolder}");
                return;
            }

            Main.DebugLog(() => $"Loading assets for profile: {profile.ProfileId}");

            string bundlePath = Path.Combine(assetsFolder, profile.GetAssetBundleName());
            var bundle = LoadAssetBundle(bundlePath);
            if (bundle == null) return;

            try
            {
                var assetDict = new Dictionary<string, GameObject>();

                // Load closed prefab
                var closedPrefabName = profile.GetClosedPrefabName();
                if (!string.IsNullOrEmpty(closedPrefabName))
                {
                    var closedPrefab = LoadPrefabFromBundle(bundle, closedPrefabName);
                    if (closedPrefab != null)
                    {
                        assetDict["closed"] = closedPrefab;
                    }
                    else
                    {
                        Main.ErrorLog(() => $"Failed to load closed prefab '{closedPrefabName}' for profile '{profile.ProfileId}'");
                    }
                }

                // Load open prefab if available
                var openPrefabName = profile.GetOpenPrefabName();
                if (!string.IsNullOrEmpty(openPrefabName))
                {
                    var openPrefab = LoadPrefabFromBundle(bundle, openPrefabName ?? string.Empty);
                    if (openPrefab != null)
                    {
                        assetDict["open"] = openPrefab;
                    }
                }

                // Load additional prefab if available (socket or link)
                var additionalPrefabName = profile.GetAdditionalPrefabName();
                if (!string.IsNullOrEmpty(additionalPrefabName))
                {
                    var additionalPrefab = LoadPrefabFromBundle(bundle, additionalPrefabName ?? string.Empty);
                    if (additionalPrefab != null)
                    {
                        // Determine the key based on name pattern
                        string key = additionalPrefabName != null && additionalPrefabName.ToLower().Contains("socket") ? "socket" :
                            additionalPrefabName != null && additionalPrefabName.ToLower().Contains("link") ? "link" : "additional";
                        assetDict[key] = additionalPrefab;
                    }
                }

                // Cache the assets
                profileAssetCache[profile.ProfileId] = assetDict;

                Main.DebugLog(() => $"Successfully loaded {assetDict.Count} assets for profile '{profile.ProfileId}'");
            }
            finally
            {
                bundle.Unload(false);
            }
        }

        /// <summary>
        /// Load assets for the current profile
        /// </summary>
        public static void LoadAssets()
        {
            var currentProfile = CouplerProfiles.Current;
            if (currentProfile != null)
            {
                LoadAssetsForProfile(currentProfile);
            }
        }

        /// <summary>
        /// Check if assets are loaded for current profile
        /// </summary>
        public static bool AreAssetsLoaded()
        {
            var currentProfile = CouplerProfiles.Current;
            if (currentProfile == null) return false;
            return profileAssetCache.ContainsKey(currentProfile.ProfileId);
        }

        /// <summary>
        /// Check if assets are loaded for a specific profile
        /// </summary>
        public static bool AreAssetsLoaded(ICouplerProfile profile)
        {
            return profileAssetCache.ContainsKey(profile.ProfileId);
        }

        /// <summary>
        /// Check if assets are loaded for a specific profile ID
        /// </summary>
        public static bool AreAssetsLoaded(string profileId)
        {
            return profileAssetCache.ContainsKey(profileId);
        }

        /// <summary>
        /// Loads an asset bundle from the specified file path.
        /// </summary>
        private static AssetBundle? LoadAssetBundle(string bundlePath)
        {
            if (!File.Exists(bundlePath))
            {
                Main.ErrorLog(() => $"Asset bundle not found: {bundlePath}");
                return null;
            }

            try
            {
                var bundle = AssetBundle.LoadFromFile(bundlePath);
                if (bundle == null)
                {
                    Main.ErrorLog(() => $"Failed to load AssetBundle from file: {bundlePath}");
                    return null;
                }

                Main.DebugLog(() => $"Successfully loaded asset bundle: {bundlePath}");
                return bundle;
            }
            catch (Exception ex)
            {
                Main.ErrorLog(() => $"Exception loading asset bundle '{bundlePath}': {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Loads a prefab by name from the specified asset bundle.
        /// </summary>
        private static GameObject? LoadPrefabFromBundle(AssetBundle bundle, string desiredName)
        {
            try
            {
                // Try direct name first
                var go = bundle.LoadAsset<GameObject>(desiredName);
                if (go != null)
                {
                    Main.DebugLog(() => $"Loaded '{desiredName}' directly");
                    return go;
                }

                // Scan all asset names
                string[] names;
                try { names = bundle.GetAllAssetNames(); }
                catch { names = []; }

                if (names.Length == 0)
                {
                    Main.ErrorLog(() => $"No assets found in bundle for '{desiredName}'");
                    return null;
                }

                // Match by filename
                var match = names.FirstOrDefault(p => string.Equals(Path.GetFileNameWithoutExtension(p), desiredName, StringComparison.OrdinalIgnoreCase))
                         ?? names.FirstOrDefault(p => p.EndsWith("/" + desiredName + ".prefab", StringComparison.OrdinalIgnoreCase))
                         ?? names.FirstOrDefault(p => p.IndexOf(desiredName, StringComparison.OrdinalIgnoreCase) >= 0 && p.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase));

                if (match != null)
                {
                    var loaded = bundle.LoadAsset<GameObject>(match);
                    if (loaded != null)
                    {
                        Main.DebugLog(() => $"Loaded '{desiredName}' via asset path '{match}'");
                        return loaded;
                    }
                }

                Main.ErrorLog(() => $"Asset '{desiredName}' not found in bundle. Available assets: {string.Join(", ", names)}");
                return null;
            }
            catch (Exception ex)
            {
                Main.ErrorLog(() => $"Exception loading prefab '{desiredName}': {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Cleanup all cached assets.
        /// </summary>
        public static void Cleanup()
        {
            profileAssetCache.Clear();
        }
    }
}
