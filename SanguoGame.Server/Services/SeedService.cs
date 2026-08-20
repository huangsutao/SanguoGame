using FreeSql;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using SanguoGame.Core.World;
using SanguoGame.Infrastructure;
using SanguoGame.Infrastructure.Entities;

namespace SanguoGame.Server.Services;

public sealed class SeedService
{
    private const long WorldSeedLockId = 87342016;

    private readonly IFreeSql _orm;
    private readonly WorldMapOptions _map;
    private readonly CityService _cities;
    private readonly PasswordHasher<AccountEntity> _passwords;
    private readonly ILogger<SeedService> _logger;

    public SeedService(
        IFreeSql orm,
        IOptions<WorldMapOptions> map,
        CityService cities,
        PasswordHasher<AccountEntity> passwords,
        ILogger<SeedService> logger)
    {
        _orm = orm;
        _map = map.Value;
        _cities = cities;
        _passwords = passwords;
        _logger = logger;
    }

    public async Task EnsureWorldAsync(CancellationToken cancellationToken)
    {
        await _orm.Ado.ExecuteNonQueryAsync("SELECT pg_advisory_lock(" + WorldSeedLockId + ")");
        try
        {
            await EnsureOutpostsAsync(cancellationToken);
            await EnsureMarketsAsync(cancellationToken);
            await EnsureAiAsync(cancellationToken);
        }
        finally
        {
            await _orm.Ado.ExecuteNonQueryAsync("SELECT pg_advisory_unlock(" + WorldSeedLockId + ")");
        }
    }

    private async Task EnsureOutpostsAsync(CancellationToken cancellationToken)
    {
        var existing = (int)await _orm.Select<OutpostEntity>()
            .Where(o => o.Kind == OutpostKind.Permanent)
            .CountAsync(cancellationToken);
        var attempts = 0;
        var maxAttempts = Math.Max(_map.OutpostCount * 4, 8);
        while (existing < _map.OutpostCount && attempts < maxAttempts)
        {
            attempts++;
            var def = OutpostCatalog.Permanent[existing % OutpostCatalog.Permanent.Count];
            var cell = await MapPlacement.TryPickEmptyCellAsync(
                _map.Width,
                _map.Height,
                _map.PlacementMaxAttempts,
                (x, y, ct) => WorldOccupancy.IsOccupiedAsync(_orm, x, y, ct),
                cancellationToken);
            if (cell is null)
            {
                _logger.LogWarning("据点空地不足，已生成 {Count} 座", existing);
                break;
            }

            try
            {
                await _orm.Insert(new OutpostEntity
                {
                    Type = def.Type,
                    Name = $"{def.Name}·{cell.Value.X},{cell.Value.Y}",
                    X = cell.Value.X,
                    Y = cell.Value.Y,
                    Garrison = def.Garrison,
                    Kind = OutpostKind.Permanent
                }).ExecuteAffrowsAsync(cancellationToken);
                existing++;
            }
            catch (Exception ex) when (DbErrors.IsUniqueViolation(ex))
            {
            }
        }
    }

    private async Task EnsureMarketsAsync(CancellationToken cancellationToken)
    {
        var existing = (int)await _orm.Select<MarketEntity>().CountAsync(cancellationToken);
        var attempts = 0;
        var maxAttempts = Math.Max(_map.MarketCount * 4, 8);
        while (existing < _map.MarketCount && attempts < maxAttempts)
        {
            attempts++;
            var cell = await MapPlacement.TryPickEmptyCellAsync(
                _map.Width,
                _map.Height,
                _map.PlacementMaxAttempts,
                (x, y, ct) => WorldOccupancy.IsOccupiedAsync(_orm, x, y, ct),
                cancellationToken);
            if (cell is null)
            {
                _logger.LogWarning("市集空地不足，已生成 {Count} 座", existing);
                break;
            }

            try
            {
                await _orm.Insert(new MarketEntity
                {
                    Name = $"市集·{cell.Value.X},{cell.Value.Y}",
                    X = cell.Value.X,
                    Y = cell.Value.Y
                }).ExecuteAffrowsAsync(cancellationToken);
                existing++;
            }
            catch (Exception ex) when (DbErrors.IsUniqueViolation(ex))
            {
            }
        }
    }

