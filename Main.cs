using System;
using HarmonyLib;
using UnityModManagerNet;

namespace DvMod.ZCouplers
{
    [EnableReloading]
    public static class Main
    {
        public static UnityModManager.ModEntry? mod;
        public static Settings settings = new Settings();

        public static bool Load(UnityModManager.ModEntry modEntry)
        {
            mod = modEntry;

            try
            {
                Settings? loaded = Settings.Load<Settings>(modEntry);
                if (loaded != null)
                {
                    settings = loaded;
                    modEntry.Logger.Log("Loaded existing settings");
                }
                else
                {
                    settings = new Settings();
                    modEntry.Logger.Log("Created new settings (no existing file)");
                }
            }
            catch (Exception ex)
            {
                settings = new Settings();
                modEntry.Logger.Log($"Failed to load settings, using defaults: {ex.Message}");
            }

            modEntry.OnGUI = settings.Draw;
            modEntry.OnSaveGUI = settings.Save;

            // HeadsUpDisplayBridge.Init();
            Harmony harmony = new Harmony(modEntry.Info.Id);
            harmony.PatchAll();

            // Force static initializer to execute and load asset bundle
            if (KnuckleCouplers.enabled)
                mod.Logger.Log($"Loaded {settings.couplerType}");

            // CCLIntegration.Initialize();

            return true;
        }

        public static void DebugLog(TrainCar car, Func<string> message)
        {
            if (car == PlayerManager.Car)
                DebugLog(message);
        }

        public static void DebugLog(Func<string> message)
        {
            if (settings.enableLogging)
                mod?.Logger.Log(message());
        }
    }
}
