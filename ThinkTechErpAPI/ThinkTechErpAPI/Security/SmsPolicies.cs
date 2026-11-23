namespace ThinkTechErpAPI.Security;

public static class SmsPolicies
{
    /// <summary>SMS.V or SMS.M: read providers, purchases, allocations and organization balances.</summary>
    public const string View = "SmsView";

    /// <summary>SMS.M: change providers, purchases and allocations; record organization usage.</summary>
    public const string Manage = "SmsManage";

    /// <summary>
    /// The provider gateway (api/sms/gateway): SMS.V or SMS.M, or an integration client with the sms.usage scope
    /// (e.g. MediDesk, with its token from api/security/integration/token).
    /// </summary>
    public const string Gateway = "SmsGateway";

    /// <summary>Sending through the provider gateway: SMS.M, or an integration client with the sms.usage scope.</summary>
    public const string GatewaySend = "SmsGatewaySend";
}
