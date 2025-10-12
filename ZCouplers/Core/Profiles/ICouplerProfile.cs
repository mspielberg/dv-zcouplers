using DvMod.ZCouplers.Core.Helpers;
using UnityEngine;

namespace DvMod.ZCouplers.Core.Profiles
{
    /// <summary>
    /// Profile contract for a specific coupler type. Provides prefabs and options.
    /// </summary>
    public interface ICouplerProfile
    {
        // Modular system properties
        string ProfileId { get; }
        string DisplayName { get; }

        CouplerOptions Options { get; }

        // Asset bundle information
        string GetAssetBundleName();
        string GetClosedPrefabName();
        string? GetOpenPrefabName();
        string? GetAdditionalPrefabName();

        // Prefab getters
        GameObject? GetClosedPrefab();
        GameObject? GetOpenPrefab();
        GameObject? GetAdditionalPrefab();
        GameObject? GetSocketPrefab();

        // Visual logic
        bool ShouldUseOpenVisual(ChainCouplerInteraction.State state);

        // Socket placement
        void GetSocketTransform(bool isFront, out Vector3 offset, out Quaternion rotation, out Vector3 scale);
    }
}
