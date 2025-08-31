namespace DvMod.ZCouplers.Core.Helpers;

public static class CouplerExtensions
{
    public static string Position(this Coupler coupler)
    {
        if (!coupler.isFrontCoupler)
        {
            return "rear";
        }
        return "front";
    }
}
