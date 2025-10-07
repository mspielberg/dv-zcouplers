using DvMod.ZCouplers.Core;
using DvMod.ZCouplers.Core.Helpers;
using DvMod.ZCouplers.Core.Profiles;
using UnityEngine;

namespace DvMod.ZCouplers.Visuals
{
    /// <summary>
    /// Component that handles visual updates for knuckle couplers during attachment
    /// This ensures the visual rotation works by calling LateUpdateVisible on ChainCouplerInteraction
    /// </summary>
    public class CouplerVisualUpdater : MonoBehaviour
    {
        private ChainCouplerInteraction? chainScript;

        private void Start()
        {
            chainScript = GetComponent<ChainCouplerInteraction>();
            if (chainScript == null)
            {
                Main.ErrorLog(() => "CouplerVisualUpdater: No ChainCouplerInteraction found on this GameObject");
                Destroy(this);
            }
        }

        private void LateUpdate()
        {
            if (!KnuckleCouplers.enabled)
                return;

            // Initialize chainScript if it's null (in case Start() wasn't called or component wasn't ready)
            if (chainScript == null)
            {
                chainScript = GetComponent<ChainCouplerInteraction>();
                if (chainScript == null)
                {
                    Main.ErrorLog(() => "CouplerVisualUpdater: No ChainCouplerInteraction found on this GameObject");
                    Destroy(this);
                    return;
                }
            }

            // Update LAP links based on current coupler states
            if (CouplerProfiles.Current?.ProfileId == "LAP")
            {
                var coupler = chainScript.couplerAdapter?.coupler;
                if (coupler != null && coupler.IsCoupled())
                {
                    var otherCoupler = coupler.coupledTo;
                    if (otherCoupler != null)
                    {
                        LAPLinkManager.CreateOrShowLink(coupler, otherCoupler);
                    }
                }

                // Update all LAP link transforms to handle movement and curves
                // This is done from one updater to avoid duplicate updates
                if (chainScript.couplerAdapter?.coupler?.Position() == "front")
                {
                    LAPLinkManager.UpdateAllLinkTransforms();
                }

                // Don't run AdjustPivot for LAP Couplers
                return;
            }

            // Check if this coupler is physically coupled but state doesn't reflect it
            bool isCoupled = chainScript.couplerAdapter?.IsCoupled() == true;

            // Use physical coupling state instead of relying on chainScript.state
            // since the state might not be updated yet due to timing issues
            if (isCoupled)
            {
                try
                {
                    // Get our pivot and the other coupler's pivot
                    var pivot = HookManager.GetPivot(chainScript);
                    var partnerCoupler = chainScript.couplerAdapter?.coupler?.coupledTo;

                    if (pivot != null && partnerCoupler?.visualCoupler?.chain != null)
                    {
                        var otherPivot = HookManager.GetPivot(partnerCoupler.visualCoupler.chain.GetComponent<ChainCouplerInteraction>());

                        if (otherPivot != null)
                        {
                            // Directly call AdjustPivot to rotate our visual toward the other coupler
                            HookManager.AdjustPivot(pivot, otherPivot);
                        }
                    }
                }
                catch (System.Exception ex)
                {
                    Main.ErrorLog(() => $"Exception in CouplerVisualUpdater.LateUpdate: {ex.Message}");
                }
            }
        }
    }
}
