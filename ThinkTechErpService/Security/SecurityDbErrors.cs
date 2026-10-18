using Npgsql;

namespace ThinkTechErpService.Security;

internal static class SecurityDbErrors
{
    private static readonly Dictionary<string, string> UniqueViolationMessages = new(StringComparer.Ordinal)
    {
        ["User_email_key"] = "A user with this email already exists.",
        ["Role_name_key"] = "A role with this name already exists.",
        ["Permission_code_key"] = "A permission with this code already exists.",
        ["organization_organization_code_key"] = "An organization with this code already exists."
    };

    public static async Task<T> ExecuteAsync<T>(Func<Task<T>> operation)
    {
        try
        {
            return await operation();
        }
        // A primary-key clash on a serial ID means the table's sequence is behind its data: a
        // server problem, not a client conflict, so it stays a logged 500.
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation &&
                                                  exception.ConstraintName?.EndsWith("_pkey", StringComparison.Ordinal) != true)
        {
            throw new SecurityConflictException(
                exception.ConstraintName is not null &&
                UniqueViolationMessages.TryGetValue(exception.ConstraintName, out var message)
                    ? message
                    : "The request conflicts with an existing record.",
                exception);
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.ForeignKeyViolation)
        {
            throw new SecurityValidationException(
                "A referenced user, role, permission or menu does not exist.", exception);
        }
    }
}
