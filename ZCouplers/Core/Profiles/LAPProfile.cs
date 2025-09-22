using UnityEngine;
using DV;

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
            CouplerStrength = 0.35e6f, // 0.35 MN
            SpringRate = 3.5e6f, // 3.5 MN/m
            DamperRate = 3e3f, // 3 kN*s/m
            CouplerReadyText = "Pin is inserted\nPress [KEY] to remove pin",
            CouplerParkedText = "Pin is removed\nPress [KEY] to insert pin"
        };

        public GameObject? GetClosedPrefab() => AssetManager.GetLAPClosedPrefab();
        public GameObject? GetOpenPrefab() => AssetManager.GetLAPOpenPrefab();
        public GameObject? GetAdditionalPrefab() => AssetManager.GetLAPLinkPrefab();
    }
}