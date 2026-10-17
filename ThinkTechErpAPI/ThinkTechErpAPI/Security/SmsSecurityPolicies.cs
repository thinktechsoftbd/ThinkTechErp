namespace ThinkTechErpAPI.Security;

public static class SmsSecurityPolicies
{
    public const string Admin = "SmsAdmin";
    public const string ClientOrAdmin = "SmsClientOrAdmin";
    public const string RateLimit = "SmsApiRateLimit";
}
