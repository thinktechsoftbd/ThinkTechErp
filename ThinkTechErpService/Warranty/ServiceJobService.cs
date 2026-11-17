using ThinkTechErpDomain.Models.Common;
using ThinkTechErpDomain.Models.Sales;
using ThinkTechErpDomain.Models.Security;
using ThinkTechErpDomain.Models.Warranty;
using ThinkTechErpRepository.Common;
using ThinkTechErpRepository.Inventory;
using ThinkTechErpRepository.Warranty;
using ThinkTechErpService.Common;
using ThinkTechErpService.Sales;

namespace ThinkTechErpService.Warranty;

public interface IServiceJobService
{
    Task<PagedResult<ServiceJob>> GetJobsAsync(int organizationId, ServiceJobQuery query, CancellationToken cancellationToken = default);
    Task<ServiceJob?> GetJobAsync(int organizationId, long id, CancellationToken cancellationToken = default);
    Task<ServiceJob> CreateJobAsync(int organizationId, ServiceJobUpsertRequest request, int userId, CancellationToken cancellationToken = default);
    Task<ServiceJob?> UpdateJobAsync(int organizationId, long id, ServiceJobUpsertRequest request, int userId, CancellationToken cancellationToken = default);
    Task<DeleteOutcome> DeleteJobAsync(int organizationId, long id, CancellationToken cancellationToken = default);
    Task<ServiceJob?> SaveItemsAsync(int organizationId, long id, ServiceJobItemsRequest request, int userId, CancellationToken cancellationToken = default);
    Task<ServiceJob?> ChangeStatusAsync(int organizationId, long id, ServiceJobStatusRequest request, int userId, CancellationToken cancellationToken = default);
    Task<ServiceJob?> SetApprovalAsync(int organizationId, long id, ServiceJobApprovalRequest request, int userId, CancellationToken cancellationToken = default);
    Task<ServiceJob?> DeliverAsync(int organizationId, long id, ServiceJobDeliverRequest request, int userId, CancellationToken cancellationToken = default);
    Task<ServiceJob?> CancelAsync(int organizationId, long id, string reason, int userId, CancellationToken cancellationToken = default);
    /// <summary>Bills the job: a sales invoice made from its items (posted too when asked).</summary>
    Task<ServiceJob?> CreateInvoiceAsync(int organizationId, long id, ServiceJobInvoiceRequest request, int userId, CancellationToken cancellationToken = default);

    Task<PagedResult<SupplierClaim>> GetClaimsAsync(int organizationId, SupplierClaimQuery query, CancellationToken cancellationToken = default);
    Task<SupplierClaim?> GetClaimAsync(int organizationId, long id, CancellationToken cancellationToken = default);
    Task<SupplierClaim> CreateClaimAsync(int organizationId, SupplierClaimUpsertRequest request, int userId, CancellationToken cancellationToken = default);
    Task<SupplierClaim?> UpdateClaimAsync(int organizationId, long id, SupplierClaimUpsertRequest request, int userId, CancellationToken cancellationToken = default);
    Task<SupplierClaim?> ReturnClaimAsync(int organizationId, long id, SupplierClaimReturnRequest request, int userId, CancellationToken cancellationToken = default);
    Task<SupplierClaim?> CancelClaimAsync(int organizationId, long id, int userId, CancellationToken cancellationToken = default);
}

