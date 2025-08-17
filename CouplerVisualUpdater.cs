using UnityEngine;

namespace DvMod.ZCouplers
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
            if (!KnuckleCouplers.enabled || chainScript == null)
                return;

            // Check if this coupler is physically coupled but state doesn't reflect it
            bool isCoupled = chainScript.couplerAdapter?.IsCoupled() == true;
            
            // Debug: Log the current state periodically
            if (Time.frameCount % 60 == 0) // Every 60 frames (~1 second at 60fps)
            {
                Main.DebugLog(() => $"CouplerVisualUpdater: {chainScript.couplerAdapter?.coupler?.train?.ID} state: {chainScript.state}, IsCoupled: {isCoupled}, attachedTo: {chainScript.attachedTo?.couplerAdapter?.coupler?.train?.ID}");
            }

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
