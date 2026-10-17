using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ThinkTechErpRepository.Database;
using ThinkTechErpRepository.Website;
using ThinkTechErpRepository.Sms;
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
        return services;
    }
}
