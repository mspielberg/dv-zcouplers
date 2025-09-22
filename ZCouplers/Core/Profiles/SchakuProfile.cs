using DvMod.ZCouplers.Core.Helpers;
using DvMod.ZCouplers.Visuals;
using UnityEngine;
using DV;

namespace DvMod.ZCouplers.Core.Profiles
{
    internal sealed class SchakuProfile : ICouplerProfile
    {
        public CouplerType Type => CouplerType.Scharfenberg;
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
            CouplerParkedText = "Coupler is engaged"
        };

        public GameObject? GetClosedPrefab() => AssetManager.GetSchakuClosedPrefab();
        public GameObject? GetOpenPrefab() => AssetManager.GetSchakuOpenPrefab();
        public GameObject? GetAdditionalPrefab() => null; // Scharfenberg doesn't use additional prefabs
    }
}
