using UnityEngine;
using DV.CabControls;

namespace DvMod.ZCouplers
{
    public class KnuckleCouplers
    {
        public static KnuckleCouplers? Instance { get; private set; }
        
        // Temporarily match the old working code exactly
        public static bool enabled => true; // Always enabled like the old code
        
        public KnuckleCouplers()
        {
            Instance = this;
            // Initialize asset manager
            AssetManager.LoadAssets();
        }
        
        // Asset management delegation
        public static GameObject? GetHookPrefab() => AssetManager.GetHookPrefab();
        
        // Hook management delegation
        public static void CreateHook(ChainCouplerInteraction chainCoupler) => HookManager.CreateHook(chainCoupler, GetHookPrefab());
        public static void DestroyHook(ChainCouplerInteraction chainCoupler) => HookManager.DestroyHook(chainCoupler);
        public static void UpdateCouplerVisualState(Coupler coupler, bool locked) => KnuckleCouplerState.UpdateCouplerVisualState(coupler, locked);
        public static void EnsureKnuckleCouplersForTrain(TrainCar car) => HookManager.EnsureKnuckleCouplersForTrain(car, GetHookPrefab());
        
        // Coupler state management delegation
        public static bool IsUnlocked(Coupler coupler) => KnuckleCouplerState.IsUnlocked(coupler);
        public static bool IsReadyToCouple(Coupler coupler) => KnuckleCouplerState.IsReadyToCouple(coupler);
        public static void UnlockCoupler(Coupler coupler, bool viaChainInteraction) => KnuckleCouplerState.UnlockCoupler(coupler, viaChainInteraction);
        public static void ReadyCoupler(Coupler coupler) => KnuckleCouplerState.ReadyCoupler(coupler);
        public static void SetCouplerLocked(Coupler coupler, bool locked) => KnuckleCouplerState.SetCouplerLocked(coupler, locked);
        public static bool HasUnlockedCoupler(Trainset trainset) => KnuckleCouplerState.HasUnlockedCoupler(trainset);
        
        public static void OnSettingsChanged()
        {
            if (Instance == null)
            {
                new KnuckleCouplers();
            }
            BufferVisualManager.ToggleBuffers(Main.settings.showBuffersWithKnuckles);
        }
        
        // Called from Main.Load()
        public static void Initialize()
        {
            if (Instance == null)
            {
                new KnuckleCouplers();
            }
        }
        
    }
}
