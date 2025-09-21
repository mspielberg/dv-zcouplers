using UnityEngine;

namespace DvMod.ZCouplers
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
        };

        public GameObject? GetClosedPrefab() => AssetManager.GetSchakuClosedPrefab();
        public GameObject? GetOpenPrefab() => AssetManager.GetSchakuOpenPrefab();
        public GameObject? GetAdditionalPrefab() => null; // Scharfenberg doesn't use additional prefabs
    }
}
