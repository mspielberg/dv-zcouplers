using DvMod.ZCouplers.Core.Helpers;
using DvMod.ZCouplers.Visuals;
using UnityEngine;

namespace DvMod.ZCouplers.Core.Profiles
{
    internal sealed class SchakuProfile : ICouplerProfile
    {
        // New modular properties
        public string ProfileId => "Scharfenberg";
        public string DisplayName => "Scharfenberg Typ 10";

        public CouplerOptions Options { get; } = new CouplerOptions
        {
            Name = "Scharfenberg",
            HookLateralOffsetX = 0f,
            SupportsHorizontalArticulation = true,
            SupportsVerticalArticulation = true,
            AlwaysHideAirHoses = true,
            HookClosedChildName = "Schaku_closed",
            HookOpenChildName = "Schaku_open",
            HasOpenVariant = true,
            CouplerStrength = 1.50e6f, // 1.50 MN
            SpringRate = 5.0e6f, // 5.0 MN/m
            DamperRate = 8e3f, // 8 kN*s/m
            CouplerReadyText = "Coupler is engaged",
            CouplerParkedText = "Coupler is engaged",
            EnforceAutoCoupling = true,
            EnforceAutoAirAndMu = true // Scharfenberg automatically connects everything
        };

        // Asset bundle information
        public string GetAssetBundleName() => "Scharfenberg.assetbundle";
        public string GetClosedPrefabName() => "Schaku_closed";
        public string? GetOpenPrefabName() => "Schaku_open";
        public string? GetAdditionalPrefabName() => null;

        public GameObject? GetClosedPrefab() => AssetManager.GetPrefabForProfile(this, "closed");
        public GameObject? GetOpenPrefab() => AssetManager.GetPrefabForProfile(this, "open");
        public GameObject? GetAdditionalPrefab() => null;
        public GameObject? GetSocketPrefab() => null; // Scharfenberg doesn't use socket plates

        /// <summary>
        /// Scharfenberg visual logic: Open when disconnected, closed when attached
        /// </summary>
        public bool ShouldUseOpenVisual(ChainCouplerInteraction.State state)
        {
            switch (state)
            {
                case ChainCouplerInteraction.State.Attached_Tight:
                case ChainCouplerInteraction.State.Attached_Loose:
                    return false; // Schaku closes when attached
                case ChainCouplerInteraction.State.Parked:
                case ChainCouplerInteraction.State.Dangling:
                case ChainCouplerInteraction.State.Being_Dragged:
                default:
                    return true; // open otherwise
            }
        }

        /// <summary>
        /// Scharfenberg doesn't use socket plates, so no transform needed
        /// </summary>
        public void GetSocketTransform(bool isFront, out Vector3 offset, out Quaternion rotation, out Vector3 scale)
        {
            offset = Vector3.zero;
            rotation = Quaternion.identity;
            scale = Vector3.one;
        }
    }
}
