using System.Collections.Generic;
using DvMod.ZCouplers.Core;
using DvMod.ZCouplers.Core.Utils;
using DvMod.ZCouplers.Physics;
using HarmonyLib;

namespace DvMod.ZCouplers
{
	/// <summary>
	/// Minimal patches to enforce client-side behavior without littering core code.
	/// </summary>
	public static class Patches
	{
		[HarmonyPatch(typeof(JointManager), nameof(JointManager.ForceCreateTensionJoint))]
		public static class PreventForceCreateOnClient
		{
			public static bool Prefix()
			{
				return !MultiplayerIntegration.IsClientActive; // skip on client
			}
		}

		// Prevent the client from running deferred coupler state/joint application,
		// which is intended for SP/host after save load and causes duplicate joint creation when joining MP.
		[HarmonyPatch(typeof(DeferredStateApplicator), nameof(DeferredStateApplicator.StartDeferredCouplerApplication))]
		public static class PreventDeferredStateOnClient
		{
			public static bool Prefix()
			{
				return !MultiplayerIntegration.IsClientActive; // skip entire routine on client
			}
		}

		// Patch RecordUncoupling to make it host-authoritative
		[HarmonyPatch(typeof(RecouplingPrevention), nameof(RecouplingPrevention.RecordUncoupling))]
		public static class RecouplingPreventionMpPatch
		{
			public static bool Prefix(Coupler coupler1, Coupler coupler2)
			{
				// On client: skip the entire RecordUncoupling method
				// The client will receive the block via packet from the host
				if (MultiplayerIntegration.IsClientActive)
				{
					return false; // Don't run RecordUncoupling on client
				}

				// On host or single player: let it run normally
				return true;
			}

			public static void Postfix(Coupler coupler1, Coupler coupler2)
			{
				// Only host should broadcast the block to clients
				// This runs after RecordUncoupling completes on the host
				if (MultiplayerIntegration.IsHost && coupler1 != null && coupler2 != null)
				{
					MultiplayerIntegration.HostBroadcastRecouplingBlock(coupler1, coupler2);
				}
			}
		}

		// Prevent the coroutine from starting on clients by blocking EnsureInitialized
		// This is a backup safety measure, though RecordUncoupling should already be blocked
		[HarmonyPatch(typeof(RecouplingPrevention), nameof(RecouplingPrevention.EnsureInitialized))]
		public static class PreventRecouplingCoroutineOnClient
		{
			public static bool Prefix()
			{
				// Only allow the coroutine to start on host or in single player
				var isClient = MultiplayerIntegration.IsClientActive;
				Main.DebugLog(() => $"[MP] EnsureInitialized called - IsClientActive: {isClient}, allowing: {!isClient}");
				return !isClient;
			}
		}

		// Patch UpdateBlockedPairs to broadcast unblocks when separation is achieved
		[HarmonyPatch(typeof(RecouplingPrevention), nameof(RecouplingPrevention.UpdateBlockedPairs))]
		public static class BroadcastRecouplingUnblocks
		{
			public static void Prefix(out HashSet<RecouplingPrevention.CouplerPair>? __state)
			{
				__state = null;
				if (!MultiplayerIntegration.IsHost)
					return;

				// Capture the current blocked pairs before UpdateBlockedPairs runs
				__state = new System.Collections.Generic.HashSet<RecouplingPrevention.CouplerPair>(RecouplingPrevention.blockedPairs);
			}

			public static void Postfix(System.Collections.Generic.HashSet<RecouplingPrevention.CouplerPair> __state)
			{
				if (!MultiplayerIntegration.IsHost || __state == null)
					return;

				// Find pairs that were blocked before but are no longer blocked
				foreach (var pair in __state)
				{
					if (!RecouplingPrevention.blockedPairs.Contains(pair))
					{
						// This pair was unblocked - broadcast it
						var c1 = pair.GetCoupler1();
						var c2 = pair.GetCoupler2();
						if (c1 != null && c2 != null)
						{
							MultiplayerIntegration.HostBroadcastRecouplingUnblock(c1, c2);
						}
					}
				}
			}
		}

		// Patch the host's joint destruction to also record uncoupling for recoupling prevention
		// This ensures that when a client uncouples and the host processes it, the host records it
		[HarmonyPatch(typeof(JointManager), nameof(JointManager.DestroyTensionJoint))]
		public static class HostRecordUncouplingOnJointDestroy
		{
			// Track what we've already recorded to avoid duplicates
			private static readonly HashSet<string> recordedThisFrame = new HashSet<string>();
			private static int lastFrameCleared = -1;

			public static void Prefix(Coupler coupler)
			{
				// Clear the set each frame
				if (UnityEngine.Time.frameCount != lastFrameCleared)
				{
					recordedThisFrame.Clear();
					lastFrameCleared = UnityEngine.Time.frameCount;
				}

				// Only on host: when destroying a tension joint, record the uncoupling
				if (!MultiplayerIntegration.IsHost || coupler == null)
				{
					Main.DebugLog(() => $"[MP] DestroyTensionJoint - IsHost: {MultiplayerIntegration.IsHost}, coupler null: {coupler == null}, skipping");
					return;
				}

				var otherCoupler = coupler.coupledTo;
				if (otherCoupler != null)
				{
					// Create a unique key for this pair to avoid recording twice
					var key = $"{coupler.GetHashCode()}-{otherCoupler.GetHashCode()}";
					if (recordedThisFrame.Contains(key))
					{
						Main.DebugLog(() => $"[MP] Already recorded this uncoupling this frame, skipping");
						return;
					}
					recordedThisFrame.Add(key);

					Main.DebugLog(() => $"[MP] Host recording uncoupling from DestroyTensionJoint: {coupler.train.ID} <-> {otherCoupler.train.ID}");
					// Record the uncoupling on the host so the coroutine tracks it
					// This handles the case where a client uncouples and the host processes the joint destruction
					// This will also run for host-initiated uncouples, but that's OK - it just adds to the tracking
					RecouplingPrevention.RecordUncoupling(coupler, otherCoupler);
				}
				else
				{
					Main.DebugLog(() => $"[MP] DestroyTensionJoint - coupler.coupledTo is null, can't record");
				}
			}
		}
	}
}
