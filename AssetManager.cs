using UnityEngine;

namespace DvMod.ZCouplers
{
    /// <summary>
    /// Handles loading and managing knuckle coupler assets
    /// </summary>
    public static class AssetManager
    {
        private static GameObject? hookPrefab;

        public static GameObject? GetHookPrefab()
        {
            return hookPrefab;
        }

        public static void LoadAssets()
        {
            var bundleStream = typeof(AssetManager).Assembly.GetManifestResourceStream(typeof(Main), "ZCouplers.assetbundle");
            if (bundleStream == null)
            {
                Main.DebugLog(() => "Failed to load ZCouplers.assetbundle - stream is null");
                return;
            }

            var bundle = AssetBundle.LoadFromStream(bundleStream);
            if (bundle == null)
            {
                Main.DebugLog(() => "Failed to load AssetBundle from stream");
                return;
            }

            CouplerType couplerType = Main.settings.couplerType;
            hookPrefab = bundle.LoadAsset<GameObject>(couplerType.ToString());

            if (hookPrefab == null)
            {
                Main.DebugLog(() => $"Failed to load hook prefab for coupler type: {couplerType}");
            }
            else
            {
                Main.DebugLog(() => $"Successfully loaded hook prefab for coupler type: {couplerType}");
            }

            bundle.Unload(false);
        }

    }
}