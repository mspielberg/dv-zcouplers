using System.Collections;

using UnityEngine;

namespace DvMod.ZCouplers
{
    /// <summary>
    /// Ensures hose GameObjects remain hidden even if re-enabled by game optimizers/LODs.
    /// </summary>
    internal sealed class HoseHider : MonoBehaviour
    {
        public static void Attach(Transform t)
        {
            if (t == null)
                return;
            if (t.GetComponent<HoseHider>() == null)
                t.gameObject.AddComponent<HoseHider>();
        }

        private void OnEnable()
        {
            // Only enforce in profiles that require hiding hoses (e.g., Schaku)
            if (CouplerProfiles.Current?.Options.AlwaysHideAirHoses != true)
                return;

            // Immediately disable visuals and object
            HideNow();
            // Also re-assert next frame in case something else flips it this frame
            StartCoroutine(DisableNextFrame());
        }

        private IEnumerator DisableNextFrame()
        {
            yield return null;
            HideNow();
        }

        private void HideNow()
        {
            try
            {
                var renderers = GetComponentsInChildren<MeshRenderer>(true);
                foreach (var r in renderers)
                    r.enabled = false;
                gameObject.SetActive(false);
            }
            catch { }
        }
    }
}