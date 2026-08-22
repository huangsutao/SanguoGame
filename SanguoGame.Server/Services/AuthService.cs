using System.Data.Common;
using FreeSql;
using Microsoft.AspNetCore.Identity;
using SanguoGame.Core;
using SanguoGame.Infrastructure;
using SanguoGame.Infrastructure.Entities;
using SanguoGame.Server.Contracts;
using SanguoGame.Server.Security;

namespace SanguoGame.Server.Services;

public sealed class AuthService
{
    public const int RefreshReuseGraceSeconds = 15;

    private const string ReservedAiPrefix = "ai_";

    private readonly IFreeSql _orm;
    private readonly JwtIssuer _jwt;
    private readonly PasswordHasher<AccountEntity> _passwordHasher;

    public AuthService(IFreeSql orm, JwtIssuer jwt, PasswordHasher<AccountEntity> passwordHasher)
    {
        _orm = orm;
        _jwt = jwt;
        _passwordHasher = passwordHasher;
    }

    public async Task<TokenResponse> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken)
    {
        var username = request.Username.Trim();
        var normalized = NormalizeUsername(username);
        if (normalized.StartsWith(ReservedAiPrefix, StringComparison.Ordinal))
        {
            throw new BizException(ErrorCodes.ValidationFailed, "用户名不可使用");
        }

        var now = DateTime.UtcNow;
        var account = new AccountEntity
        {
            Username = username,
            UsernameNormalized = normalized,
            CreatedAt = now
        };
        account.PasswordHash = _passwordHasher.HashPassword(account, request.Password);

        try
        {
            account.Id = await _orm.Insert(account).ExecuteIdentityAsync(cancellationToken);
        }
        catch (Exception ex) when (DbErrors.IsUniqueViolation(ex))
        {
            throw new BizException(ErrorCodes.UsernameTaken, "用户名已注册");
        }

        return await IssueTokensAsync(account, cancellationToken);
    }

    public async Task<TokenResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        var account = await _orm.Select<AccountEntity>()
            .Where(a => a.UsernameNormalized == NormalizeUsername(request.Username))
            .FirstAsync(cancellationToken);

        if (account is null)
        {
            throw new BizException(ErrorCodes.Unauthorized, "用户名或密码错误");
        }

        var verify = _passwordHasher.VerifyHashedPassword(account, account.PasswordHash, request.Password);
        if (verify == PasswordVerificationResult.Failed)
        {
            throw new BizException(ErrorCodes.Unauthorized, "用户名或密码错误");
        }

        if (verify == PasswordVerificationResult.SuccessRehashNeeded)
        {
            account.PasswordHash = _passwordHasher.HashPassword(account, request.Password);
            await _orm.Update<AccountEntity>()
                .SetSource(account)
                .UpdateColumns(a => a.PasswordHash)
                .ExecuteAffrowsAsync(cancellationToken);
        }

        var tokens = await IssueTokensAsync(account, cancellationToken);
        await PurgeRefreshTokensAsync(cancellationToken);
        return tokens;
    }

    public async Task<TokenResponse> RefreshAsync(RefreshRequest request, CancellationToken cancellationToken)
    {
        var hash = JwtIssuer.HashRefreshToken(request.RefreshToken);
        var now = DateTime.UtcNow;
        using var conn = await _orm.Ado.MasterPool.GetAsync();
        await using var transaction = await conn.Value.BeginTransactionAsync(cancellationToken);
        try
        {
            var stored = await _orm.Select<RefreshTokenEntity>()
                .WithTransaction(transaction)
                .ForUpdate()
                .Where(t => t.TokenHash == hash)
                .FirstAsync(cancellationToken);

            if (stored is null || stored.ExpiresAt <= now)
            {
                await transaction.RollbackAsync(cancellationToken);
                throw new BizException(ErrorCodes.Unauthorized, "刷新令牌无效或已过期");
            }

            if (stored.RevokedAt is not null)
            {
                var reusedAfterGrace = now - stored.RevokedAt.Value > TimeSpan.FromSeconds(RefreshReuseGraceSeconds);
                if (reusedAfterGrace)
                {
                    await _orm.Update<RefreshTokenEntity>()
                        .WithTransaction(transaction)
                        .Where(t => t.AccountId == stored.AccountId && t.RevokedAt == null)
                        .Set(t => t.RevokedAt, now)
                        .ExecuteAffrowsAsync(cancellationToken);
                    await transaction.CommitAsync(cancellationToken);
                }
                else
                {
                    await transaction.RollbackAsync(cancellationToken);
                }

                throw new BizException(ErrorCodes.Unauthorized, "刷新令牌无效或已过期");
            }

            var account = await _orm.Select<AccountEntity>()
                .WithTransaction(transaction)
                .Where(a => a.Id == stored.AccountId)
                .FirstAsync(cancellationToken);
            if (account is null)
            {
                await transaction.RollbackAsync(cancellationToken);
                throw new BizException(ErrorCodes.Unauthorized, "刷新令牌无效或已过期");
            }

            stored.RevokedAt = now;
            await _orm.Update<RefreshTokenEntity>()
                .WithTransaction(transaction)
                .SetSource(stored)
                .UpdateColumns(t => t.RevokedAt)
                .ExecuteAffrowsAsync(cancellationToken);

            var tokens = await IssueTokensAsync(account, cancellationToken, transaction);
            await transaction.CommitAsync(cancellationToken);
            await PurgeRefreshTokensAsync(cancellationToken);
            return tokens;
        }
        catch (BizException)
        {
            throw;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task LogoutAsync(LogoutRequest request, CancellationToken cancellationToken)
    {
        var hash = JwtIssuer.HashRefreshToken(request.RefreshToken);
        var stored = await _orm.Select<RefreshTokenEntity>()
            .Where(t => t.TokenHash == hash && t.RevokedAt == null)
            .FirstAsync(cancellationToken);
        if (stored is null)
        {
            return;
        }

        stored.RevokedAt = DateTime.UtcNow;
        await _orm.Update<RefreshTokenEntity>()
            .SetSource(stored)
            .UpdateColumns(t => t.RevokedAt)
            .ExecuteAffrowsAsync(cancellationToken);
    }

    public async Task<SessionResponse> GetSessionAsync(long accountId, CancellationToken cancellationToken)
    {
        var account = await _orm.Select<AccountEntity>()
            .Where(a => a.Id == accountId)
            .FirstAsync(cancellationToken);
        if (account is null)
        {
            throw new BizException(ErrorCodes.Unauthorized, "未登录或令牌无效");
        }

        var character = await _orm.Select<CharacterEntity>()
            .Where(c => c.AccountId == accountId)
            .FirstAsync(cancellationToken);
        SessionCharacterDto? characterDto = character is null ? null : new SessionCharacterDto(character.Id, character.Name);
        SessionCityDto? cityDto = null;
        if (character is not null)
        {
            var city = await _orm.Select<CityEntity>()
                .Where(c => c.CharacterId == character.Id)
                .FirstAsync(cancellationToken);
            if (city is not null)
            {
                cityDto = new SessionCityDto(city.Id, city.Name, city.X, city.Y);
            }
        }

        return new SessionResponse(account.Id, account.Username, characterDto, cityDto);
    }

    private async Task<TokenResponse> IssueTokensAsync(
        AccountEntity account,
        CancellationToken cancellationToken,
        DbTransaction? transaction = null)
    {
        var (accessToken, expiresAt) = _jwt.IssueAccessToken(account.Id, account.Username);
        var (rawRefresh, hash, refreshExpires) = _jwt.IssueRefreshToken();
        var insert = _orm.Insert(new RefreshTokenEntity
        {
            AccountId = account.Id,
            TokenHash = hash,
            ExpiresAt = refreshExpires,
            CreatedAt = DateTime.UtcNow
        });
        if (transaction is not null)
        {
            insert = insert.WithTransaction(transaction);
        }

        await insert.ExecuteAffrowsAsync(cancellationToken);
        return new TokenResponse(accessToken, rawRefresh, expiresAt);
    }

    private async Task PurgeRefreshTokensAsync(CancellationToken cancellationToken)
    {
        try
        {
            var now = DateTime.UtcNow;
            var revokedBefore = now.AddDays(-7);
            await _orm.Delete<RefreshTokenEntity>()
                .Where(t => t.ExpiresAt < now || (t.RevokedAt != null && t.RevokedAt < revokedBefore))
                .ExecuteAffrowsAsync(cancellationToken);
        }
        catch
        {
            // 清理失败不影响登录 / 刷新。
        }
    }

    private static string NormalizeUsername(string username) => username.Trim().ToLowerInvariant();
}
