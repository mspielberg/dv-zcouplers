using DvMod.ZCouplers.Core.Helpers;
using DvMod.ZCouplers.Visuals;
using UnityEngine;

namespace DvMod.ZCouplers.Core.Profiles
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
            CouplerStrength = 1.70e6f, // 1.70 MN
            SpringRate = 12.0e6f, // 12.0 MN/m
            DamperRate = 15e3f, // 15 kN*s/m
            CouplerParkedText = "Coupler is unlocked\nPress [KEY] to lock coupler",
            CouplerReadyText = "Coupler is locked\nPress [KEY] to unlock coupler"
        };

        public GameObject? GetClosedPrefab() => AssetManager.GetSA3ClosedPrefab();
        public GameObject? GetOpenPrefab() => AssetManager.GetSA3OpenPrefab();
        public GameObject? GetAdditionalPrefab() => null; // SA3 doesn't use additional prefabs
    }
}