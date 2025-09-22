using UnityEngine;
using DV;

namespace DvMod.ZCouplers
{
    internal sealed class AARKnuckleProfile : ICouplerProfile
    {
        public CouplerType Type => CouplerType.AARKnuckle;
        public CouplerOptions Options { get; } = new CouplerOptions
        {
            Name = "AAR Knuckle",
            SupportsHorizontalArticulation = true,
            SupportsVerticalArticulation = false,
            AlwaysHideAirHoses = false,
            HookAdditionalOffset = new Vector3(0f, -0.03f, 0f),  // Full 3D offset: X=lateral, Y=vertical, Z=forward/back
            HookClosedChildName = "AAR_closed",
            HookOpenChildName = "AAR_open",
            HasOpenVariant = true,
            HasSocketPlates = true, // AAR couplers use socket plates
            CouplerStrength = 3.65e6f, // 3.65 MN
            SpringRate = 12.0e6f, // 12.0 MN/m
            DamperRate = 12e3f, // 12 kN*s/m
            CouplerParkedText = "Coupler is closed\nPress [KEY] to open coupler",
            CouplerReadyText = "Coupler is open\nPress [KEY] to close coupler"
        };

        public GameObject? GetClosedPrefab() => AssetManager.GetAARClosedPrefab();
        public GameObject? GetOpenPrefab() => AssetManager.GetAAROpenPrefab();
        public GameObject? GetAdditionalPrefab() => null; // AAR doesn't use additional prefabs
    }
}