using UnityEngine;

namespace DvMod.ZCouplers
{
    internal sealed class LAPProfile : ICouplerProfile
    {
        public CouplerType Type => CouplerType.LAPCoupler;
        public CouplerOptions Options { get; } = new CouplerOptions
        {
            Name = "LaP",
            HookLateralOffsetX = 0f, // left offset
            HookAdditionalOffset = new Vector3(0f, 0f, -0.125f),
            SupportsHorizontalArticulation = false,
            SupportsVerticalArticulation = false,
            AlwaysHideAirHoses = false,
            HookClosedChildName = "LaP_closed",
            HookOpenChildName = "LaP_open",
            HasOpenVariant = true,
        };

        public GameObject? GetClosedPrefab() => AssetManager.GetLAPClosedPrefab();
        public GameObject? GetOpenPrefab() => AssetManager.GetLAPOpenPrefab();
        public GameObject? GetAdditionalPrefab() => AssetManager.GetLAPLinkPrefab();
    }
}