using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
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
using ThinkTechErpService.Storage;
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
        services.TryAddSingleton<IPasswordResetNotifier, EmailPasswordResetNotifier>();
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
        services.AddScoped<ISubscriptionPlanRepository, SubscriptionPlanRepository>();
        services.AddScoped<ISubscriptionPlanService, SubscriptionPlanService>();

        // Settings
        services.AddScoped<ILookupRepository, LookupRepository>();
        services.AddScoped<ILookupService, LookupService>();
        services.AddScoped<IAreaRepository, AreaRepository>();
        services.AddScoped<IAreaService, AreaService>();
        services.AddScoped<IDivisionRepository, DivisionRepository>();
        services.AddScoped<IDivisionService, DivisionService>();
        services.AddScoped<IDistrictRepository, DistrictRepository>();
        services.AddScoped<IDistrictService, DistrictService>();
        services.AddScoped<IShippingZoneRepository, ShippingZoneRepository>();
        services.AddScoped<IShippingZoneService, ShippingZoneService>();

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
        services.AddScoped<IInvoiceVoucherRepository, InvoiceVoucherRepository>();
        services.AddScoped<IInvoiceVoucherService, InvoiceVoucherService>();

        // File storage (FileStorage section): the ThinkTech storage API keeps the files for product.image_url,
        // organization.logo_url and User.photoUrl.
        services.Configure<FileStorageOptions>(configuration.GetSection(FileStorageOptions.SectionName));
        services.AddSingleton<FileStorageTokenCache>();
        services.AddHttpClient<IFileStorageService, FileStorageService>((provider, client) =>
        {
            // Left unset when missing or not an http(s) address: the service then answers that storage isn't set up.
            var baseUrl = provider.GetRequiredService<IOptions<FileStorageOptions>>().Value.BaseUrl?.Trim().TrimEnd('/');
            if (Uri.TryCreate(baseUrl + "/", UriKind.Absolute, out var address) && address.Scheme is "https" or "http")
                client.BaseAddress = address;
        });
        return services;
    }
}
