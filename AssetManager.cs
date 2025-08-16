using System.Linq;
using UnityEngine;

namespace DvMod.ZCouplers
{
    /// <summary>
    /// Handles loading and managing knuckle coupler assets
    /// </summary>
    public static class AssetManager
    {
        private static GameObject? hookPrefab;
        private static GameObject? hookOpenPrefab; // For AAR open state

        public static GameObject? GetHookPrefab()
        {
            return hookPrefab;
        }

        public static GameObject? GetHookOpenPrefab()
        {
            return hookOpenPrefab;
        }

        /// <summary>
        /// Gets the appropriate hook prefab based on coupler type and state
        /// </summary>
        public static GameObject? GetHookPrefabForState(CouplerType couplerType, bool isParked)
        {
            // Use open hook for AAR couplers in parked state
            if (couplerType == CouplerType.AARKnuckle && isParked && hookOpenPrefab != null)
            {
                return hookOpenPrefab;
            }
            
            return hookPrefab;
        }

        /// <summary>
        /// Check if assets are properly loaded
        /// </summary>
        public static bool AreAssetsLoaded()
        {
            return hookPrefab != null;
        }

        public static void LoadAssets()
        {
            var bundleStream = typeof(AssetManager).Assembly.GetManifestResourceStream(typeof(Main), "ZCouplers.assetbundle");
            if (bundleStream == null)
            {
                Main.ErrorLog(() => "Failed to load ZCouplers.assetbundle - stream is null");
                return;
            }

            var bundle = AssetBundle.LoadFromStream(bundleStream);
            if (bundle == null)
            {
                Main.ErrorLog(() => "Failed to load AssetBundle from stream");
                return;
            }

            try
            {
                CouplerType couplerType = Main.settings.couplerType;
                
                // Use internal asset names instead of enum ToString()
                string assetName = couplerType switch
                {
                    CouplerType.AARKnuckle => "hook",
                    CouplerType.SA3Knuckle => "SA3",
                    _ => couplerType.ToString() // fallback
                };

                Main.DebugLog(() => $"Loading asset '{assetName}' for coupler type {couplerType}");
                hookPrefab = bundle.LoadAsset<GameObject>(assetName);

                if (hookPrefab == null)
                {
                    Main.ErrorLog(() => $"Failed to load hook prefab for asset name: {assetName} (coupler type: {couplerType})");
                    
                    // List available assets for debugging
                    var allAssets = bundle.GetAllAssetNames();
                    Main.ErrorLog(() => $"Available assets in bundle: {string.Join(", ", allAssets)}");
                }
                else
                {
                    Main.DebugLog(() => $"Successfully loaded hook prefab for asset name: {assetName} (coupler type: {couplerType}), components: {string.Join(", ", hookPrefab.GetComponents<UnityEngine.Component>().Select(c => c.GetType().Name))}");
                }

                // Load the open hook variant for AAR couplers
                if (couplerType == CouplerType.AARKnuckle)
                {
                    Main.DebugLog(() => "Loading 'hook_open' asset for AAR coupler");
                    hookOpenPrefab = bundle.LoadAsset<GameObject>("hook_open");
                    if (hookOpenPrefab == null)
                    {
                        Main.ErrorLog(() => "Failed to load hook_open prefab for AAR coupler");
                    }
                    else
                    {
                        Main.DebugLog(() => "Successfully loaded hook_open prefab for AAR coupler");
                    }
                }
            }
            catch (System.Exception ex)
            {
                Main.ErrorLog(() => $"Exception while loading assets: {ex.Message}");
            }
            finally
            {
                bundle.Unload(false);
            }
        }

    }
}