using System;
using System.IO;
using System.Linq;
using System.Reflection;
using DvMod.ZCouplers.Core;
using DvMod.ZCouplers.Core.Helpers;
using UnityEngine;

namespace DvMod.ZCouplers.Visuals
{
    /// <summary>
    /// Loads and manages knuckle coupler assets.
    /// </summary>
    public static class AssetManager
    {
        private static GameObject? aarClosedPrefab;
        private static GameObject? aarOpenPrefab; // For AAR open state
        private static GameObject? aarSocketPrefab; // For AAR mount hardware
        private static GameObject? sa3ClosedPrefab; // For SA3 closed/ready state
        private static GameObject? sa3OpenPrefab;   // For SA3 open/parked state
        private static GameObject? sa3SocketPrefab; // For SA3 mount hardware
        private static GameObject? schakuClosedPrefab; // For Scharfenberg closed/ready state
        private static GameObject? schakuOpenPrefab;   // For Scharfenberg open/parked state
        private static GameObject? lapClosedPrefab; // For LAP closed/ready state
        private static GameObject? lapOpenPrefab;   // For LAP open/parked state
        private static GameObject? lapLinkPrefab; // For LAP link hardware

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

        public static GameObject? GetAARClosedPrefab() => aarClosedPrefab;
        public static GameObject? GetAAROpenPrefab() => aarOpenPrefab;
        public static GameObject? GetAARSocketPrefab() => aarSocketPrefab;

        public static GameObject? GetSA3ClosedPrefab() => sa3ClosedPrefab;
        public static GameObject? GetSA3OpenPrefab() => sa3OpenPrefab;
        public static GameObject? GetSA3SocketPrefab() => sa3SocketPrefab;

        public static GameObject? GetSchakuClosedPrefab() => schakuClosedPrefab;
        public static GameObject? GetSchakuOpenPrefab() => schakuOpenPrefab;