    private async Task EnsureAiAsync(CancellationToken cancellationToken)
    {
        var existing = await CountAiCitiesAsync(cancellationToken);
        for (var n = 1; existing < _map.AiCityCount && n <= _map.AiCityCount + 32; n++)
        {
            var username = $"ai_{n:000}";
            try
            {
                if (await EnsureOneAiCityAsync(username, n, cancellationToken))
                {
                    existing++;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "AI 补齐失败 {Username}", username);
            }
        }
    }

    private async Task<bool> EnsureOneAiCityAsync(string username, int n, CancellationToken cancellationToken)
    {
        var account = await _orm.Select<AccountEntity>()
            .Where(a => a.UsernameNormalized == username)
            .FirstAsync(cancellationToken);
        if (account is null)
        {
            account = new AccountEntity
            {
                Username = username,
                UsernameNormalized = username,
                IsAi = true,
                CreatedAt = DateTime.UtcNow
            };
            account.PasswordHash = _passwords.HashPassword(account, Guid.NewGuid().ToString("N") + "Aa1!");
            try
            {
                account.Id = await _orm.Insert(account).ExecuteIdentityAsync(cancellationToken);
            }
            catch (Exception ex) when (DbErrors.IsUniqueViolation(ex))
            {
                account = await _orm.Select<AccountEntity>()
                    .Where(a => a.UsernameNormalized == username)
                    .FirstAsync(cancellationToken);
            }
        }

        if (account is null || !account.IsAi)
        {
            return false;
        }

        var character = await _orm.Select<CharacterEntity>()
            .Where(c => c.AccountId == account.Id)
            .FirstAsync(cancellationToken);
        if (character is null)
        {
            var baseName = n <= AiTemplates.CharacterNames.Count
                ? AiTemplates.CharacterNames[n - 1]
                : $"黄巾{n}";
            var name = baseName;
            for (var suffix = 1; suffix < 20; suffix++)
            {
                if (!await _orm.Select<CharacterEntity>().AnyAsync(c => c.Name == name, cancellationToken))
                {
                    break;
                }

                name = $"{baseName}{suffix}";
            }

            try
            {
                await _orm.Insert(new CharacterEntity
                {
                    AccountId = account.Id,
                    Name = name,
                    CreatedAt = DateTime.UtcNow
                }).ExecuteAffrowsAsync(cancellationToken);
            }
            catch (Exception ex) when (DbErrors.IsUniqueViolation(ex))
            {
                _logger.LogWarning(ex, "AI 角色创建失败 {Username}", username);
            }

            character = await _orm.Select<CharacterEntity>()
                .Where(c => c.AccountId == account.Id)
                .FirstAsync(cancellationToken);
        }

        if (character is null)
        {
            return false;
        }

        if (await _orm.Select<CityEntity>().AnyAsync(c => c.CharacterId == character.Id, cancellationToken))
        {
            return false;
        }

        await _cities.FoundAsync(account.Id, cancellationToken);
        return true;
    }

    private async Task<int> CountAiCitiesAsync(CancellationToken cancellationToken)
    {
        var aiAccountIds = (await _orm.Select<AccountEntity>()
                .Where(a => a.IsAi)
                .ToListAsync(cancellationToken))
            .Select(a => a.Id)
            .ToArray();
        if (aiAccountIds.Length == 0)
        {
            return 0;
        }

        var characterIds = (await _orm.Select<CharacterEntity>()
                .Where(c => aiAccountIds.Contains(c.AccountId))
                .ToListAsync(cancellationToken))
            .Select(c => c.Id)
            .ToArray();
        if (characterIds.Length == 0)
        {
            return 0;
        }

        return (int)await _orm.Select<CityEntity>()
            .Where(c => characterIds.Contains(c.CharacterId))
            .CountAsync(cancellationToken);
    }
}
