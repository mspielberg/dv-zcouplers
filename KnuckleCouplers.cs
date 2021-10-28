using System.IO;
using HarmonyLib;
using UnityEngine;

namespace DvMod.ZCouplers
{
    public static class KnuckleCouplers
    {
        private static readonly AssetBundle bundle = AssetBundle.LoadFromFile(Path.Combine(Main.mod.Path, "knucklecoupler"));
        private static readonly GameObject hookPrefab = bundle.LoadAsset<GameObject>("hook");

        [HarmonyPatch(typeof(ChainCouplerInteraction), nameof(ChainCouplerInteraction.Entry_Enabled))]
        public static class Entry_EnabledPatch
        {
            public static void Postfix(ChainCouplerInteraction __instance)
            {
                CreateMocks(__instance.couplerAdapter.coupler);
            }
        }

        [HarmonyPatch(typeof(ChainCouplerInteraction), nameof(ChainCouplerInteraction.Exit_Enabled))]
        public static class Exit_EnabledPatch
        {
            public static void Postfix(ChainCouplerInteraction __instance)
            {
                GameObject.Destroy(GetPivot(__instance).gameObject);
            }
        }

        private static void YLookAt(Transform t1, Transform t2)
        {
            t1.localEulerAngles = Vector3.zero;
            var offset = t1.InverseTransformPoint(t2.position);
            var angle = Vector3.SignedAngle(Vector3.forward, offset, Vector3.up);
            t1.localEulerAngles = new Vector3(0, angle, 0);
        }

        [HarmonyPatch(typeof(ChainCouplerInteraction), nameof(ChainCouplerInteraction.LateUpdate_Attached))]
        public static class LateUpdate_AttachedPatch
        {
            public static void Postfix(ChainCouplerInteraction __instance)
            {
                var pivot = GetPivot(__instance);
                var otherPivot = GetPivot(__instance.attachedTo);
                if (pivot != null && otherPivot != null)
                {
                    YLookAt(pivot, otherPivot);
                    YLookAt(otherPivot, pivot);
                }
            }
        }

        [HarmonyPatch(typeof(ChainCouplerInteraction), nameof(ChainCouplerInteraction.Exit_Attached))]
        public static class Exit_AttachedPatch
        {
            public static void Postfix(ChainCouplerInteraction __instance)
            {
                GetPivot(__instance).localEulerAngles = Vector3.zero;
            }
        }

        private static Transform? GetPivot(ChainCouplerInteraction chainScript)
        {
            return chainScript.couplerAdapter.coupler.transform.Find("ZCouplers pivot");
        }

        private const float PivotLength = 1.0f;
        private const float HeightOffset = -0.067f;
        private static void CreateMocks(Coupler coupler)
        {
            var frontPivot = new GameObject("ZCouplers pivot");
            frontPivot.transform.SetParent(coupler.transform, false);
            frontPivot.transform.localPosition = new Vector3(0, HeightOffset, -PivotLength);

            var hook = GameObject.Instantiate(hookPrefab);
            hook.transform.SetParent(frontPivot.transform, false);
            hook.transform.localPosition = PivotLength * Vector3.forward;
        }
    }
}