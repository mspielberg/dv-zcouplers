using System;
using System.Linq;
using System.Runtime.CompilerServices;
using DvMod.ZCouplers.Core.Profiles;
using DvMod.ZCouplers.Core.Utils;
using DvMod.ZCouplers.Patches;
using DvMod.ZCouplers.Physics;
using DvMod.ZCouplers.Visuals;
using HarmonyLib;
using UnityEngine;
using UnityModManagerNet;

namespace DvMod.ZCouplers.Core;

[EnableReloading]
public static class Main
{
    public static UnityModManager.ModEntry? mod;
    public static Harmony? harmony;

    public static Settings settings = null!; // Will be initialized in Load() after profiles are registered
    public static bool IsCCLLoaded { get; private set; }

    public static bool Load(UnityModManager.ModEntry modEntry)
    {
        mod = modEntry;

        // Register coupler profiles FIRST before loading settings
        RegisterCouplerProfilesAutomatically();

        try
        {
            Settings currentSettings = UnityModManager.ModSettings.Load<Settings>(modEntry);
            if (currentSettings != null)
            {
                Main.settings = currentSettings;
                // Validate that the loaded profile ID exists, otherwise use first available
                if (CouplerProfiles.GetById(Main.settings.selectedCoupler) == null)
                {
                    Main.settings.selectedCoupler = CouplerProfiles.GetAllProfileIds().FirstOrDefault() ?? "AAR";
                    modEntry.Logger.Log($"Invalid profile ID in settings, defaulting to: {Main.settings.selectedCoupler}");
                }
                // Initialize the lastCouplerProfile to current value to track future changes
                Main.settings.lastCouplerProfile = Main.settings.couplerProfile;
                modEntry.Logger.Log("Loaded existing settings");
            }
            else
            {
                Main.settings = new Settings();
                // Ensure we have a valid profile
                Main.settings.selectedCoupler = CouplerProfiles.GetAllProfileIds().FirstOrDefault() ?? "AAR";
                // Initialize the lastCouplerProfile to current value to track future changes
                Main.settings.lastCouplerProfile = Main.settings.couplerProfile;
                modEntry.Logger.Log("Created new settings (no existing file)");
            }
        }
        catch (Exception ex)
        {
            Main.settings = new Settings();
            // Ensure we have a valid profile
            Main.settings.selectedCoupler = CouplerProfiles.GetAllProfileIds().FirstOrDefault() ?? "AAR";
            // Initialize the lastCouplerProfile to current value to track future changes
            Main.settings.lastCouplerProfile = Main.settings.couplerProfile;
            modEntry.Logger.Log("Failed to load settings, using defaults: " + ex.Message);
        }
        modEntry.OnGUI = Main.settings.OnGUI;
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

        KnuckleCouplers.Initialize();
        mod.Logger.Log($"Loaded ZCouplers with profile: {Main.settings.couplerProfile?.DisplayName ?? Main.settings.selectedCoupler}");

        var ccl = UnityModManager.modEntries.FirstOrDefault(mod => mod.Info.Id == "DVCustomCarLoader");
        if (ccl is not { Active: true }) return true;
        IsCCLLoaded = true;
        mod.Logger.Log("Found CCL!");

        return true;
    }

    /// <summary>
    /// Automatically discovers and registers all ICouplerProfile implementations via reflection.
    /// This makes the system fully modular - just add a new profile class and it's automatically discovered.
    /// </summary>
    private static void RegisterCouplerProfilesAutomatically()
    {
        try
        {
            var assembly = System.Reflection.Assembly.GetExecutingAssembly();
            var profileType = typeof(ICouplerProfile);

            // Find all types that implement ICouplerProfile and are not interfaces or abstract
            var profileTypes = assembly.GetTypes()
                .Where(t => profileType.IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract);

            int count = 0;
            foreach (var type in profileTypes)
            {
                try
                {
	                // Create an instance and register it
	                if (System.Activator.CreateInstance(type) is not ICouplerProfile profile) continue;
	                CouplerProfiles.Register(profile);
                    mod?.Logger.Log($"Registered coupler profile: {profile.DisplayName} (ID: {profile.ProfileId})");
                    count++;
                }
                catch (System.Exception ex)
                {
                    mod?.Logger.Error($"Failed to instantiate profile type {type.Name}: {ex.Message}");
                }
            }

            mod?.Logger.Log($"Auto-registered {count} coupler profiles");
        }
        catch (System.Exception ex)
        {
            mod?.Logger.Error($"Error during automatic profile registration: {ex.Message}");
            mod?.Logger.Error(ex.StackTrace);
        }
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

    public static void DebugLog(Func<string> message, [CallerFilePath] string sourceFilePath = "")
    {
        if (settings.enableLogging)
        {
            string prefix = GetLogPrefix(sourceFilePath) + "[Debug] ";
            mod?.Logger.Log(prefix + message());
        }
    }

    public static void ErrorLog(Func<string> message, [CallerFilePath] string sourceFilePath = "")
    {
        string prefix = GetLogPrefix(sourceFilePath) + "[Error] ";
        mod?.Logger.Log(prefix + message());
    }

    /// <summary>
    /// Get a log prefix in the format [ClassName] based on caller information.
    /// </summary>
    private static string GetLogPrefix(string sourceFilePath)
    {
        if (string.IsNullOrEmpty(sourceFilePath))
            return "";

        try
        {
            // Extract the file name without extension (e.g., "HookManager" from "HookManager.cs")
            string fileName = System.IO.Path.GetFileNameWithoutExtension(sourceFilePath);

            // Return formatted prefix
	        return $"[{fileName}] ";
        }
        catch
        {
            return "";
        }
    }
}
