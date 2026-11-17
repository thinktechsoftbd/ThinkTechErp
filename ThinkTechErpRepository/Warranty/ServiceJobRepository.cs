using Dapper;
using Npgsql;
using ThinkTechErpDomain.Models.Common;
using ThinkTechErpDomain.Models.Security;
using ThinkTechErpDomain.Models.Warranty;
using ThinkTechErpRepository.Common;
using ThinkTechErpRepository.Database;
using ThinkTechErpRepository.Security;

namespace ThinkTechErpRepository.Warranty;

public interface IServiceJobRepository
{
    Task<PagedResult<ServiceJob>> GetJobsAsync(int organizationId, ServiceJobQuery query, CancellationToken cancellationToken = default);
    /// <summary>The job with its items, timeline and supplier claims.</summary>
    Task<ServiceJob?> GetJobAsync(int organizationId, long id, CancellationToken cancellationToken = default);
    /// <summary>Jobs for the same serial, mobile or job no., or opened under one of these cards; newest first.</summary>
    Task<IReadOnlyList<ServiceJob>> FindJobsAsync(int organizationId, string query, IReadOnlyCollection<long> cardIds, CancellationToken cancellationToken = default);

    /// <summary>A serial number of this organization (of the product, when given), matched case-insensitively.</summary>
    Task<IReadOnlyList<(long SerialNumberId, int ProductId)>> FindSerialNumbersAsync(int organizationId, int? productId, string serialNo, CancellationToken cancellationToken = default);

    /// <summary>Opens a job (fully checked by the caller); returns its id.</summary>
    Task<long> CreateJobAsync(int organizationId, ServiceJob job, int userId, CancellationToken cancellationToken = default);
    /// <summary>Changes the intake, diagnosis and estimate of an open job. False when it doesn't exist.</summary>
    Task<bool> UpdateJobAsync(int organizationId, long id, ServiceJob job, int userId, CancellationToken cancellationToken = default);
    Task<DeleteOutcome> DeleteJobAsync(int organizationId, long id, CancellationToken cancellationToken = default);
    /// <summary>Replaces the items of an open job that isn't billed.</summary>
    Task SaveItemsAsync(int organizationId, long id, IReadOnlyList<ServiceJobItem> items, int userId, CancellationToken cancellationToken = default);

    Task ChangeStatusAsync(int organizationId, long id, ServiceJobStatusRequest request, int userId, CancellationToken cancellationToken = default);
    Task SetApprovalAsync(int organizationId, long id, ServiceJobApprovalRequest request, int userId, CancellationToken cancellationToken = default);
    Task DeliverAsync(int organizationId, long id, ServiceJobDeliverRequest request, int userId, CancellationToken cancellationToken = default);
    Task CancelAsync(int organizationId, long id, string reason, int userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Links the job to the invoice made for it. False when it is no longer open, or already billed by an invoice
    /// that isn't cancelled.
    /// </summary>
    Task<bool> LinkInvoiceAsync(int organizationId, long id, long invoiceId, int userId, CancellationToken cancellationToken = default);

    Task<PagedResult<SupplierClaim>> GetClaimsAsync(int organizationId, SupplierClaimQuery query, CancellationToken cancellationToken = default);
    Task<SupplierClaim?> GetClaimAsync(int organizationId, long id, CancellationToken cancellationToken = default);
    /// <summary>Records a claim (fully checked by the caller); a job still with the shop moves to AtSupplier.</summary>
    Task<long> CreateClaimAsync(int organizationId, SupplierClaim claim, int userId, CancellationToken cancellationToken = default);
    Task<bool> UpdateClaimAsync(int organizationId, long id, SupplierClaim claim, int userId, CancellationToken cancellationToken = default);
    /// <summary>The unit is back; the job (when AtSupplier) goes back to InRepair.</summary>
    Task ReturnClaimAsync(int organizationId, long id, SupplierClaim result, int userId, CancellationToken cancellationToken = default);
    Task CancelClaimAsync(int organizationId, long id, int userId, CancellationToken cancellationToken = default);
}

public sealed class ServiceJobRepository(IDbConnectionFactory connectionFactory) : IServiceJobRepository
{
    private async Task<NpgsqlConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    // =====================================================================
    // Reading
    // =====================================================================

    private const string JobFrom = """
        FROM public.service_job j
        JOIN public.lookup_data dt ON dt.lookup_data_id = j.device_type_lookup_data_id
        LEFT JOIN public.supplier c ON c.supplier_id = j.customer_id
        LEFT JOIN public.product p ON p.product_id = j.product_id
        LEFT JOIN public.warranty_registration w ON w.warranty_registration_id = j.warranty_registration_id
        LEFT JOIN public."User" t ON t.id = j.technician_user_id
        LEFT JOIN public.sales_invoice i ON i.sales_invoice_id = j.sales_invoice_id
        """;

