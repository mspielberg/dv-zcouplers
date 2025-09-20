using System;
using System.Reflection;
using HarmonyLib;
using DvMod.ZCouplers;
using UnityEngine;

namespace DvMod.ZCouplers.Patches
{
    /// <summary>
    /// Patch TrainCarColliders.Toggle method to intercept when walkable GameObjects are being activated
    /// This is the root cause - the LOD system calls SetActive(true) on the entire [walkable] GameObject,
    /// which re-enables all colliders inside it, overriding our individual collider.enabled = false settings
    /// </summary>
    [HarmonyPatch]
    public static class TrainCarCollidersPatches
    {
        // Patch the private Toggle method that's called by ToggleWalkable
        [HarmonyPatch(typeof(TrainCarColliders), "Toggle", new Type[] { typeof(Transform), typeof(bool) })]
        [HarmonyPostfix]
        public static void Toggle_Postfix(Transform target, bool on)
        {
            if (!KnuckleCouplers.enabled || target == null || !on)
            {
                return;
            }

            try
            {
                // Check if this is a walkable transform being activated
                if (target.name == "[walkable]")
                {
                    // Find the associated TrainCar
                    TrainCar car = target.GetComponentInParent<TrainCar>();
                    if (car != null)
                    {
                        Main.DebugLog(() => $"Detected [walkable] GameObject activation for car {car.ID}, re-applying buffer collider management");
                        BufferVisualManager.ApplyBufferCollidersForCar(car);
                    }
                }
            }
            catch (Exception ex)
            {
                Main.ErrorLog(() => $"Error in TrainCarColliders.Toggle postfix: {ex.Message}");
            }
        }
    }
}