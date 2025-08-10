using System;
using HarmonyLib;

namespace DvMod.ZCouplers;

[HarmonyPatch(typeof(CarSpawner), "PrepareTrainCarForDeleting")]
public static class PrepareTrainCarForDeletingPatch
{
	public static void Postfix(TrainCar trainCar)
	{
		try
		{
			if (!(trainCar == null))
			{
				Main.DebugLog(() => "TrainCar.PrepareTrainCarForDeleting.Postfix for " + trainCar.ID);
				SafeCleanupCoupler(trainCar.frontCoupler);
				SafeCleanupCoupler(trainCar.rearCoupler);
			}
		}
		catch (Exception ex)
		{
			Exception ex2 = ex;
			Exception ex3 = ex2;
			Main.DebugLog(() => "Error during car deletion cleanup: " + ex3.Message);
		}
	}

	private static void SafeCleanupCoupler(Coupler coupler)
	{
		if (coupler == null || coupler.gameObject == null)
		{
			return;
		}
		try
		{
			JointManager.DestroyCompressionJoint(coupler, "PrepareForDeleting");
			JointManager.DestroyTensionJoint(coupler);
			CouplingScannerPatches.KillCouplingScanner(coupler);
		}
		catch (Exception ex)
		{
			Exception ex2 = ex;
			Exception ex3 = ex2;
			Main.DebugLog(() => "Error cleaning up coupler: " + ex3.Message);
		}
	}
}