    private const string JobSelect = $"""
        SELECT j.service_job_id AS Id, j.object_id AS ObjectId, j.job_no AS JobNo, j.received_at AT TIME ZONE 'utc' AS ReceivedAt,
               j.job_type AS JobType, j.service_location AS ServiceLocation, j.priority AS Priority,
               j.customer_id AS CustomerId, c.supplier_code AS CustomerCode, coalesce(c.supplier_name, j.customer_name) AS CustomerName,
               coalesce(j.customer_mobile, c.mobile) AS CustomerMobile, j.service_address AS ServiceAddress,
               j.device_type_lookup_data_id AS DeviceTypeLookupDataId, dt.lookup_data_name AS DeviceType,
               j.product_id AS ProductId, p.product_code AS ProductCode, p.product_name AS ProductName,
               j.serial_number_id AS SerialNumberId, j.brand_name AS BrandName, j.model_name AS ModelName, j.serial_no AS SerialNo,
               j.accessories_received AS AccessoriesReceived, j.physical_condition AS PhysicalCondition,
               j.problem_description AS ProblemDescription,
               j.warranty_registration_id AS WarrantyRegistrationId, w.warranty_no AS WarrantyNo,
               CASE WHEN w.warranty_registration_id IS NOT NULL THEN {WarrantyCardRepository.CoverageSql} END AS WarrantyCoverage,
               w.end_date AS WarrantyEndDate, j.warranty_void_reason AS WarrantyVoidReason,
               j.technician_user_id AS TechnicianUserId, t.name AS TechnicianName, j.diagnosis AS Diagnosis,
               j.estimated_amount AS EstimatedAmount, j.approval_status AS ApprovalStatus,
               j.approved_at AT TIME ZONE 'utc' AS ApprovedAt, j.promised_date AS PromisedDate,
               j.status AS Status, j.resolution AS Resolution, j.resolution_note AS ResolutionNote,
               j.completed_at AT TIME ZONE 'utc' AS CompletedAt, j.delivered_at AT TIME ZONE 'utc' AS DeliveredAt,
               j.delivered_by AS DeliveredBy, j.delivered_to_name AS DeliveredToName, j.cancel_reason AS CancelReason,
               j.sales_invoice_id AS SalesInvoiceId, i.invoice_no AS InvoiceNo, i.status AS InvoiceStatus,
               i.total_amount AS InvoiceTotal, i.total_amount - i.settled_amount AS InvoiceDue,
               coalesce((SELECT sum(x.line_total) FROM public.service_job_item x WHERE x.service_job_id = j.service_job_id), 0) AS ItemsTotal,
               (SELECT count(*) FROM public.service_job_item x WHERE x.service_job_id = j.service_job_id)::int AS ItemCount,
               (SELECT count(*) FROM public.service_supplier_claim sc
                WHERE sc.service_job_id = j.service_job_id AND sc.status = 'Sent')::int AS OpenClaimCount,
               j.remarks AS Remarks, j.created_at AT TIME ZONE 'utc' AS CreatedAt, j.created_by AS CreatedBy,
               j.updated_at AT TIME ZONE 'utc' AS UpdatedAt
        {JobFrom}
        """;

    private const string JobFilter = """
        WHERE j.organization_id = @Org
          AND (CAST(@Pattern AS text) IS NULL OR j.job_no ILIKE @Pattern OR c.supplier_name ILIKE @Pattern
               OR j.customer_name ILIKE @Pattern OR j.customer_mobile ILIKE @Pattern OR c.mobile ILIKE @Pattern
               OR j.serial_no ILIKE @Pattern OR j.brand_name ILIKE @Pattern OR j.model_name ILIKE @Pattern
               OR p.product_name ILIKE @Pattern OR w.warranty_no ILIKE @Pattern OR i.invoice_no ILIKE @Pattern)
          AND (CAST(@Status AS text) IS NULL
               OR (@Status = 'Open' AND j.status NOT IN ('Delivered', 'Cancelled')) OR j.status = @Status)
          AND (CAST(@JobType AS text) IS NULL OR j.job_type = @JobType)
          AND (CAST(@TechnicianUserId AS int) IS NULL OR j.technician_user_id = @TechnicianUserId)
          AND (CAST(@CustomerId AS int) IS NULL OR j.customer_id = @CustomerId)
          AND (CAST(@DeviceTypeLookupDataId AS bigint) IS NULL OR j.device_type_lookup_data_id = @DeviceTypeLookupDataId)
          AND (CAST(@From AS date) IS NULL OR j.received_at >= CAST(@From AS date))
          AND (CAST(@To AS date) IS NULL OR j.received_at < CAST(@To AS date) + 1)
        """;

    public async Task<PagedResult<ServiceJob>> GetJobsAsync(int organizationId, ServiceJobQuery query, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        return await RepositoryHelpers.PagedAsync<ServiceJob>(connection,
            $"SELECT count(*) {JobFrom} {JobFilter}",
            $"{JobSelect} {JobFilter} ORDER BY j.received_at DESC, j.service_job_id DESC",
            new
            {
                Org = organizationId,
                Pattern = RepositoryHelpers.Pattern(query.Search),
                query.Status,
                query.JobType,
                query.TechnicianUserId,
                query.CustomerId,
                query.DeviceTypeLookupDataId,
                query.From,
                query.To
            }, query.Limit, query.Offset, cancellationToken);
    }

