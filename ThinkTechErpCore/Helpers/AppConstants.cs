namespace ThinkTechErpCore.Helpers;

/// <summary>
/// SMS gateways by their public.sms_provider.sms_provider_id. Callers of the SMS gateway API
/// (api/sms/gateway/{providerId}) pass one of these to pick the provider.
/// </summary>
public static class SmsProviderConstants
{
    public const int ADN = 5;
    public const int CDC = 6;
}
