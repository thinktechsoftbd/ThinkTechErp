using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ThinkTechErpRepository.Database;
using ThinkTechErpRepository.Security;
using ThinkTechErpRepository.Settings;
using ThinkTechErpRepository.Website;
using ThinkTechErpRepository.Sms;
using ThinkTechErpService.Security;
using ThinkTechErpService.Settings;
using ThinkTechErpService.Sms;
using ThinkTechErpService.Website;

namespace ThinkTechErpService;

public static class DependencyInjection
{
    public static IServiceCollection AddThinkTechErpServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionStrings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var schema in new[] { "Website", "Sms", "Security", "SmsApi" })
        {
            var connectionString = configuration.GetConnectionString(schema);
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException(
                    $"Connection string '{schema}' is missing. Set ConnectionStrings__{schema}.");
            }

            connectionStrings.Add(schema, connectionString);
        }

        services.AddSingleton<IDbConnectionFactory>(new NpgsqlConnectionFactory(connectionStrings));
        services.AddScoped<IWebsiteRepository, WebsiteRepository>();
        services.AddScoped<IWebsiteService, WebsiteService>();
        services.AddScoped<ISmsRepository, SmsRepository>();
        services.AddScoped<ISmsService, SmsService>();

        // Security. The host must also register IAccessTokenIssuer and SecurityTokenSettings.
        services.AddScoped<ISecurityUserRepository, SecurityUserRepository>();
        services.AddScoped<ISecurityRoleRepository, SecurityRoleRepository>();
        services.AddScoped<ISecurityPermissionRepository, SecurityPermissionRepository>();
        services.AddScoped<ISecurityMenuRepository, SecurityMenuRepository>();
        services.AddScoped<IOrganizationRepository, OrganizationRepository>();
        services.AddScoped<IAuthTokenRepository, AuthTokenRepository>();
        services.AddSingleton<IPasswordHasher, BcryptPasswordHasher>();
        services.TryAddSingleton<IPasswordResetNotifier, LoggingPasswordResetNotifier>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<ISecurityUserService, SecurityUserService>();
        services.AddScoped<ISecurityRoleService, SecurityRoleService>();
        services.AddScoped<ISecurityPermissionService, SecurityPermissionService>();
        services.AddScoped<ISecurityMenuService, SecurityMenuService>();
        services.AddScoped<IOrganizationService, OrganizationService>();
        services.AddScoped<ISubscriptionModuleRepository, SubscriptionModuleRepository>();
        services.AddScoped<ISubscriptionRepository, SubscriptionRepository>();
        services.AddScoped<ISubscriptionModuleService, SubscriptionModuleService>();
        services.AddScoped<ISubscriptionService, SubscriptionService>();

        // Settings
        services.AddScoped<ILookupRepository, LookupRepository>();
        services.AddScoped<ILookupService, LookupService>();
        return services;
    }
}
