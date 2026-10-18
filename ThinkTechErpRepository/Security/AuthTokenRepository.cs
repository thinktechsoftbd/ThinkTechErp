using Dapper;
using Npgsql;
using ThinkTechErpDomain.Models.Security;
using ThinkTechErpRepository.Database;

namespace ThinkTechErpRepository.Security;

public sealed class AuthTokenRepository(IDbConnectionFactory connectionFactory) : IAuthTokenRepository
{
    public async Task<DateTime> CreateRefreshTokenAsync(
        int userId, string tokenHash, TimeSpan lifetime, string? createdByIp, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection(SecurityDb.Connection);
        await connection.OpenAsync(cancellationToken);

        // Keep the table small. Revoked tokens stay until they expire so a stolen, already
        // rotated token can still be recognized if it comes back.
        await connection.ExecuteAsync(new CommandDefinition($"""
            DELETE FROM security.refresh_token
            WHERE user_id = @UserId AND expires_at < {SecurityDb.NowUtc}
            """, new { UserId = userId }, cancellationToken: cancellationToken));
        return await InsertRefreshTokenAsync(connection, null, userId, tokenHash, lifetime, createdByIp, cancellationToken);
    }

    public async Task<RefreshTokenRecord?> GetRefreshTokenAsync(string tokenHash, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection(SecurityDb.Connection);
        return await connection.QuerySingleOrDefaultAsync<RefreshTokenRecord>(new CommandDefinition($"""
            SELECT id AS Id,
                   user_id AS UserId,
                   expires_at <= {SecurityDb.NowUtc} AS IsExpired,
                   revoked_at IS NOT NULL AS IsRevoked,
                   coalesce(replaced_by_token_hash IS NOT NULL
                            AND revoked_at > {SecurityDb.NowUtc} - interval '30 seconds', false) AS WasJustRotated
            FROM security.refresh_token
            WHERE token_hash = @TokenHash
            """, new { TokenHash = tokenHash }, cancellationToken: cancellationToken));
    }

    public async Task<DateTime?> RotateRefreshTokenAsync(
        int tokenId, string newTokenHash, TimeSpan lifetime, string? createdByIp, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection(SecurityDb.Connection);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var userId = await connection.QuerySingleOrDefaultAsync<int?>(new CommandDefinition($"""
            UPDATE security.refresh_token
            SET revoked_at = {SecurityDb.NowUtc}, replaced_by_token_hash = @NewTokenHash
            WHERE id = @TokenId AND revoked_at IS NULL AND expires_at > {SecurityDb.NowUtc}
            RETURNING user_id
            """, new { TokenId = tokenId, NewTokenHash = newTokenHash }, transaction, cancellationToken: cancellationToken));
        if (userId is null)
            return null;

        var expiresAt = await InsertRefreshTokenAsync(
            connection, transaction, userId.Value, newTokenHash, lifetime, createdByIp, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return expiresAt;
    }

    public async Task RevokeRefreshTokenAsync(string tokenHash, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection(SecurityDb.Connection);
        await connection.ExecuteAsync(new CommandDefinition($"""
            UPDATE security.refresh_token
            SET revoked_at = {SecurityDb.NowUtc}
            WHERE token_hash = @TokenHash AND revoked_at IS NULL
            """, new { TokenHash = tokenHash }, cancellationToken: cancellationToken));
    }

    public async Task RevokeAllRefreshTokensAsync(int userId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection(SecurityDb.Connection);
        await connection.ExecuteAsync(new CommandDefinition($"""
            UPDATE security.refresh_token
            SET revoked_at = {SecurityDb.NowUtc}
            WHERE user_id = @UserId AND revoked_at IS NULL
            """, new { UserId = userId }, cancellationToken: cancellationToken));
    }

    public async Task CreatePasswordResetTokenAsync(
        int userId, string tokenHash, TimeSpan lifetime, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection(SecurityDb.Connection);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition($"""
            UPDATE security.password_reset_token
            SET used_at = {SecurityDb.NowUtc}
            WHERE user_id = @UserId AND used_at IS NULL;
            INSERT INTO security.password_reset_token (user_id, token_hash, expires_at, created_at)
            VALUES (@UserId, @TokenHash, {SecurityDb.NowUtc} + make_interval(secs => @Seconds), {SecurityDb.NowUtc});
            """, new { UserId = userId, TokenHash = tokenHash, Seconds = lifetime.TotalSeconds },
            transaction, cancellationToken: cancellationToken));
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<int?> ResetPasswordAsync(
        string tokenHash, string newPasswordHash, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection(SecurityDb.Connection);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var userId = await connection.QuerySingleOrDefaultAsync<int?>(new CommandDefinition($"""
            UPDATE security.password_reset_token
            SET used_at = {SecurityDb.NowUtc}
            WHERE token_hash = @TokenHash AND used_at IS NULL AND expires_at > {SecurityDb.NowUtc}
            RETURNING user_id
            """, new { TokenHash = tokenHash }, transaction, cancellationToken: cancellationToken));
        if (userId is null)
            return null;

        var updated = await connection.ExecuteAsync(new CommandDefinition($"""
            UPDATE security."User"
            SET "passwordHash" = @PasswordHash, "updatedAt" = {SecurityDb.NowUtc}
            WHERE id = @UserId AND status = 'ACTIVE'
            """, new { UserId = userId, PasswordHash = newPasswordHash }, transaction, cancellationToken: cancellationToken));
        if (updated == 0)
            return null;

        await connection.ExecuteAsync(new CommandDefinition($"""
            UPDATE security.refresh_token
            SET revoked_at = {SecurityDb.NowUtc}
            WHERE user_id = @UserId AND revoked_at IS NULL
            """, new { UserId = userId }, transaction, cancellationToken: cancellationToken));
        await transaction.CommitAsync(cancellationToken);
        return userId;
    }

    private static Task<DateTime> InsertRefreshTokenAsync(
        NpgsqlConnection connection, NpgsqlTransaction? transaction, int userId, string tokenHash, TimeSpan lifetime,
        string? createdByIp, CancellationToken cancellationToken)
        => connection.QuerySingleAsync<DateTime>(new CommandDefinition($"""
            INSERT INTO security.refresh_token (user_id, token_hash, expires_at, created_at, created_by_ip)
            VALUES (@UserId, @TokenHash, {SecurityDb.NowUtc} + make_interval(secs => @Seconds), {SecurityDb.NowUtc}, @CreatedByIp)
            RETURNING expires_at AT TIME ZONE 'utc'
            """, new
            {
                UserId = userId,
                TokenHash = tokenHash,
                Seconds = lifetime.TotalSeconds,
                CreatedByIp = createdByIp
            }, transaction, cancellationToken: cancellationToken));
}
