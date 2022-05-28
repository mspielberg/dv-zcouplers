using HarmonyLib;
using System;
using UnityModManagerNet;

namespace DvMod.ZCouplers
{
    [EnableReloading]
    public static class Main
    {
        public static UnityModManager.ModEntry? mod;
        public static Settings settings = new Settings();
        public static bool enabled;

        static public bool Load(UnityModManager.ModEntry modEntry)
        {
            mod = modEntry;

            if (UnityModManager.FindMod("ZRealism")?.Version is Version zrealismVersion && zrealismVersion != null && zrealismVersion < new Version(0, 4, 0))
            {
                mod.Logger.Error("ZCouplers is not compatible with versions of ZRealism before 0.4.0");
                return false;
            }

            try
            {
                var loaded = Settings.Load<Settings>(modEntry);
                if (loaded.version == modEntry.Info.Version)
                    settings = loaded;
                else
                    settings = new Settings();
            }
            catch
            {
                settings = new Settings();
            }

            modEntry.OnGUI = OnGui;
            modEntry.OnSaveGUI = OnSaveGui;
            HeadsUpDisplayBridge.Init();
            Harmony harmony = new Harmony(modEntry.Info.Id);
            harmony.PatchAll();

            // Force static initializer to execute and load asset bundle
            if (KnuckleCouplers.enabled)
            {
                mod.Logger.Log("Loaded {settings.couplerType}");
            }
            CCLIntegration.Initialize();

            return true;
        }

        static private void OnGui(UnityModManager.ModEntry modEntry)
        {
            settings.Draw(modEntry);
        }

        static private void OnSaveGui(UnityModManager.ModEntry modEntry)
        {
            settings.Save(modEntry);
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
