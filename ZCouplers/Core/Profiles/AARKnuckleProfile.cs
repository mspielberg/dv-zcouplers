using UnityEngine;

namespace DvMod.ZCouplers
{
    internal sealed class AARKnuckleProfile : ICouplerProfile
    {
        public CouplerType Type => CouplerType.AARKnuckle;
        public CouplerOptions Options { get; } = new CouplerOptions
        {
            Name = "AAR Knuckle",
            HookLateralOffsetX = 0f,
            SupportsVerticalArticulation = false,
            AlwaysHideAirHoses = false,
            HookClosedChildName = "hook",
            HookOpenChildName = "hook_open",
            HasOpenVariant = true,
        };

        public GameObject? GetClosedPrefab() => AssetManager.GetAARClosedPrefab();
        public GameObject? GetOpenPrefab() => AssetManager.GetAAROpenPrefab();
    }
}