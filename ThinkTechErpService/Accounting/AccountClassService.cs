using ThinkTechErpDomain.Models.Accounting;
using ThinkTechErpDomain.Models.Common;
using ThinkTechErpDomain.Models.Security;
using ThinkTechErpRepository.Accounting;
using ThinkTechErpService.Common;

namespace ThinkTechErpService.Accounting;

/// <summary>The chart of accounts tree on the accounting.acc_* tables (one chart for every organization).</summary>
public interface IAccountClassService
{
    Task<IReadOnlyList<AccountClassNode>> GetTreeAsync(CancellationToken cancellationToken = default);
    /// <summary>Adds a main, map or sub class under its parent with the next code.</summary>
    Task<AccountClassNode> CreateAsync(AccountClassCreateRequest request, CancellationToken cancellationToken = default);
    /// <summary>Renames a main, map or sub class; codes never change.</summary>
    Task<AccountClassNode?> RenameAsync(string kind, int id, AccountClassRenameRequest request, CancellationToken cancellationToken = default);
    /// <summary>Deletes a main, map or sub class that has nothing under it.</summary>
    Task<DeleteOutcome> DeleteAsync(string kind, int id, CancellationToken cancellationToken = default);
}

public sealed class AccountClassService(IAccountClassRepository repository) : IAccountClassService
{
    public Task<IReadOnlyList<AccountClassNode>> GetTreeAsync(CancellationToken cancellationToken = default)
        => repository.GetTreeAsync(cancellationToken);

    public async Task<AccountClassNode> CreateAsync(AccountClassCreateRequest request, CancellationToken cancellationToken = default)
    {
        var kind = EditableKind(request.Kind);
        var name = CheckedName(kind, request.Name);
        var parentKind = kind switch
        {
            AccountClassKind.Main => AccountClassKind.Account,
            AccountClassKind.Map => AccountClassKind.Main,
            _ => AccountClassKind.Map
        };
        return await repository.CreateAsync(kind, request.ParentId, name, cancellationToken)
            ?? throw new BusinessRuleException($"The {AccountClassKind.Label(parentKind)} it goes under no longer exists. Refresh the tree.");
    }

    public Task<AccountClassNode?> RenameAsync(string kind, int id, AccountClassRenameRequest request, CancellationToken cancellationToken = default)
    {
        kind = EditableKind(kind);
        return repository.RenameAsync(kind, id, CheckedName(kind, request.Name), cancellationToken);
    }

    public async Task<DeleteOutcome> DeleteAsync(string kind, int id, CancellationToken cancellationToken = default)
    {
        kind = EditableKind(kind);
        if (await repository.HasChildrenAsync(kind, id, cancellationToken))
            throw new BusinessConflictException(
                $"Cannot delete: this {AccountClassKind.Label(kind)} has {AccountClassKind.Label(AccountClassKind.ChildOf(kind)!)}es under it.");
        return await repository.DeleteAsync(kind, id, cancellationToken);
    }

    // Parent and account classes are the fixed top of the chart.
    private static string EditableKind(string? kind)
    {
        var value = Text.Clean(kind)?.ToLowerInvariant();
        return value is not null && AccountClassKind.Editable.Contains(value)
            ? value
            : throw new BusinessRuleException("Only main, map and sub classes can be added, renamed or deleted.");
    }

    private static string CheckedName(string kind, string? name)
    {
        var value = Text.Clean(name) ?? throw new BusinessRuleException("Name is required.");
        var max = AccountClassKind.MaxNameLength(kind);
        return value.Length <= max
            ? value
            : throw new BusinessRuleException($"A {AccountClassKind.Label(kind)} name can be at most {max} characters.");
    }
}
