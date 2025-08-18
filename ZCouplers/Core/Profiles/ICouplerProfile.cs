using UnityEngine;

namespace DvMod.ZCouplers
{
    /// <summary>
    /// Profile contract for a specific coupler type. Provides prefabs and options.
    /// </summary>
    public interface ICouplerProfile
    {
        CouplerType Type { get; }
        CouplerOptions Options { get; }

        GameObject? GetClosedPrefab();
        GameObject? GetOpenPrefab();
    }
}