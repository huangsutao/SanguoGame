using FreeSql;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using SanguoGame.Core.World;
using SanguoGame.Infrastructure;
using SanguoGame.Infrastructure.Entities;

namespace SanguoGame.Server.Services;

public sealed class SeedService
{
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
        await EnsureOutpostsAsync(cancellationToken);
        await EnsureAiAsync(cancellationToken);
    }

    private async Task EnsureOutpostsAsync(CancellationToken cancellationToken)
    {
        var existing = (int)await _orm.Select<OutpostEntity>().CountAsync(cancellationToken);
        for (var i = existing; i < _map.OutpostCount; i++)
        {
            var def = OutpostCatalog.All[i % OutpostCatalog.All.Count];
            if (!MapPlacement.TryPickEmptyCell(
                    _map.Width,
                    _map.Height,
                    _map.PlacementMaxAttempts,
                    (x, y) => WorldOccupancy.IsOccupied(_orm, x, y),
                    out var x,
                    out var y))
            {
                _logger.LogWarning("据点空地不足，已生成 {Count} 座", i);
                break;
            }

            try
            {
                await _orm.Insert(new OutpostEntity
                {
                    Type = def.Type,
                    Name = $"{def.Name}·{x},{y}",
                    X = x,
                    Y = y,
                    Garrison = def.Garrison
                }).ExecuteAffrowsAsync(cancellationToken);
            }
            catch (Exception ex) when (DbErrors.IsUniqueViolation(ex))
            {
                i--;
            }
        }
    }

    private async Task EnsureAiAsync(CancellationToken cancellationToken)
    {
        var existing = (int)await _orm.Select<AccountEntity>().Where(a => a.IsAi).CountAsync(cancellationToken);
        for (var i = existing; i < _map.AiCityCount; i++)
        {
            var n = i + 1;
            var username = $"ai_{n:000}";
            if (await _orm.Select<AccountEntity>().AnyAsync(a => a.UsernameNormalized == username, cancellationToken))
            {
                continue;
            }

            var account = new AccountEntity
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
                continue;
            }

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
                continue;
            }

            try
            {
                await _cities.FoundAsync(account.Id, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "AI 建城失败 {Username}", username);
            }
        }
    }
}
