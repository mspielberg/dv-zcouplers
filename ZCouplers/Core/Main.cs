using System;
using DV;
using DvMod.ZCouplers.Core.Profiles;
using DvMod.ZCouplers.Core.Utils;
using DvMod.ZCouplers.Physics;
using DvMod.ZCouplers.Visuals;
using DvMod.ZCouplers.Patches;
using HarmonyLib;

using UnityEngine;
using UnityModManagerNet;

namespace DvMod.ZCouplers.Core;

[EnableReloading]
public static class Main
{
    public static UnityModManager.ModEntry? mod;
    public static Harmony? harmony;

	public static Settings settings = new Settings();

    public static bool Load(UnityModManager.ModEntry modEntry)
    {
        mod = modEntry;
        try
        {
            Settings settings = UnityModManager.ModSettings.Load<Settings>(modEntry);
            if (settings != null)
            {
                Main.settings = settings;
                modEntry.Logger.Log("Loaded existing settings");
            }
            else
            {
                Main.settings = new Settings();
                modEntry.Logger.Log("Created new settings (no existing file)");
            }
        }
        catch (Exception ex)
        {
            Main.settings = new Settings();
            modEntry.Logger.Log("Failed to load settings, using defaults: " + ex.Message);
        }
        modEntry.OnGUI = Main.settings.Draw<Settings>;
        modEntry.OnSaveGUI = Main.settings.Save;
        modEntry.OnUnload = Unload;
        AppDomain.CurrentDomain.UnhandledException += delegate (object sender, UnhandledExceptionEventArgs e)
        {
            if (e.ExceptionObject is Exception ex2)
            {
                string? stackTrace = ex2.StackTrace;
                if (stackTrace != null && stackTrace.Contains("LateUpdate_Attached"))
                {
                    modEntry.Logger.Log("Caught LateUpdate_Attached exception: " + ex2.Message);
                }
            }
        };
        var harmonyInstance = new Harmony(modEntry.Info.Id);
        harmonyInstance.PatchAll();
        
        // Store harmony instance for cleanup
        harmony = harmonyInstance;
        
        // Register coupler profiles (modular per-coupler files)
        CouplerProfiles.Register(new AARKnuckleProfile());
        CouplerProfiles.Register(new SA3Profile());
        CouplerProfiles.Register(new SchakuProfile());
        CouplerProfiles.Register(new LAPProfile());

		KnuckleCouplers.Initialize();
		// Initialize optional Multiplayer integration via runtime shim (no hard dependency)
		MpShim.TryInitialize(modEntry);
		mod.Logger.Log($"Loaded {Main.settings.couplerType}");
		return true;
	}

    public static bool Unload(UnityModManager.ModEntry modEntry)
    {
        try
        {
            // Restore original game state before cleanup
            RestoreOriginalState();
            
            // Cleanup Harmony patches
            harmony?.UnpatchAll();
            harmony = null;
            
            // Cleanup all systems in reverse order of initialization
            RecouplingPrevention.Shutdown();
            KnuckleCouplers.Cleanup();
            JointManager.Cleanup();
            HookManager.Cleanup();
            LAPLinkManager.Cleanup();
            AssetManager.Cleanup();
            
            // Cleanup patches static data
            UncouplePatch.Cleanup();
            KnuckleCouplerPatches.Cleanup();
            
            // Cleanup save system
            SaveManager.Cleanup();
            
            // Cleanup profile registry
            CouplerProfiles.Cleanup();
            
            modEntry.Logger.Log("ZCouplers unloaded successfully");
        }
        catch (System.Exception ex)
        {
            modEntry.Logger.Error($"Error during ZCouplers unload: {ex.Message}");
            modEntry.Logger.Error(ex.StackTrace);
        }
        
        return true;
    }

    /// <summary>
    /// Restore original game state before unloading.
    /// </summary>
    private static void RestoreOriginalState()
    {
        try
        {
            // Remove all GameObjHider components from all objects
            CleanupGameObjHiders();
            
            if (CarSpawner.Instance?.allCars == null)
                return;

            foreach (var car in CarSpawner.Instance.allCars)
            {
                if (car == null) continue;

                // Restore air hoses and remove GameObjHider components
                RestoreAirHoses(car);
                
                // Restore buffer visibility
                RestoreBuffers(car);
                
                // Re-enable coupler components that may have been disabled
                RestoreCouplerComponents(car);
                
                // Reset coupler states to vanilla
                ResetCouplerStates(car);
            }
        }
        catch (System.Exception ex)
        {
            ErrorLog(() => $"Error restoring original state: {ex.Message}");
        }
    }

