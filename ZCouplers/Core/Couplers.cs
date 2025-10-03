using DvMod.ZCouplers.Physics;

namespace DvMod.ZCouplers.Core
{
    /// <summary>
    /// Legacy Couplers class - most functionality moved to specialized classes.
    /// This remains for backward compatibility and any remaining utilities.
    /// </summary>
    public static class Couplers
    {
        // Delegate to JointManager
        public static bool HasTensionJoint(Coupler coupler) => JointManager.HasTensionJoint(coupler);
        public static void ForceCreateTensionJoint(Coupler coupler) => JointManager.ForceCreateTensionJoint(coupler);
        public static void UpdateAllCompressionJoints() => JointManager.UpdateAllCompressionJoints();
    }
}
