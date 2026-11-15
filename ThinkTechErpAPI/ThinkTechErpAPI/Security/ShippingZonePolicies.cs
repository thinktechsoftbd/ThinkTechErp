namespace ThinkTechErpAPI.Security;

public static class ShippingZonePolicies
{
    /// <summary>SHIPPING_ZONE.V or SHIPPING_ZONE.M: read shipping zones.</summary>
    public const string View = "ShippingZoneView";

    /// <summary>SHIPPING_ZONE.M: add, edit and delete shipping zones.</summary>
    public const string Manage = "ShippingZoneManage";
}
