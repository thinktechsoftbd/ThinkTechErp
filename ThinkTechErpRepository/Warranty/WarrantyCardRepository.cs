using Dapper;
using ThinkTechErpDomain.Models.Common;
using ThinkTechErpDomain.Models.Security;
using ThinkTechErpDomain.Models.Warranty;
using ThinkTechErpRepository.Common;
using ThinkTechErpRepository.Database;
using ThinkTechErpRepository.Security;

namespace ThinkTechErpRepository.Warranty;

public interface IWarrantyCardRepository
{
    Task<PagedResult<WarrantyCard>> GetCardsAsync(int organizationId, WarrantyCardQuery query, CancellationToken cancellationToken = default);
    Task<WarrantyCard?> GetCardAsync(int organizationId, long id, CancellationToken cancellationToken = default);
    /// <summary>Cards matching a serial no., warranty no., invoice no. or mobile exactly (case-insensitive).</summary>
    Task<IReadOnlyList<WarrantyCard>> FindCardsAsync(int organizationId, string query, CancellationToken cancellationToken = default);
    /// <summary>Serial-tracked units with this serial, with the posted sale each last went out on.</summary>
    Task<IReadOnlyList<SerialSaleInfo>> FindSerialsAsync(int organizationId, string serialNo, CancellationToken cancellationToken = default);
    /// <summary>A manual card (fully worked out by the caller); returns its id.</summary>
    Task<long> CreateManualCardAsync(int organizationId, WarrantyCard card, int userId, CancellationToken cancellationToken = default);
    /// <summary>False when the card isn't Active.</summary>
    Task<bool> VoidCardAsync(int organizationId, long id, string reason, int userId, CancellationToken cancellationToken = default);
}

public sealed class WarrantyCardRepository(IDbConnectionFactory connectionFactory) : IWarrantyCardRepository
{
    /// <summary>What card w covers today (UTC), as in public.warranty_coverage.</summary>
    public const string CoverageSql = """
        CASE WHEN w.status <> 'Active' THEN w.status
             WHEN w.replacement_end_date >= (now() AT TIME ZONE 'utc')::date THEN 'InReplacement'
             WHEN w.end_date >= (now() AT TIME ZONE 'utc')::date THEN 'InWarranty'
             ELSE 'Expired' END
        """;

    private const string CardFrom = """
        FROM public.warranty_registration w
        JOIN public.product p ON p.product_id = w.product_id
        LEFT JOIN public.brand b ON b.brand_id = p.brand_id
        LEFT JOIN public.sales_invoice i ON i.sales_invoice_id = w.sales_invoice_id
        LEFT JOIN public.supplier c ON c.supplier_id = w.customer_id
        LEFT JOIN public.warranty_policy wp ON wp.warranty_policy_id = w.warranty_policy_id
        LEFT JOIN public.supplier sp ON sp.supplier_id = w.supplier_id
        LEFT JOIN public.warranty_registration o ON o.warranty_registration_id = w.replaces_registration_id
        """;

    private const string CardSelect = $"""
        SELECT w.warranty_registration_id AS Id, w.object_id AS ObjectId, w.warranty_no AS WarrantyNo, w.source AS Source,
               w.sales_invoice_id AS SalesInvoiceId, i.invoice_no AS InvoiceNo, i.invoice_date AS InvoiceDate,
               w.sales_invoice_line_id AS SalesInvoiceLineId, w.product_id AS ProductId, p.product_code AS ProductCode,
               p.product_name AS ProductName, b.brand_name AS BrandName, w.serial_number_id AS SerialNumberId,
               w.serial_no AS SerialNo, w.quantity AS Quantity, w.customer_id AS CustomerId, c.supplier_code AS CustomerCode,
               coalesce(c.supplier_name, w.customer_name) AS CustomerName, coalesce(w.customer_mobile, c.mobile) AS CustomerMobile,
               w.warranty_policy_id AS WarrantyPolicyId, wp.policy_name AS WarrantyPolicyName, w.start_date AS StartDate,
               w.replacement_end_date AS ReplacementEndDate, w.end_date AS EndDate, w.covers_parts AS CoversParts,
               w.covers_labor AS CoversLabor, w.terms AS Terms, w.supplier_id AS SupplierId, sp.supplier_name AS SupplierName,
               w.supplier_warranty_end_date AS SupplierWarrantyEndDate, w.replaces_registration_id AS ReplacesRegistrationId,
               o.warranty_no AS ReplacesWarrantyNo,
               (SELECT n.warranty_registration_id FROM public.warranty_registration n
                WHERE n.replaces_registration_id = w.warranty_registration_id
                ORDER BY n.warranty_registration_id DESC LIMIT 1) AS ReplacedByRegistrationId,
               (SELECT n.warranty_no FROM public.warranty_registration n
                WHERE n.replaces_registration_id = w.warranty_registration_id
                ORDER BY n.warranty_registration_id DESC LIMIT 1) AS ReplacedByWarrantyNo,
               w.status AS Status, {CoverageSql} AS Coverage,
               greatest(w.end_date - (now() AT TIME ZONE 'utc')::date, 0) AS DaysRemaining,
               w.void_reason AS VoidReason, w.remarks AS Remarks,
               (SELECT count(*) FROM public.service_job j WHERE j.warranty_registration_id = w.warranty_registration_id)::int AS JobCount,
               w.created_at AT TIME ZONE 'utc' AS CreatedAt, w.created_by AS CreatedBy, w.updated_at AT TIME ZONE 'utc' AS UpdatedAt
        {CardFrom}
        """;

