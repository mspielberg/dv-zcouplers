using UnityEngine;

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
        };

        public GameObject? GetClosedPrefab() => AssetManager.GetAARClosedPrefab();
        public GameObject? GetOpenPrefab() => AssetManager.GetAAROpenPrefab();
        public GameObject? GetAdditionalPrefab() => null; // AAR doesn't use additional prefabs
    }
}