        public static GameObject? GetLAPClosedPrefab() => lapClosedPrefab;
        public static GameObject? GetLAPOpenPrefab() => lapOpenPrefab;
        public static GameObject? GetLAPLinkPrefab() => lapLinkPrefab;

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
                case CouplerType.Scharfenberg:
                    return schakuClosedPrefab != null || schakuOpenPrefab != null;
                case CouplerType.LAPCoupler:
                    return lapClosedPrefab != null || lapOpenPrefab != null;
                default:
                    return aarClosedPrefab != null;
            }
        }

        /// <summary>
        /// Loads assets for the specified coupler type from separate asset bundle files.
        /// </summary>
        public static void LoadAssets()
        {
            if (!Directory.Exists(assetsFolder))
            {
                Main.ErrorLog(() => $"Assets folder not found: {assetsFolder}");
                return;
            }

            CouplerType couplerType = Main.settings.couplerType;
            Main.DebugLog(() => $"Loading assets for coupler type: {couplerType}");

            // Load assets based on coupler type
            switch (couplerType)
            {
                case CouplerType.AARKnuckle:
                    LoadAARAssets();
                    break;

                case CouplerType.SA3Knuckle:
                    LoadSA3Assets();
                    break;

                case CouplerType.Scharfenberg:
                    LoadScharfenbergAssets();
                    break;

                case CouplerType.LAPCoupler:
                    LoadLAPAssets();
                    break;

                default:
                    Main.ErrorLog(() => $"Unknown coupler type: {couplerType}");
                    // Fallback to AAR
                    LoadAARAssets();
                    break;
            }
        }

        /// <summary>
        /// Loads AAR coupler assets from AAR.assetbundle.
        /// </summary>
        private static void LoadAARAssets()
        {
            string bundlePath = Path.Combine(assetsFolder, "AAR.assetbundle");
            var bundle = LoadAssetBundle(bundlePath);
            if (bundle == null) return;

            try
            {
                Main.DebugLog(() => "Loading AAR assets");
                aarClosedPrefab = LoadPrefabFromBundle(bundle, "AAR_closed");
                aarOpenPrefab = LoadPrefabFromBundle(bundle, "AAR_open");
                aarSocketPrefab = LoadPrefabFromBundle(bundle, "AAR_socket");

                if (aarClosedPrefab == null)
                    Main.ErrorLog(() => "Failed to load 'AAR_closed' prefab for AAR coupler");

                if (aarOpenPrefab == null)
                    Main.ErrorLog(() => "Failed to load 'AAR_open' prefab for AAR coupler");

                if (aarSocketPrefab == null)
                    Main.ErrorLog(() => "Failed to load 'AAR_socket' prefab for AAR coupler mount hardware");
            }
            finally
            {
                bundle.Unload(false);
            }
        }

        /// <summary>
        /// Loads SA3 coupler assets from SA3.assetbundle.
        /// </summary>
        private static void LoadSA3Assets()
        {
            string bundlePath = Path.Combine(assetsFolder, "SA3.assetbundle");
            var bundle = LoadAssetBundle(bundlePath);
            if (bundle == null) return;

            try
            {
                Main.DebugLog(() => "Loading SA3 assets");
                sa3ClosedPrefab = LoadPrefabFromBundle(bundle, "SA3_closed");
                sa3OpenPrefab = LoadPrefabFromBundle(bundle, "SA3_open");
                sa3SocketPrefab = LoadPrefabFromBundle(bundle, "SA3_socket");

                if (sa3ClosedPrefab == null)
                    Main.ErrorLog(() => "Failed to load 'SA3_closed' prefab for SA3 coupler");

                if (sa3OpenPrefab == null)
                    Main.ErrorLog(() => "Failed to load 'SA3_open' prefab for SA3 coupler");

                if (sa3SocketPrefab == null)
                    Main.ErrorLog(() => "Failed to load 'SA3_socket' prefab for SA3 coupler mount hardware");
            }
            finally
            {
                bundle.Unload(false);
            }
        }

        /// <summary>
        /// Loads Scharfenberg coupler assets from Scharfenberg.assetbundle.
        /// </summary>
        private static void LoadScharfenbergAssets()
        {
            string bundlePath = Path.Combine(assetsFolder, "Scharfenberg.assetbundle");
            var bundle = LoadAssetBundle(bundlePath);
            if (bundle == null) return;

            try
            {
                Main.DebugLog(() => "Loading Scharfenberg assets");
                schakuClosedPrefab = LoadPrefabFromBundle(bundle, "Schaku_closed");
                schakuOpenPrefab = LoadPrefabFromBundle(bundle, "Schaku_open");

                if (schakuClosedPrefab == null)
                    Main.ErrorLog(() => "Failed to load 'Schaku_closed' prefab for Scharfenberg coupler");

                if (schakuOpenPrefab == null)
                    Main.ErrorLog(() => "Failed to load 'Schaku_open' prefab for Scharfenberg coupler");
            }
            finally
            {
                bundle.Unload(false);
            }
        }

        /// <summary>
        /// Loads LAP coupler assets from LAP.assetbundle.
        /// </summary>
        private static void LoadLAPAssets()
        {
            string bundlePath = Path.Combine(assetsFolder, "LAP.assetbundle");
            var bundle = LoadAssetBundle(bundlePath);
            if (bundle == null) return;

            try
            {
                Main.DebugLog(() => "Loading LAP assets");
                lapClosedPrefab = LoadPrefabFromBundle(bundle, "LaP_closed");
                lapOpenPrefab = LoadPrefabFromBundle(bundle, "LaP_open");
                lapLinkPrefab = LoadPrefabFromBundle(bundle, "LaP_link");

                if (lapClosedPrefab == null)
                    Main.ErrorLog(() => "Failed to load 'LaP_closed' prefab for LAP coupler");

                if (lapOpenPrefab == null)
                    Main.ErrorLog(() => "Failed to load 'LaP_open' prefab for LAP coupler");

                if (lapLinkPrefab == null)
                    Main.ErrorLog(() => "Failed to load 'LaP_link' prefab for LAP coupler link hardware");
            }
            finally
            {
                bundle.Unload(false);
            }
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
                // Try direct name first (works if asset was explicitly named)
                var go = bundle.LoadAsset<GameObject>(desiredName);
                if (go != null)
                {
                    Main.DebugLog(() => $"Loaded '{desiredName}' directly");
                    return go;
                }

                // Scan all asset names (lowercased paths like "assets/prefabs/foo.prefab")
                string[] names;
                try { names = bundle.GetAllAssetNames(); }
                catch { names = Array.Empty<string>(); }

                if (names.Length == 0)
                {
                    Main.ErrorLog(() => $"No assets found in bundle for '{desiredName}'");
                    return null;
                }

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
        /// Clean up all asset references.
        /// Called during mod unload.
        /// </summary>
        public static void Cleanup()
        {
            aarClosedPrefab = null;
            aarOpenPrefab = null;
            aarSocketPrefab = null;
            sa3ClosedPrefab = null;
            sa3OpenPrefab = null;
            sa3SocketPrefab = null;
            schakuClosedPrefab = null;
            schakuOpenPrefab = null;
            lapClosedPrefab = null;
            lapOpenPrefab = null;
            lapLinkPrefab = null;
        }
    }
}
