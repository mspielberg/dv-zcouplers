using UnityEngine;

namespace DvMod.ZCouplers
{
    internal sealed class SchakuProfile : ICouplerProfile
    {
        public CouplerType Type => CouplerType.Schafenberg;
        public CouplerOptions Options { get; } = new CouplerOptions
        {
            Name = "Schafenberg",
            HookLateralOffsetX = 0f,
            SupportsVerticalArticulation = true,
            AlwaysHideAirHoses = true,
            HookClosedChildName = "Schaku_closed",
            HookOpenChildName = "Schaku_open",
            HasOpenVariant = true,
        };

        public GameObject? GetClosedPrefab() => AssetManager.GetSchakuClosedPrefab();
        public GameObject? GetOpenPrefab() => AssetManager.GetSchakuOpenPrefab();
    }
}