public sealed class ServiceJobService(
    IServiceJobRepository jobs,
    IWarrantyCardRepository cards,
    IWarrantySetupRepository setup,
    IProductRepository products,
    IOwnershipRepository ownership,
    ISalesDocumentService sales) : IServiceJobService
{
    private static DateOnly TodayUtc => DateOnly.FromDateTime(DateTime.UtcNow);

    /// <summary>Runs a change on a job that must exist, then reads it back (null: no such job).</summary>
    private async Task<ServiceJob?> ActAsync(int organizationId, long id, Func<Task> action, CancellationToken cancellationToken)
    {
        if (await jobs.GetJobAsync(organizationId, id, cancellationToken) is null)
            return null;
        await BusinessDbErrors.ExecuteAsync(action);
        return await jobs.GetJobAsync(organizationId, id, cancellationToken);
    }

    // =====================================================================
    // Jobs
    // =====================================================================

    public Task<PagedResult<ServiceJob>> GetJobsAsync(int organizationId, ServiceJobQuery query, CancellationToken cancellationToken = default)
    {
        query.Search = Text.Clean(query.Search);
        query.Status = Text.Clean(query.Status);
        query.JobType = Text.Clean(query.JobType);
        return jobs.GetJobsAsync(organizationId, query, cancellationToken);
    }

    public Task<ServiceJob?> GetJobAsync(int organizationId, long id, CancellationToken cancellationToken = default)
        => jobs.GetJobAsync(organizationId, id, cancellationToken);

    public async Task<ServiceJob> CreateJobAsync(int organizationId, ServiceJobUpsertRequest request, int userId, CancellationToken cancellationToken = default)
    {
        var job = await BuildJobAsync(organizationId, request, null, cancellationToken);
        var id = await BusinessDbErrors.ExecuteAsync(() => jobs.CreateJobAsync(organizationId, job, userId, cancellationToken));
        return (await jobs.GetJobAsync(organizationId, id, cancellationToken))!;
    }

    public async Task<ServiceJob?> UpdateJobAsync(int organizationId, long id, ServiceJobUpsertRequest request, int userId, CancellationToken cancellationToken = default)
    {
        var existing = await jobs.GetJobAsync(organizationId, id, cancellationToken);
        if (existing is null)
            return null;
        var job = await BuildJobAsync(organizationId, request, existing, cancellationToken);
        await BusinessDbErrors.ExecuteAsync(() => jobs.UpdateJobAsync(organizationId, id, job, userId, cancellationToken));
        return await jobs.GetJobAsync(organizationId, id, cancellationToken);
    }

    public Task<DeleteOutcome> DeleteJobAsync(int organizationId, long id, CancellationToken cancellationToken = default)
        => jobs.DeleteJobAsync(organizationId, id, cancellationToken);

    private async Task<ServiceJob> BuildJobAsync(
        int organizationId, ServiceJobUpsertRequest request, ServiceJob? existing, CancellationToken cancellationToken)
    {
        if (!ServiceJobType.All.Contains(request.JobType))
            throw new BusinessRuleException("Choose Warranty or Paid.");
        if (!ServiceLocation.All.Contains(request.ServiceLocation))
            throw new BusinessRuleException("Choose in the shop (InShop) or at the customer's place (OnSite).");
        if (!ServicePriority.All.Contains(request.Priority))
            throw new BusinessRuleException("Choose Normal or Urgent.");
        if (!await setup.DeviceTypeExistsAsync(request.DeviceTypeLookupDataId, cancellationToken))
            throw new BusinessRuleException("Choose a device type from the list.");
        if (request.TechnicianUserId is int technician && !await setup.UserExistsAsync(technician, cancellationToken))
            throw new BusinessRuleException("The technician doesn't exist.");

        var problem = Text.Clean(request.ProblemDescription)
            ?? throw new BusinessRuleException("Describe the problem the customer reports.");

        // The card: judged when the device comes in (or when another card is chosen), not again on every edit.
        WarrantyCard? card = null;
        if (request.WarrantyRegistrationId is long cardId)
        {
            card = await cards.GetCardAsync(organizationId, cardId, cancellationToken)
                ?? throw new BusinessRuleException("The warranty card doesn't exist.");
            var judge = existing is null || existing.WarrantyRegistrationId != cardId || existing.JobType != request.JobType;
            if (judge)
                CheckCover(card, request);
        }
        else if (request.JobType == ServiceJobType.Warranty)
            throw new BusinessRuleException("Choose the warranty card the claim is made under (find it with the warranty check).");

        // The customer: as given, else the card's.
        var customerId = request.CustomerId;
        var customerName = Text.Clean(request.CustomerName);
        var customerMobile = Text.Clean(request.CustomerMobile);
        if (customerId is null && customerName is null && customerMobile is null && card is not null)
        {
            customerId = card.CustomerId;
            customerName = card.CustomerId is null ? card.CustomerName : null;
            customerMobile = card.CustomerMobile;
        }
        if (customerId is null && customerMobile is null)
            throw new BusinessRuleException("Pick the customer, or enter the walk-in customer's mobile number.");

        // The device: as given, else the card's unit; a serial on file names its product.
        var productId = request.ProductId ?? card?.ProductId;
        var serialNo = Text.Clean(request.SerialNo) ?? (request.ProductId is null ? card?.SerialNo : null);
        long? serialNumberId = null;
        if (card is not null && productId == card.ProductId && serialNo is not null
            && string.Equals(serialNo, card.SerialNo, StringComparison.OrdinalIgnoreCase))
            serialNumberId = card.SerialNumberId;
        else if (serialNo is not null)
        {
            var matches = await jobs.FindSerialNumbersAsync(organizationId, productId, serialNo, cancellationToken);
            if (matches.Count == 1)
            {
                serialNumberId = matches[0].SerialNumberId;
                productId ??= matches[0].ProductId;
            }
        }

        var address = Text.Clean(request.ServiceAddress);
        if (request.ServiceLocation == ServiceLocation.OnSite && address is null)
            throw new BusinessRuleException("On-site service needs the address.");

        DateTime? receivedAt = null;
        if (existing is null && request.ReceivedAt is { } received)
        {
            var utc = received.Kind == DateTimeKind.Unspecified ? received : received.ToUniversalTime();
            if (utc > DateTime.UtcNow.AddMinutes(10))
                throw new BusinessRuleException("The device can't be received in the future.");
            receivedAt = DateTime.SpecifyKind(utc, DateTimeKind.Unspecified);
        }

        return new ServiceJob
        {
            ReceivedAt = receivedAt ?? default,
            JobType = request.JobType,
            ServiceLocation = request.ServiceLocation,
            Priority = request.Priority,
            CustomerId = customerId,
            CustomerName = customerName,
            CustomerMobile = customerMobile,
            ServiceAddress = address,
            DeviceTypeLookupDataId = request.DeviceTypeLookupDataId,
            ProductId = productId,
            SerialNumberId = serialNumberId,
            BrandName = Text.Clean(request.BrandName),
            ModelName = Text.Clean(request.ModelName),
            SerialNo = serialNo,
            AccessoriesReceived = Text.Clean(request.AccessoriesReceived),
            PhysicalCondition = Text.Clean(request.PhysicalCondition),
            ProblemDescription = problem,
            WarrantyRegistrationId = card?.Id,
            WarrantyVoidReason = request.JobType == ServiceJobType.Paid ? Text.Clean(request.WarrantyVoidReason) : null,
            TechnicianUserId = request.TechnicianUserId,
            Diagnosis = Text.Clean(request.Diagnosis),
            EstimatedAmount = TaxCalculator.Round(request.EstimatedAmount),
            PromisedDate = request.PromisedDate,
            Remarks = Text.Clean(request.Remarks)
        };
    }

    /// <summary>A warranty claim needs a card in force; a paid job on a covered unit says why the warranty doesn't apply.</summary>
    private static void CheckCover(WarrantyCard card, ServiceJobUpsertRequest request)
    {
        var covered = card.Coverage is WarrantyCoverage.InWarranty or WarrantyCoverage.InReplacement;
        if (request.JobType == ServiceJobType.Warranty && !covered)
            throw new BusinessRuleException(card.Coverage switch
            {
                WarrantyCoverage.Expired => $"The warranty on card {card.WarrantyNo} ended on {card.EndDate:dd MMM yyyy}. Open a paid job instead.",
                WarrantyCoverage.Replaced => $"Card {card.WarrantyNo} was replaced: use the replacement unit's card ({card.ReplacedByWarrantyNo}).",
                _ => $"Card {card.WarrantyNo} is void ({card.VoidReason}). Open a paid job instead."
            });
        if (request.JobType == ServiceJobType.Paid && covered && Text.Clean(request.WarrantyVoidReason) is null)
            throw new BusinessRuleException(
                $"Card {card.WarrantyNo} is still under warranty. Say why this job is charged (e.g. physical or liquid damage), or open it as a warranty job.");
    }

    public async Task<ServiceJob?> SaveItemsAsync(int organizationId, long id, ServiceJobItemsRequest request, int userId, CancellationToken cancellationToken = default)
    {
        var job = await jobs.GetJobAsync(organizationId, id, cancellationToken);
        if (job is null)
            return null;

        var productMap = await products.GetForLinesAsync(organizationId, request.Items.Select(i => i.ProductId), cancellationToken);
        var items = new List<ServiceJobItem>();
        var serials = new HashSet<long>();
        for (var index = 0; index < request.Items.Count; index++)
        {
            var line = request.Items[index];
            var label = $"Item {index + 1}";
            if (!productMap.TryGetValue(line.ProductId, out var product))
                throw new BusinessRuleException($"{label}: the product doesn't exist.");
            label = $"{label} ({product.Name})";
            var unit = product.Units.FirstOrDefault(u => u.UnitId == line.UnitId)
                ?? throw new BusinessRuleException($"{label}: the unit isn't one of the product's units.");
            if (line.Quantity <= 0)
                throw new BusinessRuleException($"{label}: enter a quantity.");

            if (line.IsWarrantyCovered && job.JobType != ServiceJobType.Warranty)
                throw new BusinessRuleException($"{label}: only a warranty job has items covered by the warranty. Charge 0 instead.");
            var price = line.IsWarrantyCovered ? 0 : line.UnitPrice;
            var discount = line.IsWarrantyCovered ? 0 : TaxCalculator.Round(line.DiscountAmount);
            if (discount > TaxCalculator.Round(line.Quantity * price))
                throw new BusinessRuleException($"{label}: the discount is more than the item.");

            long? serialNumberId = null;
            var serialNo = Text.Clean(line.SerialNo);
            if (product.TrackingType == "Serial" && product.ProductType == "Stock")
            {
                if (serialNo is null)
                    throw new BusinessRuleException($"{label}: enter the serial number of the part fitted.");
                if (line.Quantity * unit.ConversionFactor != 1)
                    throw new BusinessRuleException($"{label}: a serial-numbered part is one item per line.");
                var matches = await jobs.FindSerialNumbersAsync(organizationId, product.Id, serialNo, cancellationToken);
                if (matches.Count != 1)
                    throw new BusinessRuleException($"{label}: serial {serialNo} isn't one of this product's serial numbers.");
                serialNumberId = matches[0].SerialNumberId;
                if (!serials.Add(serialNumberId.Value))
                    throw new BusinessRuleException($"{label}: serial {serialNo} is on the job twice.");
            }
            else if (serialNo is not null)
                throw new BusinessRuleException($"{label}: the product isn't tracked by serial number.");

            if (line.BatchId is not null && product.TrackingType != "Batch")
                throw new BusinessRuleException($"{label}: the product isn't tracked by batch.");

            items.Add(new ServiceJobItem
            {
                LineNo = index + 1,
                ProductId = product.Id,
                Description = Text.Clean(line.Description),
                UnitId = unit.UnitId,
                Quantity = line.Quantity,
                BatchId = line.BatchId,
                SerialNumberId = serialNumberId,
                UnitPrice = price,
                DiscountAmount = discount,
                IsWarrantyCovered = line.IsWarrantyCovered,
                Remarks = Text.Clean(line.Remarks)
            });
        }

        var batches = items.Where(i => i.BatchId is not null).Select(i => (i.BatchId!.Value, i.ProductId)).ToList();
        if (!await ownership.BatchesBelongToAsync(organizationId, batches, cancellationToken))
            throw new BusinessRuleException("A chosen batch isn't one of the item product's batches.");

        await BusinessDbErrors.ExecuteAsync(() => jobs.SaveItemsAsync(organizationId, id, items, userId, cancellationToken));
        return await jobs.GetJobAsync(organizationId, id, cancellationToken);
    }

    public Task<ServiceJob?> ChangeStatusAsync(int organizationId, long id, ServiceJobStatusRequest request, int userId, CancellationToken cancellationToken = default)
    {
        if (request.Status == ServiceJobStatus.Delivered)
            throw new BusinessRuleException("Deliver the device with the deliver action.");
        if (request.Status == ServiceJobStatus.Cancelled)
            throw new BusinessRuleException("Cancel the job with the cancel action, giving the reason.");
        if (!ServiceJobStatus.Open.Contains(request.Status))
            throw new BusinessRuleException("Choose one of the job statuses.");
        request.Resolution = Text.Clean(request.Resolution);
        if (request.Resolution is not null && !ServiceResolution.All.Contains(request.Resolution))
            throw new BusinessRuleException("Choose how the job ended: Repaired, Replaced, NoFaultFound, NotRepairable or CustomerDeclined.");
        request.ResolutionNote = Text.Clean(request.ResolutionNote);
        request.Note = Text.Clean(request.Note);
        return ActAsync(organizationId, id, () => jobs.ChangeStatusAsync(organizationId, id, request, userId, cancellationToken), cancellationToken);
    }

    public Task<ServiceJob?> SetApprovalAsync(int organizationId, long id, ServiceJobApprovalRequest request, int userId, CancellationToken cancellationToken = default)
    {
        request.Note = Text.Clean(request.Note);
        if (request.EstimatedAmount is { } estimate)
            request.EstimatedAmount = TaxCalculator.Round(estimate);
        return ActAsync(organizationId, id, () => jobs.SetApprovalAsync(organizationId, id, request, userId, cancellationToken), cancellationToken);
    }

    public Task<ServiceJob?> DeliverAsync(int organizationId, long id, ServiceJobDeliverRequest request, int userId, CancellationToken cancellationToken = default)
    {
        request.DeliveredToName = Text.Clean(request.DeliveredToName);
        request.Note = Text.Clean(request.Note);
        return ActAsync(organizationId, id, () => jobs.DeliverAsync(organizationId, id, request, userId, cancellationToken), cancellationToken);
    }

    public Task<ServiceJob?> CancelAsync(int organizationId, long id, string reason, int userId, CancellationToken cancellationToken = default)
    {
        var why = Text.Clean(reason) ?? throw new BusinessRuleException("Say why the job is cancelled.");
        return ActAsync(organizationId, id, () => jobs.CancelAsync(organizationId, id, why, userId, cancellationToken), cancellationToken);
    }

    public async Task<ServiceJob?> CreateInvoiceAsync(int organizationId, long id, ServiceJobInvoiceRequest request, int userId, CancellationToken cancellationToken = default)
    {
        var job = await jobs.GetJobAsync(organizationId, id, cancellationToken);
        if (job is null)
            return null;
        if (!ServiceJobStatus.Open.Contains(job.Status))
            throw new BusinessRuleException($"Job {job.JobNo} is {job.Status.ToLowerInvariant()}; it can't be billed any more.");
        if (job.SalesInvoiceId is not null && job.InvoiceStatus != DocumentStatus.Cancelled)
            throw new BusinessRuleException($"The job is already billed on invoice {job.InvoiceNo}.");
        if (job.Items.Count == 0)
            throw new BusinessRuleException("Add the parts and service charges to the job before billing it.");

        var productMap = await products.GetForLinesAsync(organizationId, job.Items.Select(i => i.ProductId), cancellationToken);
        var invoiceRequest = new SalesInvoiceUpsertRequest
        {
            InvoiceDate = request.InvoiceDate ?? TodayUtc,
            SaleType = request.SaleType,
            CustomerId = job.CustomerId,
            CustomerName = job.CustomerId is null ? job.CustomerName : null,
            CustomerMobile = job.CustomerMobile,
            WarehouseId = request.WarehouseId,
            PaidAmount = request.PaidAmount,
            PaymentMethod = request.PaymentMethod,
            PaymentProvider = request.PaymentProvider,
            PaymentReferenceNo = request.PaymentReferenceNo,
            Remarks = Text.Clean(request.Remarks) ?? $"Service job {job.JobNo}",
            Lines = job.Items.Select(item => new SalesLineRequest
            {
                ProductId = item.ProductId,
                UnitId = item.UnitId,
                Quantity = item.Quantity,
                UnitPrice = item.UnitPrice,
                DiscountAmount = item.DiscountAmount,
                // Warranty work is free: no tax on it either.
                TaxGroupId = item.IsWarrantyCovered ? null : productMap.GetValueOrDefault(item.ProductId)?.SaleTaxGroupId,
                Description = item.Description ?? (item.IsWarrantyCovered ? "Covered by warranty" : null),
                BatchId = item.BatchId,
                SerialNos = item.SerialNo is null ? [] : [item.SerialNo],
                Remarks = item.Remarks
            }).ToList()
        };

        var invoice = await sales.CreateInvoiceAsync(organizationId, invoiceRequest, userId, cancellationToken);
        if (!await jobs.LinkInvoiceAsync(organizationId, id, invoice.Id, userId, cancellationToken))
        {
            await sales.DeleteInvoiceAsync(organizationId, invoice.Id, cancellationToken);
            throw new BusinessConflictException("The job was billed or closed by someone else meanwhile. Reload it.");
        }

        if (request.Post)
        {
            try
            {
                await sales.PostInvoiceAsync(organizationId, invoice.Id, userId, cancellationToken);
            }
            catch (BusinessRuleException exception)
            {
                throw new BusinessRuleException(
                    $"Draft invoice {invoice.InvoiceNo} was made for the job, but it couldn't be completed: {exception.Message}", exception);
            }
        }
        return await jobs.GetJobAsync(organizationId, id, cancellationToken);
    }

    // =====================================================================
    // Supplier claims
    // =====================================================================

    public Task<PagedResult<SupplierClaim>> GetClaimsAsync(int organizationId, SupplierClaimQuery query, CancellationToken cancellationToken = default)
    {
        query.Search = Text.Clean(query.Search);
        query.Status = Text.Clean(query.Status);
        return jobs.GetClaimsAsync(organizationId, query, cancellationToken);
    }

    public Task<SupplierClaim?> GetClaimAsync(int organizationId, long id, CancellationToken cancellationToken = default)
        => jobs.GetClaimAsync(organizationId, id, cancellationToken);

    private async Task<SupplierClaim> BuildClaimAsync(int organizationId, SupplierClaimUpsertRequest request, CancellationToken cancellationToken)
    {
        ServiceJob? job = null;
        if (request.ServiceJobId is long jobId)
            job = await jobs.GetJobAsync(organizationId, jobId, cancellationToken)
                ?? throw new BusinessRuleException("The service job doesn't exist.");

        var productId = request.ProductId ?? job?.ProductId
            ?? throw new BusinessRuleException("Choose the product sent to the supplier.");
        var serialNo = Text.Clean(request.SerialNo) ?? (request.ProductId is null || request.ProductId == job?.ProductId ? job?.SerialNo : null);
        long? serialNumberId = null;
        if (serialNo is not null)
        {
            var matches = await jobs.FindSerialNumbersAsync(organizationId, productId, serialNo, cancellationToken);
            if (matches.Count == 1)
                serialNumberId = matches[0].SerialNumberId;
        }

        var sent = request.SentDate ?? TodayUtc;
        if (request.ExpectedReturnDate is { } expected && expected < sent)
            throw new BusinessRuleException("The expected return date can't be before the date the unit is sent.");
        return new SupplierClaim
        {
            ServiceJobId = job?.Id,
            SupplierId = request.SupplierId,
            ProductId = productId,
            SerialNumberId = serialNumberId,
            SerialNo = serialNo,
            ProblemDescription = Text.Clean(request.ProblemDescription) ?? throw new BusinessRuleException("Describe the fault."),
            SentDate = sent,
            SupplierReferenceNo = Text.Clean(request.SupplierReferenceNo),
            ExpectedReturnDate = request.ExpectedReturnDate,
            Remarks = Text.Clean(request.Remarks)
        };
    }

    public async Task<SupplierClaim> CreateClaimAsync(int organizationId, SupplierClaimUpsertRequest request, int userId, CancellationToken cancellationToken = default)
    {
        var claim = await BuildClaimAsync(organizationId, request, cancellationToken);
        var id = await BusinessDbErrors.ExecuteAsync(() => jobs.CreateClaimAsync(organizationId, claim, userId, cancellationToken));
        return (await jobs.GetClaimAsync(organizationId, id, cancellationToken))!;
    }

    public async Task<SupplierClaim?> UpdateClaimAsync(int organizationId, long id, SupplierClaimUpsertRequest request, int userId, CancellationToken cancellationToken = default)
    {
        var existing = await jobs.GetClaimAsync(organizationId, id, cancellationToken);
        if (existing is null)
            return null;
        // The job a claim was made for stays with it.
        request.ServiceJobId = existing.ServiceJobId;
        var claim = await BuildClaimAsync(organizationId, request, cancellationToken);
        await BusinessDbErrors.ExecuteAsync(() => jobs.UpdateClaimAsync(organizationId, id, claim, userId, cancellationToken));
        return await jobs.GetClaimAsync(organizationId, id, cancellationToken);
    }

    public async Task<SupplierClaim?> ReturnClaimAsync(int organizationId, long id, SupplierClaimReturnRequest request, int userId, CancellationToken cancellationToken = default)
    {
        var claim = await jobs.GetClaimAsync(organizationId, id, cancellationToken);
        if (claim is null)
            return null;
        if (!SupplierClaimOutcome.All.Contains(request.Outcome))
            throw new BusinessRuleException("Choose what the supplier did: Repaired, Replaced, Rejected or Credited.");

        var returned = request.ReturnedDate ?? TodayUtc;
        if (returned < claim.SentDate)
            throw new BusinessRuleException("The return date can't be before the date the unit was sent.");
        var replacementSerial = request.Outcome == SupplierClaimOutcome.Replaced ? Text.Clean(request.ReplacementSerialNo) : null;
        long? replacementSerialId = null;
        if (replacementSerial is not null)
        {
            var matches = await jobs.FindSerialNumbersAsync(organizationId, claim.ProductId, replacementSerial, cancellationToken);
            if (matches.Count == 1)
                replacementSerialId = matches[0].SerialNumberId;
        }

        var result = new SupplierClaim
        {
            ReturnedDate = returned,
            Outcome = request.Outcome,
            ReplacementSerialNumberId = replacementSerialId,
            ReplacementSerialNo = replacementSerial,
            ServiceChargeAmount = TaxCalculator.Round(request.ServiceChargeAmount),
            Remarks = Text.Clean(request.Remarks)
        };
        await BusinessDbErrors.ExecuteAsync(() => jobs.ReturnClaimAsync(organizationId, id, result, userId, cancellationToken));
        return await jobs.GetClaimAsync(organizationId, id, cancellationToken);
    }

    public async Task<SupplierClaim?> CancelClaimAsync(int organizationId, long id, int userId, CancellationToken cancellationToken = default)
    {
        if (await jobs.GetClaimAsync(organizationId, id, cancellationToken) is null)
            return null;
        await BusinessDbErrors.ExecuteAsync(() => jobs.CancelClaimAsync(organizationId, id, userId, cancellationToken));
        return await jobs.GetClaimAsync(organizationId, id, cancellationToken);
    }
}
