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
                settings = loaded.version == modEntry.Info.Version ? loaded : new Settings();
            }
            catch
            {
                settings = new Settings();
            }

            modEntry.OnGUI = settings.Draw;
            modEntry.OnSaveGUI = settings.Save;

            // HeadsUpDisplayBridge.Init();
            Harmony harmony = new Harmony(modEntry.Info.Id);
            harmony.PatchAll();

            // Force static initializer to execute and load asset bundle
            if (KnuckleCouplers.enabled)
                mod.Logger.Log("Loaded {settings.couplerType}");

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
