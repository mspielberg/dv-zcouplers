using HarmonyLib;

namespace DvMod.ZCouplers;

[HarmonyPatch(typeof(TrainCar), "UncoupleSelf")]
public static class UncoupleSelfPatch
{
    public static void Postfix(TrainCar __instance)
    {
        Main.DebugLog(() => "TrainCar.UncoupleSelf.Postfix");
        JointManager.DestroyCompressionJoint(__instance.frontCoupler, "UncoupleSelf");
        JointManager.DestroyCompressionJoint(__instance.rearCoupler, "UncoupleSelf");
        JointManager.DestroyTensionJoint(__instance.frontCoupler);
        JointManager.DestroyTensionJoint(__instance.rearCoupler);
        CouplingScannerPatches.KillCouplingScanner(__instance.frontCoupler);
        CouplingScannerPatches.KillCouplingScanner(__instance.rearCoupler);
    }
}