using DvMod.ZCouplers.Core.Helpers;
using DvMod.ZCouplers.Visuals;
using UnityEngine;

namespace DvMod.ZCouplers.Core.Profiles
{
    internal sealed class SA3Profile : ICouplerProfile
    {
        // New modular properties
        public string ProfileId => "SA3";
        public string DisplayName => "SA3 Knuckle Coupler";

        public CouplerOptions Options { get; } = new CouplerOptions
        {
            Name = "SA3",
            HookLateralOffsetX = -0.035f, // left offset
            SupportsHorizontalArticulation = true,
            SupportsVerticalArticulation = false,
            AlwaysHideAirHoses = false,
            HookClosedChildName = "SA3_closed",
            HookOpenChildName = "SA3_open",
            HasOpenVariant = true,
            HasSocketPlates = true,
            CouplerStrength = 1.70e6f, // 1.70 MN
            SpringRate = 12.0e6f, // 12.0 MN/m
            DamperRate = 15e3f, // 15 kN*s/m
            CouplerParkedText = "Coupler is unlocked\nPress [KEY] to lock coupler",
            CouplerReadyText = "Coupler is locked\nPress [KEY] to unlock coupler",
            EnforceAutoCoupling = false,
            EnforceAutoAirAndMu = false
        };

        // Asset bundle information
        public string GetAssetBundleName() => "SA3.assetbundle";
        public string GetClosedPrefabName() => "SA3_closed";
        public string? GetOpenPrefabName() => "SA3_open";
        public string? GetAdditionalPrefabName() => "SA3_socket";

        public GameObject? GetClosedPrefab() => AssetManager.GetPrefabForProfile(this, "closed");
        public GameObject? GetOpenPrefab() => AssetManager.GetPrefabForProfile(this, "open");
        public GameObject? GetAdditionalPrefab() => null;
        public GameObject? GetSocketPrefab() => AssetManager.GetPrefabForProfile(this, "socket");

        /// <summary>
        /// SA3 visual logic: Open only when Parked
        /// </summary>
        public bool ShouldUseOpenVisual(ChainCouplerInteraction.State state)
        {
            return state == ChainCouplerInteraction.State.Parked;
        }

        /// <summary>
        /// Get socket transform data for SA3 couplers
        /// </summary>
        public void GetSocketTransform(bool isFront, out Vector3 offset, out Quaternion rotation, out Vector3 scale)
        {
            if (isFront)
            {
                offset = new Vector3(-0.02f, 0.04f, 0.01f);
                rotation = Quaternion.identity;
                scale = Vector3.one;
            }
            else
            {
                offset = new Vector3(0.02f, 0.04f, -0.01f);
                rotation = Quaternion.Euler(0f, 180f, 0f);
                scale = Vector3.one;
            }
        }
    }
}
