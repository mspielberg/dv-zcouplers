using DvMod.ZCouplers.Core.Helpers;
using DvMod.ZCouplers.Visuals;
using UnityEngine;

namespace DvMod.ZCouplers.Core.Profiles
{
    internal sealed class AARKnuckleProfile : ICouplerProfile
    {
        // New modular properties
        public string ProfileId => "AAR";
        public string DisplayName => "AAR Knuckle Coupler";

        public CouplerOptions Options { get; } = new CouplerOptions
        {
            Name = "AAR Knuckle",
            SupportsHorizontalArticulation = true,
            SupportsVerticalArticulation = false,
            AlwaysHideAirHoses = false,
            HookAdditionalOffset = new Vector3(0f, -0.03f, 0f),
            HookClosedChildName = "AAR_closed",
            HookOpenChildName = "AAR_open",
            HasOpenVariant = true,
            HasSocketPlates = true,
            CouplerStrength = 3.65e6f, // 3.65 MN
            SpringRate = 12.0e6f, // 12.0 MN/m
            DamperRate = 12e3f, // 12 kN*s/m
            CouplerParkedText = "Coupler is closed\nPress [KEY] to open coupler",
            CouplerReadyText = "Coupler is open\nPress [KEY] to close coupler",
            EnforceAutoCoupling = false,
            EnforceAutoAirAndMu = false
        };

        // Asset bundle information
        public string GetAssetBundleName() => "AAR.assetbundle";
        public string GetClosedPrefabName() => "AAR_closed";
        public string? GetOpenPrefabName() => "AAR_open";
        public string? GetAdditionalPrefabName() => "AAR_socket";

        public GameObject? GetClosedPrefab() => AssetManager.GetPrefabForProfile(this, "closed");
        public GameObject? GetOpenPrefab() => AssetManager.GetPrefabForProfile(this, "open");
        public GameObject? GetAdditionalPrefab() => null;
        public GameObject? GetSocketPrefab() => AssetManager.GetPrefabForProfile(this, "socket");

        /// <summary>
        /// AAR visual logic: Open when ready (Dangling/Being_Dragged), closed otherwise
        /// </summary>
        public bool ShouldUseOpenVisual(ChainCouplerInteraction.State state)
        {
            switch (state)
            {
                case ChainCouplerInteraction.State.Dangling:
                case ChainCouplerInteraction.State.Being_Dragged:
                    return true; // AAR ready state visuals are open
                case ChainCouplerInteraction.State.Parked:
                case ChainCouplerInteraction.State.Attached_Loose:
                case ChainCouplerInteraction.State.Attached_Tight:
                default:
                    return false; // closed
            }
        }

        /// <summary>
        /// Get socket transform data for AAR couplers
        /// </summary>
        public void GetSocketTransform(bool isFront, out Vector3 offset, out Quaternion rotation, out Vector3 scale)
        {
            if (isFront)
            {
                offset = new Vector3(0f, -0.01f, 0.01f);
                rotation = Quaternion.identity;
                scale = Vector3.one;
            }
            else
            {
                offset = new Vector3(0f, -0.01f, -0.01f);
                rotation = Quaternion.Euler(0f, 180f, 0f);
                scale = Vector3.one;
            }
        }
    }
}
