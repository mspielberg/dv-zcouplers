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
            SupportsVerticalArticulation = false,
            AlwaysHideAirHoses = false,
            HookClosedChildName = "SA3_closed",
            HookOpenChildName = "SA3_open",
            HasOpenVariant = true,
        };

        public GameObject? GetClosedPrefab() => AssetManager.GetSA3ClosedPrefab();
        public GameObject? GetOpenPrefab() => AssetManager.GetSA3OpenPrefab();
    }
}