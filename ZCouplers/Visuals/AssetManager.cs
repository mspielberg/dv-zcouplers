using System;
using System.IO;
using System.Linq;
using UnityEngine;

namespace DvMod.ZCouplers
{
    /// <summary>
    /// Loads and manages knuckle coupler assets.
    /// </summary>
    public static class AssetManager
    {
        private static GameObject? aarClosedPrefab;
        private static GameObject? aarOpenPrefab; // For AAR open state
        private static GameObject? sa3ClosedPrefab; // For SA3 closed/ready state
        private static GameObject? sa3OpenPrefab;   // For SA3 open/parked state
        private static GameObject? schakuClosedPrefab; // For Schafenberg closed/ready state
        private static GameObject? schakuOpenPrefab;   // For Schafenberg open/parked state

        public static GameObject? GetAARClosedPrefab() => aarClosedPrefab;
        public static GameObject? GetAAROpenPrefab() => aarOpenPrefab;

        public static GameObject? GetSA3ClosedPrefab() => sa3ClosedPrefab;
        public static GameObject? GetSA3OpenPrefab() => sa3OpenPrefab;

        public static GameObject? GetSchakuClosedPrefab() => schakuClosedPrefab;
        public static GameObject? GetSchakuOpenPrefab() => schakuOpenPrefab;

        /// <summary>
        /// Returns the hook prefab for the specified coupler type and parked state.
        /// </summary>
        public static GameObject? GetHookPrefabForState(CouplerType couplerType, bool isParked)
        {
            switch (couplerType)
            {
                case CouplerType.AARKnuckle:
                    return isParked && aarOpenPrefab != null ? aarOpenPrefab : aarClosedPrefab;

                case CouplerType.SA3Knuckle:
                    return isParked && sa3OpenPrefab != null ? sa3OpenPrefab : sa3ClosedPrefab;

                case CouplerType.Schafenberg:
                    return isParked && schakuOpenPrefab != null ? schakuOpenPrefab : schakuClosedPrefab;

                default:
                    return aarClosedPrefab;
            }
        }

        /// <summary>
        /// Returns whether assets for the current coupler type are loaded.
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
                case CouplerType.Schafenberg:
                    return schakuClosedPrefab != null || schakuOpenPrefab != null;
                default:
                    return aarClosedPrefab != null;
            }
        }

        public static void LoadAssets()
        {
            var bundleStream = typeof(AssetManager).Assembly.GetManifestResourceStream(typeof(Main), "zcouplers");
            if (bundleStream == null)
            {
                Main.ErrorLog(() => "Failed to load AssetBundle - stream is null");
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

                // Helper to load a prefab by name regardless of folder path within the bundle
                GameObject? LoadPrefab(string desiredName)
                {
                    // Try direct name first (works if asset was explicitly named)
                    var go = bundle.LoadAsset<GameObject>(desiredName);
                    if (go != null)
                        return go;

                    // Scan all asset names (lowercased paths like "assets/prefabs/foo.prefab")
                    string[] names;
                    try { names = bundle.GetAllAssetNames(); }
                    catch { names = Array.Empty<string>(); }

                    if (names.Length == 0)
                        return null;

                    // Match by filename without extension, then by path ending, then by contains
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
                    else if (Main.settings.enableLogging)
                    {
                        Main.ErrorLog(() => $"Asset '{desiredName}' not found in bundle.");
                    }
                    return null;
                }

                // Load assets based on coupler type
                switch (couplerType)
                {
                    case CouplerType.AARKnuckle:
                        Main.DebugLog(() => "Loading AAR assets");
                        aarClosedPrefab = LoadPrefab("AAR_closed");
                        aarOpenPrefab = LoadPrefab("AAR_open");
                        aarSocketPrefab = LoadPrefab("AAR_socket");

                        if (aarClosedPrefab == null)
                            Main.ErrorLog(() => "Failed to load 'AAR_closed' prefab for AAR coupler");

                        if (aarOpenPrefab == null)
                            Main.ErrorLog(() => "Failed to load 'AAR_open' prefab for AAR coupler");
                        break;

                    case CouplerType.SA3Knuckle:
                        Main.DebugLog(() => "Loading SA3 assets");
                        sa3ClosedPrefab = LoadPrefab("SA3_closed");
                        sa3OpenPrefab = LoadPrefab("SA3_open");

                        if (sa3ClosedPrefab == null)
                            Main.ErrorLog(() => "Failed to load 'SA3_closed' prefab for SA3 coupler");

                        if (sa3OpenPrefab == null)
                            Main.ErrorLog(() => "Failed to load 'SA3_open' prefab for SA3 coupler");
                        break;

                    case CouplerType.Schafenberg:
                        Main.DebugLog(() => "Loading Schafenberg assets");
                        schakuClosedPrefab = LoadPrefab("Schaku_closed");
                        schakuOpenPrefab = LoadPrefab("Schaku_open");

                        if (schakuClosedPrefab == null)
                            Main.ErrorLog(() => "Failed to load 'Schaku_closed' prefab for Schafenberg coupler");

                        if (schakuOpenPrefab == null)
                            Main.ErrorLog(() => "Failed to load 'Schaku_open' prefab for Schafenberg coupler");
                        break;

                    default:
                        // Fallback - try to load by enum name
                        string assetName = couplerType.ToString();
                        Main.DebugLog(() => $"Loading fallback asset '{assetName}' ({couplerType})");
                        aarClosedPrefab = LoadPrefab(assetName);

                        if (aarClosedPrefab == null)
                        {
                            Main.ErrorLog(() => $"Failed to load hook prefab for asset name: {assetName} ({couplerType})");
                        }
                        else
                        {
                            Main.DebugLog(() => $"Loaded hook prefab: {assetName} ({couplerType})");
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