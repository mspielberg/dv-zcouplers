using UnityEngine;

namespace DvMod.ZCouplers
{
    internal sealed class SA3Profile : ICouplerProfile
    {
        public CouplerType Type => CouplerType.SA3Knuckle;
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
            HasSocketPlates = true, // SA3 couplers use socket plates
        };

        public GameObject? GetClosedPrefab() => AssetManager.GetSA3ClosedPrefab();
        public GameObject? GetOpenPrefab() => AssetManager.GetSA3OpenPrefab();
        public GameObject? GetAdditionalPrefab() => null; // SA3 doesn't use additional prefabs
    }
}