    public async Task<ServiceJob?> GetJobAsync(int organizationId, long id, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        var job = await connection.QuerySingleOrDefaultAsync<ServiceJob>(new CommandDefinition(
            $"{JobSelect} WHERE j.organization_id = @Org AND j.service_job_id = @Id",
            new { Org = organizationId, Id = id }, cancellationToken: cancellationToken));
        if (job is null)
            return null;

        using var details = await connection.QueryMultipleAsync(new CommandDefinition($"""
            SELECT x.service_job_item_id AS Id, x.line_no AS LineNo, x.product_id AS ProductId, p.product_code AS ProductCode,
                   p.product_name AS ProductName, p.product_type AS ProductType, p.tracking_type AS TrackingType,
                   x.description AS Description, x.unit_id AS UnitId, u.unit_code AS UnitCode, x.quantity AS Quantity,
                   x.batch_id AS BatchId, b.batch_no AS BatchNo, x.serial_number_id AS SerialNumberId, s.serial_no AS SerialNo,
                   x.unit_price AS UnitPrice, x.discount_amount AS DiscountAmount, x.line_total AS LineTotal,
                   x.is_warranty_covered AS IsWarrantyCovered, x.sales_invoice_line_id AS SalesInvoiceLineId, x.remarks AS Remarks
            FROM public.service_job_item x
            JOIN public.product p ON p.product_id = x.product_id
            JOIN public.unit u ON u.unit_id = x.unit_id
            LEFT JOIN public.batch b ON b.batch_id = x.batch_id
            LEFT JOIN public.serial_number s ON s.serial_number_id = x.serial_number_id
            WHERE x.service_job_id = @Id
            ORDER BY x.line_no;

            SELECT h.service_job_status_history_id AS Id, h.from_status AS FromStatus, h.to_status AS ToStatus, h.note AS Note,
                   h.customer_notified AS CustomerNotified, h.changed_at AT TIME ZONE 'utc' AS ChangedAt,
                   h.changed_by AS ChangedBy, u.name AS ChangedByName
            FROM public.service_job_status_history h
            LEFT JOIN public."User" u ON u.id = h.changed_by
            WHERE h.service_job_id = @Id
            ORDER BY h.changed_at, h.service_job_status_history_id;

            {ClaimSelect} WHERE sc.service_job_id = @Id ORDER BY sc.sent_date, sc.service_supplier_claim_id;
            """, new { Id = id }, cancellationToken: cancellationToken));
        job.Items = (await details.ReadAsync<ServiceJobItem>()).AsList();
        job.History = (await details.ReadAsync<ServiceJobStatusChange>()).AsList();
        job.Claims = (await details.ReadAsync<SupplierClaim>()).AsList();
        return job;
    }

    public async Task<IReadOnlyList<ServiceJob>> FindJobsAsync(
        int organizationId, string query, IReadOnlyCollection<long> cardIds, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        return (await connection.QueryAsync<ServiceJob>(new CommandDefinition($"""
            {JobSelect}
            WHERE j.organization_id = @Org
              AND (upper(j.serial_no) = upper(@Q) OR upper(j.job_no) = upper(@Q) OR j.customer_mobile = @Q OR c.mobile = @Q
                   OR j.warranty_registration_id = ANY(@CardIds))
            ORDER BY j.received_at DESC, j.service_job_id DESC
            LIMIT 20
            """, new { Org = organizationId, Q = query, CardIds = cardIds.ToArray() }, cancellationToken: cancellationToken))).AsList();
    }

    public async Task<IReadOnlyList<(long SerialNumberId, int ProductId)>> FindSerialNumbersAsync(
        int organizationId, int? productId, string serialNo, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        return (await connection.QueryAsync<(long, int)>(new CommandDefinition("""
            SELECT serial_number_id, product_id FROM public.serial_number
            WHERE organization_id = @Org AND upper(serial_no) = upper(@SerialNo)
              AND (CAST(@ProductId AS int) IS NULL OR product_id = @ProductId)
            LIMIT 5
            """, new { Org = organizationId, ProductId = productId, SerialNo = serialNo }, cancellationToken: cancellationToken))).AsList();
    }

    // =====================================================================
    // Writing a job
    // =====================================================================

    private sealed class JobState
    {
        public long Id { get; set; }
        public string JobNo { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string? Resolution { get; set; }
        public string ApprovalStatus { get; set; } = string.Empty;
        public decimal EstimatedAmount { get; set; }
        public long? SalesInvoiceId { get; set; }
        public string? InvoiceNo { get; set; }
        public string? InvoiceStatus { get; set; }
    }

    /// <summary>Locks the job row; throws when it isn't this organization's.</summary>
    private static async Task<JobState> LockJobAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, int organizationId, long id, CancellationToken cancellationToken)
        => await connection.QuerySingleOrDefaultAsync<JobState>(new CommandDefinition("""
            SELECT j.service_job_id AS Id, j.job_no AS JobNo, j.status AS Status, j.resolution AS Resolution,
                   j.approval_status AS ApprovalStatus, j.estimated_amount AS EstimatedAmount, j.sales_invoice_id AS SalesInvoiceId,
                   (SELECT i.invoice_no FROM public.sales_invoice i WHERE i.sales_invoice_id = j.sales_invoice_id) AS InvoiceNo,
                   (SELECT i.status FROM public.sales_invoice i WHERE i.sales_invoice_id = j.sales_invoice_id) AS InvoiceStatus
            FROM public.service_job j
            WHERE j.organization_id = @Org AND j.service_job_id = @Id
            FOR UPDATE OF j
            """, new { Org = organizationId, Id = id }, transaction, cancellationToken: cancellationToken))
           ?? throw new BusinessRuleException("The service job doesn't exist.");

    private static void RequireOpen(JobState job)
    {
        if (!ServiceJobStatus.Open.Contains(job.Status))
            throw new BusinessRuleException($"Job {job.JobNo} is {job.Status.ToLowerInvariant()}; it can't be changed any more.");
    }

    /// <summary>An invoice that isn't cancelled bills the job.</summary>
    private static bool IsBilled(JobState job) => job.SalesInvoiceId is not null && job.InvoiceStatus != DocumentStatus.Cancelled;

    private static Task AddHistoryAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, long jobId, string? from, string to, string? note,
        bool customerNotified, int userId, CancellationToken cancellationToken)
        => connection.ExecuteAsync(new CommandDefinition($"""
            INSERT INTO public.service_job_status_history (service_job_id, from_status, to_status, note, customer_notified, changed_at, changed_by)
            VALUES (@JobId, @From, @To, @Note, @Notified, {SecurityDb.NowUtc}, @UserId)
            """, new { JobId = jobId, From = from, To = to, Note = note, Notified = customerNotified, UserId = userId },
            transaction, cancellationToken: cancellationToken));

