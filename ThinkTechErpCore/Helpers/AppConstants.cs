namespace ThinkTechErpCore.Helpers;

/// <summary>
/// SMS gateways by their public.sms_provider.sms_provider_id: the id each gateway client (ISmsGatewayClient) is for.
/// Callers of the SMS gateway API (api/sms/gateway) name no provider: their organization's sms_provider_id picks it.
/// </summary>
public static class SmsProviderConstants
{
    public const int ADN = 5;
    public const int CDC = 6;
}