    private const string CardFilter = $"""
        WHERE w.organization_id = @Org
          AND (CAST(@Pattern AS text) IS NULL OR w.warranty_no ILIKE @Pattern OR w.serial_no ILIKE @Pattern
               OR c.supplier_name ILIKE @Pattern OR w.customer_name ILIKE @Pattern OR w.customer_mobile ILIKE @Pattern
               OR c.mobile ILIKE @Pattern OR p.product_code ILIKE @Pattern OR p.product_name ILIKE @Pattern
               OR i.invoice_no ILIKE @Pattern)
          AND (CAST(@Status AS text) IS NULL OR w.status = @Status)
          AND (CAST(@Coverage AS text) IS NULL OR {CoverageSql} = @Coverage)
          AND (CAST(@Source AS text) IS NULL OR w.source = @Source)
          AND (CAST(@ProductId AS int) IS NULL OR w.product_id = @ProductId)
          AND (CAST(@CustomerId AS int) IS NULL OR w.customer_id = @CustomerId)
          AND (CAST(@From AS date) IS NULL OR w.start_date >= @From)
          AND (CAST(@To AS date) IS NULL OR w.start_date <= @To)
        """;

    public async Task<PagedResult<WarrantyCard>> GetCardsAsync(int organizationId, WarrantyCardQuery query, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        return await RepositoryHelpers.PagedAsync<WarrantyCard>(connection,
            $"SELECT count(*) {CardFrom} {CardFilter}",
            $"{CardSelect} {CardFilter} ORDER BY w.start_date DESC, w.warranty_registration_id DESC",
            new
            {
                Org = organizationId,
                Pattern = RepositoryHelpers.Pattern(query.Search),
                query.Status,
                query.Coverage,
                query.Source,
                query.ProductId,
                query.CustomerId,
                query.From,
                query.To
            }, query.Limit, query.Offset, cancellationToken);
    }

