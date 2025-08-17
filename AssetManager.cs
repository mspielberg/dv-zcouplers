using UnityEngine;

namespace DvMod.ZCouplers
{
    /// <summary>
    /// Handles loading and managing knuckle coupler assets
    /// </summary>
    public static class AssetManager
    {
        private static GameObject? aarClosedPrefab;
        private static GameObject? aarOpenPrefab; // For AAR open state
        private static GameObject? sa3ClosedPrefab; // For SA3 closed/ready state
        private static GameObject? sa3OpenPrefab;   // For SA3 open/parked state

        public static GameObject? GetAARClosedPrefab()
        {
            return aarClosedPrefab;
        }

        public static GameObject? GetAAROpenPrefab()
        {
            return aarOpenPrefab;
        }

        public static GameObject? GetSA3ClosedPrefab()
        {
            return sa3ClosedPrefab;
        }

        public static GameObject? GetSA3OpenPrefab()
        {
            return sa3OpenPrefab;
        }

        /// <summary>
        /// Gets the appropriate hook prefab based on coupler type and state
        /// </summary>
        public static GameObject? GetHookPrefabForState(CouplerType couplerType, bool isParked)
        {
            switch (couplerType)
            {
                case CouplerType.AARKnuckle:
                    // Use open hook for AAR couplers in parked state, closed AAR for ready state
                    if (isParked && aarOpenPrefab != null)
                        return aarOpenPrefab;
                    return aarClosedPrefab;
                    
                case CouplerType.SA3Knuckle:
                    // Use open SA3 for parked state, closed SA3 for ready state
                    if (isParked && sa3OpenPrefab != null)
                        return sa3OpenPrefab;
                    return sa3ClosedPrefab;
                    
                    
                default:
                    return aarClosedPrefab;
            }
        }

        /// <summary>
        /// Check if assets are properly loaded
        /// </summary>
        public static bool AreAssetsLoaded()
        {
            CouplerType couplerType = Main.settings.couplerType;
            
            switch (couplerType)
            {
                case CouplerType.AARKnuckle:
                    return aarClosedPrefab != null || aarOpenPrefab != null;
                case CouplerType.SA3Knuckle:
                    return sa3ClosedPrefab != null || sa3OpenPrefab != null;
                default:
                    return aarClosedPrefab != null;
            }
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
                
                // Load assets based on coupler type
                switch (couplerType)
                {
                    case CouplerType.AARKnuckle:
                        Main.DebugLog(() => "Loading AAR hook assets");
                        aarClosedPrefab = bundle.LoadAsset<GameObject>("hook");
                        aarOpenPrefab = bundle.LoadAsset<GameObject>("hook_open");
                        
                        if (aarClosedPrefab == null)
                            Main.ErrorLog(() => "Failed to load 'hook' prefab for AAR coupler");
                        else
                            Main.DebugLog(() => $"Successfully loaded 'hook' prefab for AAR coupler");
                            
                        if (aarOpenPrefab == null)
                            Main.ErrorLog(() => "Failed to load 'hook_open' prefab for AAR coupler");
                        else
                            Main.DebugLog(() => "Successfully loaded 'hook_open' prefab for AAR coupler");
                        break;
                        
                    case CouplerType.SA3Knuckle:
                        Main.DebugLog(() => "Loading SA3 assets");
                        sa3ClosedPrefab = bundle.LoadAsset<GameObject>("SA3_closed");
                        sa3OpenPrefab = bundle.LoadAsset<GameObject>("SA3_open");
                        
                        if (sa3ClosedPrefab == null)
                            Main.ErrorLog(() => "Failed to load 'SA3_closed' prefab for SA3 coupler");
                        else
                            Main.DebugLog(() => $"Successfully loaded 'SA3_closed' prefab for SA3 coupler");
                            
                        if (sa3OpenPrefab == null)
                            Main.ErrorLog(() => "Failed to load 'SA3_open' prefab for SA3 coupler");
                        else
                            Main.DebugLog(() => "Successfully loaded 'SA3_open' prefab for SA3 coupler");
                        break;
                        
                    default:
                        // Fallback - try to load by enum name
                        string assetName = couplerType.ToString();
                        Main.DebugLog(() => $"Loading fallback asset '{assetName}' for coupler type {couplerType}");
                        aarClosedPrefab = bundle.LoadAsset<GameObject>(assetName);
                        
                        if (aarClosedPrefab == null)
                        {
                            Main.ErrorLog(() => $"Failed to load hook prefab for asset name: {assetName} (coupler type: {couplerType})");
                            
                            // List available assets for debugging
                            var allAssets = bundle.GetAllAssetNames();
                            Main.ErrorLog(() => $"Available assets in bundle: {string.Join(", ", allAssets)}");
                        }
                        else
                        {
                            Main.DebugLog(() => $"Successfully loaded hook prefab for asset name: {assetName} (coupler type: {couplerType})");
                        }
                        break;
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