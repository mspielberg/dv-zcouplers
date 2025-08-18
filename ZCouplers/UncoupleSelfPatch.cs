using HarmonyLib;

namespace DvMod.ZCouplers;

[HarmonyPatch(typeof(TrainCar), "UncoupleSelf")]
public static class UncoupleSelfPatch
{
    public static void Postfix(TrainCar __instance)
    {
        Main.DebugLog(() => "TrainCar.UncoupleSelf.Postfix");
        // Try to auto-disconnect air systems on both ends if they had partners
        try
        {
            var fc = __instance.frontCoupler;
            var rc = __instance.rearCoupler;
            if (fc?.coupledTo != null)
                AirSystemAutomation.TryAutoDisconnect(fc, fc.coupledTo);
            if (rc?.coupledTo != null)
                AirSystemAutomation.TryAutoDisconnect(rc, rc.coupledTo);
        }
        catch { }

        JointManager.DestroyCompressionJoint(__instance.frontCoupler, "UncoupleSelf");
        JointManager.DestroyCompressionJoint(__instance.rearCoupler, "UncoupleSelf");
        JointManager.DestroyTensionJoint(__instance.frontCoupler);
        JointManager.DestroyTensionJoint(__instance.rearCoupler);
        CouplingScannerPatches.KillCouplingScanner(__instance.frontCoupler);
        CouplingScannerPatches.KillCouplingScanner(__instance.rearCoupler);
    }
}