    public async Task<WarrantyCard?> GetCardAsync(int organizationId, long id, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<WarrantyCard>(new CommandDefinition(
            $"{CardSelect} WHERE w.organization_id = @Org AND w.warranty_registration_id = @Id",
            new { Org = organizationId, Id = id }, cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<WarrantyCard>> FindCardsAsync(int organizationId, string query, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        return (await connection.QueryAsync<WarrantyCard>(new CommandDefinition($"""
            {CardSelect}
            WHERE w.organization_id = @Org
              AND (upper(w.serial_no) = upper(@Q) OR upper(w.warranty_no) = upper(@Q) OR upper(i.invoice_no) = upper(@Q)
                   OR w.customer_mobile = @Q OR c.mobile = @Q)
            ORDER BY (w.status = 'Active') DESC, w.end_date DESC, w.warranty_registration_id DESC
            LIMIT 50
            """, new { Org = organizationId, Q = query }, cancellationToken: cancellationToken))).AsList();
    }

    public async Task<IReadOnlyList<SerialSaleInfo>> FindSerialsAsync(int organizationId, string serialNo, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        return (await connection.QueryAsync<SerialSaleInfo>(new CommandDefinition("""
            SELECT s.serial_number_id AS SerialNumberId, s.serial_no AS SerialNo, s.product_id AS ProductId,
                   p.product_code AS ProductCode, p.product_name AS ProductName, s.status AS Status,
                   sale.sales_invoice_id AS SalesInvoiceId, sale.invoice_no AS InvoiceNo, sale.invoice_date AS InvoiceDate,
                   sale.customer_name AS CustomerName, sale.customer_mobile AS CustomerMobile,
                   EXISTS (SELECT 1 FROM public.warranty_registration w
                           WHERE w.serial_number_id = s.serial_number_id AND w.status = 'Active') AS HasActiveCard
            FROM public.serial_number s
            JOIN public.product p ON p.product_id = s.product_id
            LEFT JOIN LATERAL (
                SELECT i.sales_invoice_id, i.invoice_no, i.invoice_date, coalesce(c.supplier_name, i.customer_name) AS customer_name,
                       coalesce(i.customer_mobile, c.mobile) AS customer_mobile
                FROM public.sales_invoice_serial ss
                JOIN public.sales_invoice_line l ON l.sales_invoice_line_id = ss.sales_invoice_line_id
                JOIN public.sales_invoice i ON i.sales_invoice_id = l.sales_invoice_id
                LEFT JOIN public.supplier c ON c.supplier_id = i.customer_id
                WHERE ss.serial_number_id = s.serial_number_id AND i.status = 'Posted'
                ORDER BY i.invoice_date DESC, i.sales_invoice_id DESC
                LIMIT 1) sale ON true
            WHERE s.organization_id = @Org AND upper(s.serial_no) = upper(@SerialNo)
            ORDER BY p.product_name
            LIMIT 20
            """, new { Org = organizationId, SerialNo = serialNo }, cancellationToken: cancellationToken))).AsList();
    }

    public async Task<long> CreateManualCardAsync(int organizationId, WarrantyCard card, int userId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await OrganizationGuard.EnsureAsync(connection, transaction, organizationId, "product", [card.ProductId], cancellationToken);
        // The customer pickers list every party (a supplier can buy too), as on sales invoices.
        await OrganizationGuard.EnsureAsync(connection, transaction, organizationId, "party", [card.CustomerId], cancellationToken);
        await OrganizationGuard.EnsureAsync(connection, transaction, organizationId, "supplier", [card.SupplierId], cancellationToken);

        if (card.SerialNo is not null && await connection.QuerySingleAsync<bool>(new CommandDefinition("""
                SELECT EXISTS (SELECT 1 FROM public.warranty_registration
                               WHERE organization_id = @Org AND product_id = @ProductId AND status = 'Active'
                                 AND upper(serial_no) = upper(@SerialNo))
                """, new { Org = organizationId, card.ProductId, card.SerialNo }, transaction, cancellationToken: cancellationToken)))
            throw new BusinessConflictException($"Serial {card.SerialNo} of this product already has a warranty card in force.");

        var number = await DocumentNumbers.NextAsync(connection, transaction, organizationId, DocumentType.WarrantyCard, cancellationToken);
        var id = await connection.QuerySingleAsync<long>(new CommandDefinition($"""
            INSERT INTO public.warranty_registration (
                organization_id, warranty_no, source, product_id, serial_number_id, serial_no, quantity, customer_id,
                customer_name, customer_mobile, warranty_policy_id, start_date, replacement_end_date, end_date, covers_parts,
                covers_labor, terms, supplier_id, supplier_warranty_end_date, status, remarks, created_at, created_by)
            VALUES (
                @Org, @Number, 'Manual', @ProductId, @SerialNumberId, @SerialNo, @Quantity, @CustomerId,
                @CustomerName, @CustomerMobile, @WarrantyPolicyId, @StartDate, @ReplacementEndDate, @EndDate, @CoversParts,
                @CoversLabor, @Terms, @SupplierId, @SupplierWarrantyEndDate, 'Active', @Remarks, {SecurityDb.NowUtc}, @UserId)
            RETURNING warranty_registration_id
            """, new
            {
                Org = organizationId,
                Number = number,
                card.ProductId,
                card.SerialNumberId,
                card.SerialNo,
                card.Quantity,
                card.CustomerId,
                CustomerName = card.CustomerId is null ? card.CustomerName : null,
                card.CustomerMobile,
                card.WarrantyPolicyId,
                card.StartDate,
                card.ReplacementEndDate,
                card.EndDate,
                card.CoversParts,
                card.CoversLabor,
                card.Terms,
                card.SupplierId,
                card.SupplierWarrantyEndDate,
                card.Remarks,
                UserId = userId
            }, transaction, cancellationToken: cancellationToken));
        await transaction.CommitAsync(cancellationToken);
        return id;
    }

    public async Task<bool> VoidCardAsync(int organizationId, long id, string reason, int userId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        return await connection.ExecuteAsync(new CommandDefinition($"""
            UPDATE public.warranty_registration
            SET status = 'Void', void_reason = @Reason, updated_at = {SecurityDb.NowUtc}, updated_by = @UserId
            WHERE organization_id = @Org AND warranty_registration_id = @Id AND status = 'Active'
            """, new { Org = organizationId, Id = id, Reason = reason, UserId = userId }, cancellationToken: cancellationToken)) > 0;
    }
}
