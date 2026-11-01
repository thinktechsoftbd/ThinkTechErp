using System.ComponentModel.DataAnnotations;

namespace ThinkTechErpDomain.Models.Accounting;

/// <summary>
/// The levels of the classic chart of accounts kept in the accounting.acc_* tables (as in MediDesk):
/// parent class → account class → main class → map class → sub class. One chart, shared by every organization.
/// Parent and account classes are fixed; main, map and sub classes are added, renamed and deleted from the tree.
/// </summary>
public static class AccountClassKind
{
    public const string Parent = "parent";
    public const string Account = "ac";
    public const string Main = "main";
    public const string Map = "map";
    public const string Sub = "sub";

    /// <summary>The kinds that can be added, renamed and deleted.</summary>
    public static readonly IReadOnlySet<string> Editable = new HashSet<string>(StringComparer.Ordinal) { Main, Map, Sub };

    /// <summary>The kind a new child of this kind gets, or null when it can't have children here.</summary>
    public static string? ChildOf(string kind) => kind switch
    {
        Account => Main,
        Main => Map,
        Map => Sub,
        _ => null
    };

    public static string Label(string kind) => kind switch
    {
        Parent => "parent class",
        Account => "account class",
        Main => "main class",
        Map => "map class",
        Sub => "sub class",
        _ => "account"
    };

    /// <summary>The longest name each table holds: main_name and sub_name varchar(60), map_name varchar(100).</summary>
    public static int MaxNameLength(string kind) => kind == Map ? 100 : 60;
}

/// <summary>A node of the tree; Children are the level below, in display order.</summary>
public sealed class AccountClassNode
{
    /// <summary>parent, ac, main, map or sub.</summary>
    public string Kind { get; set; } = string.Empty;
    public int Id { get; set; }
    public string? Code { get; set; }
    public string? Name { get; set; }
    public List<AccountClassNode> Children { get; set; } = [];
}

public sealed class AccountClassCreateRequest
{
    /// <summary>The kind to add: main (under an account class), map (under a main class) or sub (under a map class).</summary>
    [Required, StringLength(10)]
    public string Kind { get; set; } = string.Empty;

    /// <summary>The account, main or map class the new one goes under.</summary>
    [Range(1, int.MaxValue)]
    public int ParentId { get; set; }

    [Required, StringLength(100, MinimumLength = 1)]
    public string Name { get; set; } = string.Empty;
}

public sealed class AccountClassRenameRequest
{
    [Required, StringLength(100, MinimumLength = 1)]
    public string Name { get; set; } = string.Empty;
}
