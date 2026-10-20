using System.Collections.Generic;
using DV.Items;
using DvMod.ZCouplers.Core;
using DvMod.ZCouplers.Core.Profiles;
using UnityEngine;

namespace DvMod.ZCouplers.Visuals
{
    /// <summary>
    /// Moves the vanilla coupler gadget snap point (ItemSnapPointCoupler) onto a named empty inside the
    /// active hook prefab. The snap point stays parented to the car interior and is posed every frame,
    /// so hook swaps and profile switches that destroy the hook instance never take it or a snapped
    /// gadget with them. Without an anchor the snap point returns to its vanilla pose.
    /// </summary>
    internal sealed class GadgetAnchorFollower : MonoBehaviour
    {
        private static readonly List<GadgetAnchorFollower> instances = new List<GadgetAnchorFollower>();

        private ChainCouplerInteraction chainScript = null!;
        private Transform interior = null!;

        // Vanilla pose: position relative to the car interior, rotation relative to the coupler.
        private Vector3 vanillaLocalPosition;
        private Quaternion vanillaLocalRotation;
        private Quaternion rotationInCoupler;

        private Transform? anchor;
        private bool atVanillaPose = true;

        public static void Attach(ChainCouplerInteraction chainScript)
        {
            var coupler = chainScript?.couplerAdapter?.coupler;
            var snapPoint = coupler?.visualCoupler?.couplerSnapPoint;
            var interior = coupler?.train?.interior;
            if (snapPoint == null || interior == null)
                return;
            if (snapPoint.GetComponent<GadgetAnchorFollower>() != null)
                return;

            var follower = snapPoint.gameObject.AddComponent<GadgetAnchorFollower>();
            follower.chainScript = chainScript!;
            follower.interior = interior;
            var t = snapPoint.transform;
            follower.vanillaLocalPosition = interior.InverseTransformPoint(t.position);
            follower.vanillaLocalRotation = Quaternion.Inverse(interior.rotation) * t.rotation;
            follower.rotationInCoupler = Quaternion.Inverse(coupler!.transform.rotation) * t.rotation;
        }

        /// <summary>
        /// Return every snap point to its vanilla pose and remove the followers. Called during mod unload.
        /// </summary>
        public static void Cleanup()
        {
            foreach (var follower in instances.ToArray())
            {
                if (follower != null)
                {
                    follower.RestoreVanillaPose();
                    DestroyImmediate(follower);
                }
            }
            instances.Clear();
        }

        private void Awake() => instances.Add(this);

        private void OnDestroy()
        {
            instances.Remove(this);
            RestoreVanillaPose();
        }

        private void LateUpdate()
        {
            if (interior == null)
                return;

            var pivot = KnuckleCouplers.enabled ? HookManager.GetPivot(chainScript) : null;
            var target = pivot != null ? ResolveAnchor(pivot) : null;
            if (target == null)
            {
                RestoreVanillaPose();
                return;
            }

            // Keep the vanilla orientation and let it turn with the hook pivot.
            transform.SetPositionAndRotation(target.position, pivot!.rotation * rotationInCoupler);
            atVanillaPose = false;
        }

        private Transform? ResolveAnchor(Transform pivot)
        {
            if (anchor != null && anchor.gameObject.activeInHierarchy && anchor.IsChildOf(pivot))
                return anchor;

            anchor = null;
            var anchorName = CouplerProfiles.Current?.Options.GadgetAnchorChildName;
            if (string.IsNullOrEmpty(anchorName))
                return null;

            // Skip hooks waiting for deferred destroy after a swap; they are renamed and inactive.
            for (int i = 0; i < pivot.childCount; i++)
            {
                var hook = pivot.GetChild(i);
                if (!hook.gameObject.activeSelf)
                    continue;
                anchor = FindRecursive(hook, anchorName!);
                if (anchor != null)
                    return anchor;
            }
            return null;
        }

        private static Transform? FindRecursive(Transform parent, string name)
        {
            if (parent.name == name)
                return parent;
            for (int i = 0; i < parent.childCount; i++)
            {
                var result = FindRecursive(parent.GetChild(i), name);
                if (result != null)
                    return result;
            }
            return null;
        }

        private void RestoreVanillaPose()
        {
            if (atVanillaPose || interior == null)
                return;
            transform.SetPositionAndRotation(
                interior.TransformPoint(vanillaLocalPosition),
                interior.rotation * vanillaLocalRotation);
            atVanillaPose = true;
        }
    }
}
