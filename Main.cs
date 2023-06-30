using BepInEx;
using HarmonyLib;
using System;

namespace DvMod.ZCouplers
{
    [BepInPlugin(GUID, "ZCouplers", Version)]
    public class Main : BaseUnityPlugin
    {
        public const string GUID = "com.github.mspielberg.dv-zcouplers";
        public const string Version = "1.0.0";

        public static Main? Instance;

        public static Settings settings;

        public void Awake()
        {
            Instance = this;
            settings = new Settings(Config);

            // HeadsUpDisplayBridge.Init();
            Harmony harmony = new Harmony(GUID);
            harmony.PatchAll();

            // Force static initializer to execute and load asset bundle
            if (KnuckleCouplers.enabled)
            {
                Logger.LogInfo("Loaded {settings.couplerType}");
            }
            // CCLIntegration.Initialize(DebugLog);
        }

        public static void DebugLog(TrainCar car, Func<string> message)
        {
            if (car == PlayerManager.Car)
                DebugLog(message);
        }

        public static void DebugLog(Func<string> message)
        {
            if (settings.enableLogging.Value)
                Instance?.Logger.LogDebug(message());
        }
    }
}
