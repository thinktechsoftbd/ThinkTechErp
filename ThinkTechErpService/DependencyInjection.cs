using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ThinkTechErpRepository.Accounting;
using ThinkTechErpRepository.Common;
using ThinkTechErpRepository.Database;
using ThinkTechErpRepository.Inventory;
using ThinkTechErpRepository.Purchase;
using ThinkTechErpRepository.Sales;
using ThinkTechErpRepository.Security;
using ThinkTechErpRepository.Settings;
using ThinkTechErpRepository.Website;
using ThinkTechErpRepository.Sms;
using ThinkTechErpService.Accounting;
using ThinkTechErpService.Inventory;
using ThinkTechErpService.Purchase;
using ThinkTechErpService.Sales;
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
        var connectionString = configuration.GetConnectionString("ThinkTechErp");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Connection string 'ThinkTechErp' is missing. Set ConnectionStrings__ThinkTechErp.");
        }

        // Created by the container so it disposes the data source on shutdown.
        services.AddSingleton<IDbConnectionFactory>(_ => new NpgsqlConnectionFactory(connectionString));
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
        services.AddScoped<IOrganizationClientRepository, OrganizationClientRepository>();
        services.AddScoped<IAuthTokenRepository, AuthTokenRepository>();
        services.AddSingleton<IPasswordHasher, BcryptPasswordHasher>();
        services.TryAddSingleton<IPasswordResetNotifier, LoggingPasswordResetNotifier>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<ISecurityUserService, SecurityUserService>();
        services.AddScoped<ISecurityRoleService, SecurityRoleService>();
        services.AddScoped<ISecurityPermissionService, SecurityPermissionService>();
        services.AddScoped<ISecurityMenuService, SecurityMenuService>();
        services.AddScoped<IOrganizationService, OrganizationService>();
        services.AddScoped<IOrganizationClientService, OrganizationClientService>();
        services.AddScoped<ISubscriptionModuleRepository, SubscriptionModuleRepository>();
        services.AddScoped<ISubscriptionRepository, SubscriptionRepository>();
        services.AddScoped<ISubscriptionModuleService, SubscriptionModuleService>();
        services.AddScoped<ISubscriptionService, SubscriptionService>();

        // Settings
        services.AddScoped<ILookupRepository, LookupRepository>();
        services.AddScoped<ILookupService, LookupService>();

        // Organizations a user works in (Inventory, Purchase)
        services.AddScoped<IUserOrganizationRepository, UserOrganizationRepository>();
        services.AddScoped<IUserOrganizationService, UserOrganizationService>();
        services.AddScoped<IOwnershipRepository, OwnershipRepository>();

        // Inventory
        services.AddScoped<IInventorySetupRepository, InventorySetupRepository>();
        services.AddScoped<IProductRepository, ProductRepository>();
        services.AddScoped<IStockDocumentRepository, StockDocumentRepository>();
        services.AddScoped<IInventoryReportRepository, InventoryReportRepository>();
        services.AddScoped<IInventorySetupService, InventorySetupService>();
        services.AddScoped<IProductService, ProductService>();
        services.AddScoped<IStockDocumentService, StockDocumentService>();
        services.AddScoped<IInventoryReportService, InventoryReportService>();
        services.AddScoped<IPaymentSourceRepository, PaymentSourceRepository>();
        services.AddScoped<IPaymentSourceService, PaymentSourceService>();

        // Purchase
        services.AddScoped<IPurchaseSetupRepository, PurchaseSetupRepository>();
        services.AddScoped<IPurchaseDocumentRepository, PurchaseDocumentRepository>();
        services.AddScoped<IPurchaseReportRepository, PurchaseReportRepository>();
        services.AddScoped<IPurchaseSetupService, PurchaseSetupService>();
        services.AddScoped<IPurchaseDocumentService, PurchaseDocumentService>();
        services.AddScoped<IPurchaseReportService, PurchaseReportService>();

        // Sales
        services.AddScoped<ISalesSetupRepository, SalesSetupRepository>();
        services.AddScoped<ISalesDocumentRepository, SalesDocumentRepository>();
        services.AddScoped<ISalesReportRepository, SalesReportRepository>();
        services.AddScoped<ISalesSetupService, SalesSetupService>();
        services.AddScoped<ISalesDocumentService, SalesDocumentService>();
        services.AddScoped<ISalesReportService, SalesReportService>();

        // Accounting
        services.AddScoped<ILedgerLookupRepository, LedgerLookupRepository>();
        services.AddScoped<IVoucherRepository, VoucherRepository>();
        services.AddScoped<IAccountingReportRepository, AccountingReportRepository>();
        services.AddScoped<ILedgerLookupService, LedgerLookupService>();
        services.AddScoped<IVoucherService, VoucherService>();
        services.AddScoped<IAccountingReportService, AccountingReportService>();
        services.AddScoped<IAccountClassRepository, AccountClassRepository>();
        services.AddScoped<IAccountClassService, AccountClassService>();
        return services;
    }
}