    /// <summary>
    /// Remove all GameObjHider components from the scene.
    /// </summary>
    private static void CleanupGameObjHiders()
    {
        try
        {
            var allHiders = UnityEngine.Object.FindObjectsOfType<GameObjHider>();
            foreach (var hider in allHiders)
            {
                if (hider != null)
                {
                    // Restore the object before destroying the component
                    var gameObj = hider.gameObject;
                    gameObj.SetActive(true);
                    
                    var renderers = gameObj.GetComponentsInChildren<MeshRenderer>(true);
                    foreach (var renderer in renderers)
                    {
                        renderer.enabled = true;
                    }
                    
                    UnityEngine.Object.Destroy(hider);
                }
            }
        }
        catch (System.Exception ex)
        {
            ErrorLog(() => $"Error cleaning up GameObjHider components: {ex.Message}");
        }
    }

    /// <summary>
    /// Restore air hoses that were hidden and remove GameObjHider components.
    /// </summary>
    private static void RestoreAirHoses(TrainCar car)
    {
        try
        {
            var interior = car?.interior;
            if (interior == null) return;

            // Find and restore hoses
            var hosesTransform = interior.Find("hoses");
            if (hosesTransform != null)
            {
                // Remove GameObjHider component
                var hider = hosesTransform.GetComponent<GameObjHider>();
                if (hider != null)
                    UnityEngine.Object.Destroy(hider);

                // Restore visibility
                hosesTransform.gameObject.SetActive(true);
                
                // Restore renderer components
                var renderers = hosesTransform.GetComponentsInChildren<MeshRenderer>(true);
                foreach (var renderer in renderers)
                {
                    renderer.enabled = true;
                }
            }
        }
        catch (System.Exception ex)
        {
            ErrorLog(() => $"Error restoring air hoses for car {car?.ID}: {ex.Message}");
        }
    }

    /// <summary>
    /// Restore buffer visibility.
    /// </summary>
    private static void RestoreBuffers(TrainCar car)
    {
        try
        {
            // Simply force buffers to be visible again
            BufferVisualManager.ToggleBuffers(true);
        }
        catch (System.Exception ex)
        {
            ErrorLog(() => $"Error restoring buffers for car {car?.ID}: {ex.Message}");
        }
    }

    /// <summary>
    /// Re-enable coupler components that may have been disabled.
    /// </summary>
    private static void RestoreCouplerComponents(TrainCar car)
    {
        try
        {
            if (car.frontCoupler != null)
            {
                var couplerComponent = car.frontCoupler.GetComponent<Coupler>();
                if (couplerComponent != null)
                    couplerComponent.enabled = true;

                var chainInteraction = car.frontCoupler.visualCoupler?.chainAdapter?.chainScript;
                if (chainInteraction != null)
                    chainInteraction.enabled = true;
            }

            if (car.rearCoupler != null)
            {
                var couplerComponent = car.rearCoupler.GetComponent<Coupler>();
                if (couplerComponent != null)
                    couplerComponent.enabled = true;

                var chainInteraction = car.rearCoupler.visualCoupler?.chainAdapter?.chainScript;
                if (chainInteraction != null)
                    chainInteraction.enabled = true;
            }
        }
        catch (System.Exception ex)
        {
            ErrorLog(() => $"Error restoring coupler components for car {car?.ID}: {ex.Message}");
        }
    }

    /// <summary>
    /// Reset coupler states to vanilla behavior.
    /// </summary>
    private static void ResetCouplerStates(TrainCar car)
    {
        try
        {
            // Reset front coupler
            if (car.frontCoupler != null && !car.frontCoupler.IsCoupled())
            {
                car.frontCoupler.state = ChainCouplerInteraction.State.Dangling;
            }

            // Reset rear coupler
            if (car.rearCoupler != null && !car.rearCoupler.IsCoupled())
            {
                car.rearCoupler.state = ChainCouplerInteraction.State.Dangling;
            }
        }
        catch (System.Exception ex)
        {
            ErrorLog(() => $"Error resetting coupler states for car {car?.ID}: {ex.Message}");
        }
    }

	public static void DebugLog(TrainCar car, Func<string> message)
	{
		if (car == PlayerManager.Car)
		{
			DebugLog(message);
		}
	}

	public static void DebugLog(Func<string> message)
	{
		if (settings.enableLogging)
		{
			mod?.Logger.Log(message());
		}
	}

	public static void ErrorLog(Func<string> message)
	{
		mod?.Logger.Log(message());
	}
}
