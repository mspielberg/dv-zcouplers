using System.Collections;

using UnityEngine;

namespace DvMod.ZCouplers
{
    /// <summary>
    /// Ensures a GameObject and its MeshRenderers remain hidden even if re-enabled by game optimizers/LODs.
    /// Generic version of the old HoseHider.
    /// </summary>
    internal sealed class GameObjHider : MonoBehaviour
    {
        public static void Attach(Transform t)
        {
            if (t == null)
                return;
            if (t.GetComponent<GameObjHider>() == null)
                t.gameObject.AddComponent<GameObjHider>();
        }

        public static void Detach(Transform t)
        {
            if (t == null)
                return;
            var hh = t.GetComponent<GameObjHider>();
            if (hh != null)
                Destroy(hh);
        }

        private void OnEnable()
        {
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
