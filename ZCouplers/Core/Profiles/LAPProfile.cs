using DvMod.ZCouplers.Core.Helpers;
using DvMod.ZCouplers.Visuals;
using UnityEngine;

namespace DvMod.ZCouplers.Core.Profiles
{
    internal sealed class LAPProfile : ICouplerProfile
    {
        // New modular properties
        public string ProfileId => "LAP";
        public string DisplayName => "Link&Pin Coupler";

        public CouplerOptions Options { get; } = new CouplerOptions
        {
            Name = "LaP",
            HookLateralOffsetX = 0f,
            HookAdditionalOffset = new Vector3(0f, 0f, -0.125f),
            SupportsHorizontalArticulation = false,
            SupportsVerticalArticulation = false,
            AlwaysHideAirHoses = false,
            HookClosedChildName = "LaP_closed",
            HookOpenChildName = "LaP_open",
            HasOpenVariant = true,
            CouplerStrength = 0.35e6f, // 0.35 MN
            SpringRate = 3.5e6f, // 3.5 MN/m
            DamperRate = 3e3f, // 3 kN*s/m
            CouplerReadyText = "Pin is inserted\nPress [KEY] to remove pin",
            CouplerParkedText = "Pin is removed\nPress [KEY] to insert pin",
            EnforceAutoCoupling = false,
            EnforceAutoAirAndMu = false
        };

        // Asset bundle information
        public string GetAssetBundleName() => "LAP.assetbundle";
        public string GetClosedPrefabName() => "LaP_closed";
        public string? GetOpenPrefabName() => "LaP_open";
        public string? GetAdditionalPrefabName() => "LaP_link";

        public GameObject? GetClosedPrefab() => AssetManager.GetPrefabForProfile(this, "closed");
        public GameObject? GetOpenPrefab() => AssetManager.GetPrefabForProfile(this, "open");
        public GameObject? GetAdditionalPrefab() => AssetManager.GetPrefabForProfile(this, "link");
        public GameObject? GetSocketPrefab() => null; // LAP doesn't use socket plates

        /// <summary>
        /// LAP visual logic: Open when parked (no link), closed otherwise (link inserted)
        /// </summary>
        public bool ShouldUseOpenVisual(ChainCouplerInteraction.State state)
        {
            switch (state)
            {
                case ChainCouplerInteraction.State.Parked:
                    return true; // LAP open when parked (no link)
                case ChainCouplerInteraction.State.Dangling:
                case ChainCouplerInteraction.State.Being_Dragged:
                case ChainCouplerInteraction.State.Attached_Loose:
                case ChainCouplerInteraction.State.Attached_Tight:
                default:
                    return false; // closed otherwise (link inserted)
            }
        }

        /// <summary>
        /// LAP doesn't use socket plates, so no transform needed
        /// </summary>
        public void GetSocketTransform(bool isFront, out Vector3 offset, out Quaternion rotation, out Vector3 scale)
        {
            offset = Vector3.zero;
            rotation = Quaternion.identity;
            scale = Vector3.one;
        }
    }
}