    private static async Task EnsureJobReferencesAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, int organizationId, ServiceJob job, CancellationToken cancellationToken)
    {
        await OrganizationGuard.EnsureAsync(connection, transaction, organizationId, "party", [job.CustomerId], cancellationToken);
        await OrganizationGuard.EnsureAsync(connection, transaction, organizationId, "product", [job.ProductId], cancellationToken);
        if (job.WarrantyRegistrationId is not null && !await connection.QuerySingleAsync<bool>(new CommandDefinition(
                "SELECT EXISTS (SELECT 1 FROM public.warranty_registration WHERE organization_id = @Org AND warranty_registration_id = @Id)",
                new { Org = organizationId, Id = job.WarrantyRegistrationId }, transaction, cancellationToken: cancellationToken)))
            throw new BusinessRuleException("The warranty card doesn't exist.");
    }

    private static object JobParams(int organizationId, ServiceJob job, int userId) => new
    {
        Org = organizationId,
        job.Id,
        // Unset (default): received now.
        ReceivedAt = job.ReceivedAt == default ? (DateTime?)null : job.ReceivedAt,
        job.JobType,
        job.ServiceLocation,
        job.Priority,
        job.CustomerId,
        CustomerName = job.CustomerId is null ? job.CustomerName : null,
        job.CustomerMobile,
        job.ServiceAddress,
        job.DeviceTypeLookupDataId,
        job.ProductId,
        job.SerialNumberId,
        job.BrandName,
        job.ModelName,
        job.SerialNo,
        job.AccessoriesReceived,
        job.PhysicalCondition,
        job.ProblemDescription,
        job.WarrantyRegistrationId,
        job.WarrantyVoidReason,
        job.TechnicianUserId,
        job.Diagnosis,
        job.EstimatedAmount,
        job.PromisedDate,
        job.Remarks,
        UserId = userId
    };

    public async Task<long> CreateJobAsync(int organizationId, ServiceJob job, int userId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await EnsureJobReferencesAsync(connection, transaction, organizationId, job, cancellationToken);

        var parameters = new DynamicParameters(JobParams(organizationId, job, userId));
        parameters.Add("Number", await DocumentNumbers.NextAsync(connection, transaction, organizationId, DocumentType.ServiceJob, cancellationToken));
        var id = await connection.QuerySingleAsync<long>(new CommandDefinition($"""
            INSERT INTO public.service_job (
                organization_id, job_no, received_at, job_type, service_location, priority, customer_id, customer_name,
                customer_mobile, service_address, device_type_lookup_data_id, product_id, serial_number_id, brand_name,
                model_name, serial_no, accessories_received, physical_condition, problem_description, warranty_registration_id,
                warranty_void_reason, technician_user_id, diagnosis, estimated_amount, promised_date, status, remarks,
                created_at, created_by)
            VALUES (
                @Org, @Number, coalesce(CAST(@ReceivedAt AS timestamp), {SecurityDb.NowUtc}), @JobType, @ServiceLocation, @Priority,
                @CustomerId, @CustomerName, @CustomerMobile, @ServiceAddress, @DeviceTypeLookupDataId, @ProductId,
                @SerialNumberId, @BrandName, @ModelName, @SerialNo, @AccessoriesReceived, @PhysicalCondition,
                @ProblemDescription, @WarrantyRegistrationId, @WarrantyVoidReason, @TechnicianUserId, @Diagnosis,
                @EstimatedAmount, @PromisedDate, 'Received', @Remarks, {SecurityDb.NowUtc}, @UserId)
            RETURNING service_job_id
            """, parameters, transaction, cancellationToken: cancellationToken));
        await AddHistoryAsync(connection, transaction, id, null, ServiceJobStatus.Received, "Device received.", false, userId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return id;
    }

    public async Task<bool> UpdateJobAsync(int organizationId, long id, ServiceJob job, int userId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var state = await connection.QuerySingleOrDefaultAsync<string>(new CommandDefinition(
            "SELECT status FROM public.service_job WHERE organization_id = @Org AND service_job_id = @Id FOR UPDATE",
            new { Org = organizationId, Id = id }, transaction, cancellationToken: cancellationToken));
        if (state is null)
            return false;
        if (!ServiceJobStatus.Open.Contains(state))
            throw new BusinessRuleException($"The job is {state.ToLowerInvariant()}; it can't be changed any more.");
        await EnsureJobReferencesAsync(connection, transaction, organizationId, job, cancellationToken);

        job.Id = id;
        await connection.ExecuteAsync(new CommandDefinition($"""
            UPDATE public.service_job
            SET job_type = @JobType, service_location = @ServiceLocation, priority = @Priority, customer_id = @CustomerId,
                customer_name = @CustomerName, customer_mobile = @CustomerMobile, service_address = @ServiceAddress,
                device_type_lookup_data_id = @DeviceTypeLookupDataId, product_id = @ProductId, serial_number_id = @SerialNumberId,
                brand_name = @BrandName, model_name = @ModelName, serial_no = @SerialNo,
                accessories_received = @AccessoriesReceived, physical_condition = @PhysicalCondition,
                problem_description = @ProblemDescription, warranty_registration_id = @WarrantyRegistrationId,
                warranty_void_reason = @WarrantyVoidReason, technician_user_id = @TechnicianUserId, diagnosis = @Diagnosis,
                estimated_amount = @EstimatedAmount, promised_date = @PromisedDate, remarks = @Remarks,
                updated_at = {SecurityDb.NowUtc}, updated_by = @UserId
            WHERE service_job_id = @Id
            """, JobParams(organizationId, job, userId), transaction, cancellationToken: cancellationToken));
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async Task<DeleteOutcome> DeleteJobAsync(int organizationId, long id, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        var (status, invoiceId) = await connection.QuerySingleOrDefaultAsync<(string? Status, long? InvoiceId)>(new CommandDefinition(
            "SELECT status, sales_invoice_id FROM public.service_job WHERE organization_id = @Org AND service_job_id = @Id",
            new { Org = organizationId, Id = id }, cancellationToken: cancellationToken));
        if (status is null)
            return DeleteOutcome.NotFound;
        // Only a job opened by mistake: once work has started its history is kept (cancel it instead).
        if (status != ServiceJobStatus.Received || invoiceId is not null)
            return DeleteOutcome.InUse;
        return await RepositoryHelpers.DeleteAsync(connection, new CommandDefinition(
            "DELETE FROM public.service_job WHERE organization_id = @Org AND service_job_id = @Id AND status = 'Received' AND sales_invoice_id IS NULL",
            new { Org = organizationId, Id = id }, cancellationToken: cancellationToken));
    }

    public async Task SaveItemsAsync(int organizationId, long id, IReadOnlyList<ServiceJobItem> items, int userId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var job = await LockJobAsync(connection, transaction, organizationId, id, cancellationToken);
        RequireOpen(job);
        if (IsBilled(job))
            throw new BusinessRuleException(job.InvoiceStatus == DocumentStatus.Draft
                ? $"The job is billed on draft invoice {job.InvoiceNo}. Delete that draft to change the items."
                : $"The job is billed on invoice {job.InvoiceNo}. Cancel that invoice to change the items.");
        await OrganizationGuard.EnsureAsync(connection, transaction, organizationId, "product", items.Select(i => (int?)i.ProductId), cancellationToken);

        await connection.ExecuteAsync(new CommandDefinition("DELETE FROM public.service_job_item WHERE service_job_id = @Id",
            new { Id = id }, transaction, cancellationToken: cancellationToken));
        if (items.Count > 0)
            await connection.ExecuteAsync(new CommandDefinition($"""
                INSERT INTO public.service_job_item (
                    service_job_id, line_no, product_id, description, unit_id, quantity, batch_id, serial_number_id,
                    unit_price, discount_amount, is_warranty_covered, remarks, created_at, created_by)
                VALUES (
                    @JobId, @LineNo, @ProductId, @Description, @UnitId, @Quantity, @BatchId, @SerialNumberId,
                    @UnitPrice, @DiscountAmount, @IsWarrantyCovered, @Remarks, {SecurityDb.NowUtc}, @UserId)
                """, items.Select(item => new
                {
                    JobId = id,
                    item.LineNo,
                    item.ProductId,
                    item.Description,
                    item.UnitId,
                    item.Quantity,
                    item.BatchId,
                    item.SerialNumberId,
                    item.UnitPrice,
                    item.DiscountAmount,
                    item.IsWarrantyCovered,
                    item.Remarks,
                    UserId = userId
                }), transaction, cancellationToken: cancellationToken));
        await connection.ExecuteAsync(new CommandDefinition(
            $"UPDATE public.service_job SET updated_at = {SecurityDb.NowUtc}, updated_by = @UserId WHERE service_job_id = @Id",
            new { Id = id, UserId = userId }, transaction, cancellationToken: cancellationToken));
        await transaction.CommitAsync(cancellationToken);
    }

    // =====================================================================
    // The job's progress
    // =====================================================================

    public async Task ChangeStatusAsync(int organizationId, long id, ServiceJobStatusRequest request, int userId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var job = await LockJobAsync(connection, transaction, organizationId, id, cancellationToken);
        RequireOpen(job);

        var resolution = request.Resolution ?? job.Resolution;
        if (request.Status == ServiceJobStatus.Ready && resolution is null)
            throw new BusinessRuleException("Say how the job ended (repaired, replaced, no fault found, ...) before marking it ready.");
        if (request.Status == job.Status && request.Note is null && request.Resolution is null && request.ResolutionNote is null)
            throw new BusinessRuleException($"The job is already {request.Status}.");

        await connection.ExecuteAsync(new CommandDefinition($"""
            UPDATE public.service_job
            SET status = @Status, resolution = @Resolution,
                resolution_note = coalesce(@ResolutionNote, resolution_note),
                completed_at = CASE WHEN @Status = 'Ready' THEN coalesce(completed_at, {SecurityDb.NowUtc}) END,
                approval_status = CASE WHEN @Status = 'AwaitingApproval' AND approval_status <> 'Approved' THEN 'Pending'
                                       ELSE approval_status END,
                updated_at = {SecurityDb.NowUtc}, updated_by = @UserId
            WHERE service_job_id = @Id
            """, new { Id = id, request.Status, Resolution = resolution, request.ResolutionNote, UserId = userId },
            transaction, cancellationToken: cancellationToken));
        await AddHistoryAsync(connection, transaction, id, job.Status, request.Status, request.Note, request.CustomerNotified, userId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task SetApprovalAsync(int organizationId, long id, ServiceJobApprovalRequest request, int userId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var job = await LockJobAsync(connection, transaction, organizationId, id, cancellationToken);
        RequireOpen(job);

        var estimate = request.EstimatedAmount ?? job.EstimatedAmount;
        // Approved, the repair goes ahead. Declined, the status stays: the technician marks it ready (CustomerDeclined).
        var status = job.Status == ServiceJobStatus.AwaitingApproval && request.Approved ? ServiceJobStatus.InRepair : job.Status;
        await connection.ExecuteAsync(new CommandDefinition($"""
            UPDATE public.service_job
            SET approval_status = @Approval, approved_at = CASE WHEN @Approved THEN {SecurityDb.NowUtc} END,
                estimated_amount = @Estimate, status = @Status, updated_at = {SecurityDb.NowUtc}, updated_by = @UserId
            WHERE service_job_id = @Id
            """, new
            {
                Id = id,
                request.Approved,
                Approval = request.Approved ? ServiceApprovalStatus.Approved : ServiceApprovalStatus.Rejected,
                Estimate = estimate,
                Status = status,
                UserId = userId
            }, transaction, cancellationToken: cancellationToken));

        var note = (request.Approved
                       ? $"Customer approved the estimate of {estimate:0.##}."
                       : $"Customer declined the estimate of {estimate:0.##}.")
                   + (request.Note is null ? "" : " " + request.Note);
        await AddHistoryAsync(connection, transaction, id, job.Status, status, note, false, userId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task DeliverAsync(int organizationId, long id, ServiceJobDeliverRequest request, int userId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var job = await LockJobAsync(connection, transaction, organizationId, id, cancellationToken);
        if (job.Status != ServiceJobStatus.Ready)
            throw new BusinessRuleException(job.Status == ServiceJobStatus.Delivered
                ? "The device has already been delivered."
                : "Mark the job ready (with how it ended) before delivering the device.");

        // Parts must leave stock, and charges be billed, through a completed invoice.
        var needsInvoice = await connection.QuerySingleAsync<bool>(new CommandDefinition("""
            SELECT EXISTS (SELECT 1 FROM public.service_job_item x JOIN public.product p ON p.product_id = x.product_id
                           WHERE x.service_job_id = @Id AND (x.line_total > 0 OR p.product_type = 'Stock'))
            """, new { Id = id }, transaction, cancellationToken: cancellationToken));
        if (needsInvoice && job.InvoiceStatus != DocumentStatus.Posted)
            throw new BusinessRuleException(job.InvoiceStatus == DocumentStatus.Draft
                ? $"Complete invoice {job.InvoiceNo} before delivering, so the parts leave stock and the bill is recorded."
                : "Bill the job first: its parts and charges go on a sales invoice.");

        await connection.ExecuteAsync(new CommandDefinition($"""
            UPDATE public.service_job
            SET status = 'Delivered', delivered_at = {SecurityDb.NowUtc}, delivered_by = @UserId, delivered_to_name = @To,
                updated_at = {SecurityDb.NowUtc}, updated_by = @UserId
            WHERE service_job_id = @Id
            """, new { Id = id, To = request.DeliveredToName, UserId = userId }, transaction, cancellationToken: cancellationToken));
        var note = request.DeliveredToName is null ? request.Note : $"Collected by {request.DeliveredToName}." + (request.Note is null ? "" : " " + request.Note);
        await AddHistoryAsync(connection, transaction, id, job.Status, ServiceJobStatus.Delivered, note, request.CustomerNotified, userId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task CancelAsync(int organizationId, long id, string reason, int userId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var job = await LockJobAsync(connection, transaction, organizationId, id, cancellationToken);
        RequireOpen(job);
        if (IsBilled(job))
            throw new BusinessRuleException(job.InvoiceStatus == DocumentStatus.Draft
                ? $"Delete draft invoice {job.InvoiceNo} before cancelling the job."
                : $"Cancel invoice {job.InvoiceNo} before cancelling the job.");

        await connection.ExecuteAsync(new CommandDefinition($"""
            UPDATE public.service_job
            SET status = 'Cancelled', cancel_reason = @Reason, updated_at = {SecurityDb.NowUtc}, updated_by = @UserId
            WHERE service_job_id = @Id
            """, new { Id = id, Reason = reason, UserId = userId }, transaction, cancellationToken: cancellationToken));
        await AddHistoryAsync(connection, transaction, id, job.Status, ServiceJobStatus.Cancelled, reason, false, userId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<bool> LinkInvoiceAsync(int organizationId, long id, long invoiceId, int userId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var job = await LockJobAsync(connection, transaction, organizationId, id, cancellationToken);
        if (!ServiceJobStatus.Open.Contains(job.Status) || IsBilled(job))
            return false;

        await connection.ExecuteAsync(new CommandDefinition($"""
            UPDATE public.service_job SET sales_invoice_id = @InvoiceId, updated_at = {SecurityDb.NowUtc}, updated_by = @UserId
            WHERE service_job_id = @Id
            """, new { Id = id, InvoiceId = invoiceId, UserId = userId }, transaction, cancellationToken: cancellationToken));
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    // =====================================================================
    // Supplier claims
    // =====================================================================

    private const string ClaimFrom = """
        FROM public.service_supplier_claim sc
        JOIN public.supplier sp ON sp.supplier_id = sc.supplier_id
        JOIN public.product p ON p.product_id = sc.product_id
        LEFT JOIN public.service_job j ON j.service_job_id = sc.service_job_id
        """;

    private const string ClaimSelect = $"""
        SELECT sc.service_supplier_claim_id AS Id, sc.object_id AS ObjectId, sc.claim_no AS ClaimNo,
               sc.service_job_id AS ServiceJobId, j.job_no AS JobNo, sc.supplier_id AS SupplierId,
               sp.supplier_code AS SupplierCode, sp.supplier_name AS SupplierName, sc.product_id AS ProductId,
               p.product_code AS ProductCode, p.product_name AS ProductName, sc.serial_number_id AS SerialNumberId,
               sc.serial_no AS SerialNo, sc.problem_description AS ProblemDescription, sc.sent_date AS SentDate,
               sc.supplier_reference_no AS SupplierReferenceNo, sc.expected_return_date AS ExpectedReturnDate,
               sc.status AS Status, sc.returned_date AS ReturnedDate, sc.outcome AS Outcome,
               sc.replacement_serial_number_id AS ReplacementSerialNumberId, sc.replacement_serial_no AS ReplacementSerialNo,
               sc.service_charge_amount AS ServiceChargeAmount, sc.remarks AS Remarks,
               (coalesce(sc.returned_date, (now() AT TIME ZONE 'utc')::date) - sc.sent_date) AS DaysOut,
               sc.created_at AT TIME ZONE 'utc' AS CreatedAt, sc.updated_at AT TIME ZONE 'utc' AS UpdatedAt
        {ClaimFrom}
        """;

    private const string ClaimFilter = """
        WHERE sc.organization_id = @Org
          AND (CAST(@Pattern AS text) IS NULL OR sc.claim_no ILIKE @Pattern OR sp.supplier_name ILIKE @Pattern
               OR p.product_name ILIKE @Pattern OR p.product_code ILIKE @Pattern OR sc.serial_no ILIKE @Pattern
               OR sc.supplier_reference_no ILIKE @Pattern OR j.job_no ILIKE @Pattern)
          AND (CAST(@Status AS text) IS NULL OR sc.status = @Status)
          AND (CAST(@SupplierId AS int) IS NULL OR sc.supplier_id = @SupplierId)
          AND (CAST(@ServiceJobId AS bigint) IS NULL OR sc.service_job_id = @ServiceJobId)
          AND (CAST(@From AS date) IS NULL OR sc.sent_date >= @From)
          AND (CAST(@To AS date) IS NULL OR sc.sent_date <= @To)
        """;

    public async Task<PagedResult<SupplierClaim>> GetClaimsAsync(int organizationId, SupplierClaimQuery query, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        return await RepositoryHelpers.PagedAsync<SupplierClaim>(connection,
            $"SELECT count(*) {ClaimFrom} {ClaimFilter}",
            $"{ClaimSelect} {ClaimFilter} ORDER BY sc.sent_date DESC, sc.service_supplier_claim_id DESC",
            new
            {
                Org = organizationId,
                Pattern = RepositoryHelpers.Pattern(query.Search),
                query.Status,
                query.SupplierId,
                query.ServiceJobId,
                query.From,
                query.To
            }, query.Limit, query.Offset, cancellationToken);
    }

    public async Task<SupplierClaim?> GetClaimAsync(int organizationId, long id, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<SupplierClaim>(new CommandDefinition(
            $"{ClaimSelect} WHERE sc.organization_id = @Org AND sc.service_supplier_claim_id = @Id",
            new { Org = organizationId, Id = id }, cancellationToken: cancellationToken));
    }

    private static object ClaimParams(int organizationId, SupplierClaim claim, int userId) => new
    {
        Org = organizationId,
        claim.Id,
        claim.ServiceJobId,
        claim.SupplierId,
        claim.ProductId,
        claim.SerialNumberId,
        claim.SerialNo,
        claim.ProblemDescription,
        claim.SentDate,
        claim.SupplierReferenceNo,
        claim.ExpectedReturnDate,
        claim.Remarks,
        UserId = userId
    };

    public async Task<long> CreateClaimAsync(int organizationId, SupplierClaim claim, int userId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await OrganizationGuard.EnsureAsync(connection, transaction, organizationId, "supplier", [claim.SupplierId], cancellationToken);
        await OrganizationGuard.EnsureAsync(connection, transaction, organizationId, "product", [claim.ProductId], cancellationToken);
        JobState? job = claim.ServiceJobId is long jobId
            ? await LockJobAsync(connection, transaction, organizationId, jobId, cancellationToken)
            : null;

        var parameters = new DynamicParameters(ClaimParams(organizationId, claim, userId));
        var number = await DocumentNumbers.NextAsync(connection, transaction, organizationId, DocumentType.SupplierClaim, cancellationToken);
        parameters.Add("Number", number);
        var id = await connection.QuerySingleAsync<long>(new CommandDefinition($"""
            INSERT INTO public.service_supplier_claim (
                organization_id, claim_no, service_job_id, supplier_id, product_id, serial_number_id, serial_no,
                problem_description, sent_date, supplier_reference_no, expected_return_date, status, remarks, created_at, created_by)
            VALUES (
                @Org, @Number, @ServiceJobId, @SupplierId, @ProductId, @SerialNumberId, @SerialNo,
                @ProblemDescription, @SentDate, @SupplierReferenceNo, @ExpectedReturnDate, 'Sent', @Remarks, {SecurityDb.NowUtc}, @UserId)
            RETURNING service_supplier_claim_id
            """, parameters, transaction, cancellationToken: cancellationToken));

        if (job is not null && ServiceJobStatus.Open.Contains(job.Status) && job.Status != ServiceJobStatus.AtSupplier)
        {
            await connection.ExecuteAsync(new CommandDefinition($"""
                UPDATE public.service_job SET status = 'AtSupplier', updated_at = {SecurityDb.NowUtc}, updated_by = @UserId
                WHERE service_job_id = @Id
                """, new { job.Id, UserId = userId }, transaction, cancellationToken: cancellationToken));
            await AddHistoryAsync(connection, transaction, job.Id, job.Status, ServiceJobStatus.AtSupplier,
                $"Sent to the supplier on claim {number}.", false, userId, cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
        return id;
    }

    public async Task<bool> UpdateClaimAsync(int organizationId, long id, SupplierClaim claim, int userId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var status = await connection.QuerySingleOrDefaultAsync<string>(new CommandDefinition(
            "SELECT status FROM public.service_supplier_claim WHERE organization_id = @Org AND service_supplier_claim_id = @Id FOR UPDATE",
            new { Org = organizationId, Id = id }, transaction, cancellationToken: cancellationToken));
        if (status is null)
            return false;
        if (status != SupplierClaimStatus.Sent)
            throw new BusinessRuleException($"The claim is {status.ToLowerInvariant()}; it can't be changed any more.");
        await OrganizationGuard.EnsureAsync(connection, transaction, organizationId, "supplier", [claim.SupplierId], cancellationToken);
        await OrganizationGuard.EnsureAsync(connection, transaction, organizationId, "product", [claim.ProductId], cancellationToken);

        claim.Id = id;
        await connection.ExecuteAsync(new CommandDefinition($"""
            UPDATE public.service_supplier_claim
            SET supplier_id = @SupplierId, product_id = @ProductId, serial_number_id = @SerialNumberId, serial_no = @SerialNo,
                problem_description = @ProblemDescription, sent_date = @SentDate, supplier_reference_no = @SupplierReferenceNo,
                expected_return_date = @ExpectedReturnDate, remarks = @Remarks, updated_at = {SecurityDb.NowUtc}, updated_by = @UserId
            WHERE service_supplier_claim_id = @Id
            """, ClaimParams(organizationId, claim, userId), transaction, cancellationToken: cancellationToken));
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    private sealed class ClaimState
    {
        public string ClaimNo { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public long? ServiceJobId { get; set; }
    }

    private static async Task<ClaimState> LockClaimAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, int organizationId, long id, CancellationToken cancellationToken)
    {
        var claim = await connection.QuerySingleOrDefaultAsync<ClaimState>(new CommandDefinition("""
            SELECT claim_no AS ClaimNo, status AS Status, service_job_id AS ServiceJobId
            FROM public.service_supplier_claim WHERE organization_id = @Org AND service_supplier_claim_id = @Id
            FOR UPDATE
            """, new { Org = organizationId, Id = id }, transaction, cancellationToken: cancellationToken))
            ?? throw new BusinessRuleException("The supplier claim doesn't exist.");
        if (claim.Status != SupplierClaimStatus.Sent)
            throw new BusinessRuleException($"Claim {claim.ClaimNo} is already {claim.Status.ToLowerInvariant()}.");
        return claim;
    }

    /// <summary>A job waiting at the supplier goes back to the bench once no other claim of it is still out.</summary>
    private static async Task BackFromSupplierAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, int organizationId, long? jobId, string note, int userId,
        CancellationToken cancellationToken)
    {
        if (jobId is not long id)
            return;
        var job = await LockJobAsync(connection, transaction, organizationId, id, cancellationToken);
        var stillOut = await connection.QuerySingleAsync<bool>(new CommandDefinition(
            "SELECT EXISTS (SELECT 1 FROM public.service_supplier_claim WHERE service_job_id = @Id AND status = 'Sent')",
            new { Id = id }, transaction, cancellationToken: cancellationToken));
        if (job.Status != ServiceJobStatus.AtSupplier || stillOut)
        {
            await AddHistoryAsync(connection, transaction, id, job.Status, job.Status, note, false, userId, cancellationToken);
            return;
        }
        await connection.ExecuteAsync(new CommandDefinition($"""
            UPDATE public.service_job SET status = 'InRepair', updated_at = {SecurityDb.NowUtc}, updated_by = @UserId
            WHERE service_job_id = @Id
            """, new { Id = id, UserId = userId }, transaction, cancellationToken: cancellationToken));
        await AddHistoryAsync(connection, transaction, id, job.Status, ServiceJobStatus.InRepair, note, false, userId, cancellationToken);
    }

    public async Task ReturnClaimAsync(int organizationId, long id, SupplierClaim result, int userId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var claim = await LockClaimAsync(connection, transaction, organizationId, id, cancellationToken);

        await connection.ExecuteAsync(new CommandDefinition($"""
            UPDATE public.service_supplier_claim
            SET status = 'Returned', returned_date = @ReturnedDate, outcome = @Outcome,
                replacement_serial_number_id = @ReplacementSerialNumberId, replacement_serial_no = @ReplacementSerialNo,
                service_charge_amount = @ServiceChargeAmount, remarks = coalesce(@Remarks, remarks),
                updated_at = {SecurityDb.NowUtc}, updated_by = @UserId
            WHERE service_supplier_claim_id = @Id
            """, new
            {
                Id = id,
                result.ReturnedDate,
                result.Outcome,
                result.ReplacementSerialNumberId,
                result.ReplacementSerialNo,
                result.ServiceChargeAmount,
                result.Remarks,
                UserId = userId
            }, transaction, cancellationToken: cancellationToken));

        var note = $"Back from the supplier on claim {claim.ClaimNo}: {result.Outcome}"
                   + (result.ReplacementSerialNo is null ? "." : $", replacement serial {result.ReplacementSerialNo}.");
        await BackFromSupplierAsync(connection, transaction, organizationId, claim.ServiceJobId, note, userId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task CancelClaimAsync(int organizationId, long id, int userId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var claim = await LockClaimAsync(connection, transaction, organizationId, id, cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition($"""
            UPDATE public.service_supplier_claim SET status = 'Cancelled', updated_at = {SecurityDb.NowUtc}, updated_by = @UserId
            WHERE service_supplier_claim_id = @Id
            """, new { Id = id, UserId = userId }, transaction, cancellationToken: cancellationToken));
        await BackFromSupplierAsync(connection, transaction, organizationId, claim.ServiceJobId,
            $"Supplier claim {claim.ClaimNo} was cancelled.", userId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
