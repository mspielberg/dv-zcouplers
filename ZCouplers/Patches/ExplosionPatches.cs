using System;
using DvMod.ZCouplers.Core;
using DvMod.ZCouplers.Visuals;
using HarmonyLib;

// TrainCarExplosion has no namespace in the decompiled reference code
// ReSharper disable once CheckNamespace
namespace DvMod.ZCouplers
{
    /// <summary>
    /// Handles explosion related visual refreshes (damage buffer stems) after a car model swaps to its exploded variant.
    /// </summary>
    [HarmonyPatch]
    internal static class ExplosionPatches
    {
        [HarmonyPatch(typeof(TrainCarExplosion), nameof(TrainCarExplosion.UpdateModelToExploded))]
        private static class TrainCarExplosion_UpdateModelToExploded_Postfix
        {
            // Postfix so the base method completes model swap first
            private static void Postfix(TrainCar trainCar)
            {
                try
                {
                    if (trainCar == null)
                        return;

                    var livery = trainCar.carLivery;
                    if (livery == null)
                        return;

                    // Use current buffer visibility state so damage stems match existing buffer preference
                    bool visible = BufferVisualManager.BuffersCurrentlyVisible;
                    BufferVisualManager.ToggleDamageBufferStems(trainCar.gameObject, livery, visible);
                }
                catch (Exception ex)
                {
                    Main.ErrorLog(() => "Explosion postfix error: " + ex.Message);
                }
            }
        }
    }
}

