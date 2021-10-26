using HarmonyLib;
using UnityEngine;

namespace DvMod.ZCouplers
{
    public static class Mock
    {
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
        public static class Exit_EnabledPatch
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
        private const float ShankSize = 0.1f;
        private static void CreateMocks(Coupler coupler)
        {
            var frontPivot = new GameObject("ZCouplers pivot");
            frontPivot.transform.SetParent(coupler.transform, false);
            frontPivot.transform.localPosition = PivotLength * Vector3.back;

            var frontCube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Component.DestroyImmediate(frontCube.GetComponent<Collider>());
            frontCube.name = "shank";
            frontCube.transform.SetParent(frontPivot.transform, false);
            frontCube.transform.localPosition = PivotLength * 0.5f * Vector3.forward;
            frontCube.transform.localScale = new Vector3(ShankSize, ShankSize, PivotLength);
        }
    }
}