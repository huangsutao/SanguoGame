using Microsoft.Extensions.Options;
using SanguoGame.Core;
using SanguoGame.Core.Army;
using SanguoGame.Core.Buildings;
using SanguoGame.Core.World;
using SanguoGame.Infrastructure.Entities;
using SanguoGame.Server.Contracts;
using FreeSql;

namespace SanguoGame.Server.Services;

public sealed class AiService
{
    private readonly IFreeSql _orm;
    private readonly WorldMapOptions _map;
    private readonly BuildingService _buildings;
    private readonly FieldService _fields;
    private readonly WallService _walls;
    private readonly ArmyService _army;
    private readonly MarchService _marches;
    private readonly ILogger<AiService> _logger;

    public AiService(
        IFreeSql orm,
        IOptions<WorldMapOptions> map,
        BuildingService buildings,
        FieldService fields,
        WallService walls,
        ArmyService army,
        MarchService marches,
        ILogger<AiService> logger)
    {
        _orm = orm;
        _map = map.Value;
        _buildings = buildings;
        _fields = fields;
        _walls = walls;
        _army = army;
        _marches = marches;
        _logger = logger;
    }

    public async Task TickAsync(CancellationToken cancellationToken)
    {
        var accounts = await _orm.Select<AccountEntity>()
            .Where(a => a.IsAi)
            .ToListAsync(cancellationToken);
        foreach (var account in accounts)
        {
            try
            {
                await TickOneAsync(account.Id, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "AI tick 失败 account={AccountId}", account.Id);
            }
        }
    }

    private async Task TickOneAsync(long accountId, CancellationToken cancellationToken)
    {
        var character = await _orm.Select<CharacterEntity>()
            .Where(c => c.AccountId == accountId)
            .FirstAsync(cancellationToken);
        if (character is null)
        {
            return;
        }

        var city = await _orm.Select<CityEntity>()
            .Where(c => c.CharacterId == character.Id)
            .FirstAsync(cancellationToken);
        if (city is null)
        {
            return;
        }

        try
        {
            await _fields.CollectAsync(accountId, null, cancellationToken);
        }
        catch (BizException)
        {
        }

        await TryUpgradeAsync(accountId, cancellationToken);
        await TryRecruitAsync(accountId, cancellationToken);
        await TryMarchAsync(accountId, city, cancellationToken);
    }

    private async Task TryUpgradeAsync(long accountId, CancellationToken cancellationToken)
    {
        foreach (var type in AiTemplates.UpgradeOrder)
        {
            try
            {
                if (OuterFieldCatalog.IsField(type))
                {
                    await _fields.UpgradeAsync(accountId, type, cancellationToken);
                    return;
                }

                if (WallCatalog.IsWall(type))
                {
                    await _walls.UpgradeAsync(accountId, type, cancellationToken);
                    return;
                }

                await _buildings.UpgradeAsync(accountId, type, cancellationToken);
                return;
            }
            catch (BizException)
            {
            }
        }
    }

    private async Task TryRecruitAsync(long accountId, CancellationToken cancellationToken)
    {
        try
        {
            var overview = await _army.GetOverviewAsync(accountId, cancellationToken);
            if (overview.BarracksLevel < 1 || overview.Troops.Infantry + overview.Troops.Archer + overview.Troops.Cavalry >= overview.TroopCap / 2)
            {
                return;
            }

            var count = Math.Min(10, overview.TroopCap / 2 - (overview.Troops.Infantry + overview.Troops.Archer + overview.Troops.Cavalry));
            if (count > 0)
            {
                await _army.RecruitAsync(accountId, "infantry", count, cancellationToken);
            }
        }
        catch (BizException)
        {
        }
    }

    private async Task TryMarchAsync(long accountId, CityEntity city, CancellationToken cancellationToken)
    {
        if (city.Infantry < 20)
        {
            return;
        }

        var marching = await _orm.Select<MarchEntity>()
            .Where(m => m.FromCityId == city.Id && m.Status == MarchStatus.Marching)
            .CountAsync(cancellationToken);
        if (marching >= _map.MaxMarchesPerCity)
        {
            return;
        }

        var request = await PickTargetAsync(city, cancellationToken);
        if (request is null)
        {
            return;
        }

        try
        {
            await _marches.StartAsync(accountId, request, cancellationToken);
        }
        catch (BizException)
        {
        }
    }

    private async Task<MarchRequest?> PickTargetAsync(CityEntity city, CancellationToken cancellationToken)
    {
        var outposts = await _orm.Select<OutpostEntity>().ToListAsync(cancellationToken);
        var now = DateTime.UtcNow;
        var nearbyOutpost = outposts
            .Select(o => new { o, dist = Math.Abs(o.X - city.X) + Math.Abs(o.Y - city.Y) })
            .Where(x => x.dist is > 0 and <= 40
                && !OutpostCatalog.IsExpired(x.o.Kind, x.o.ExpiresAt, now)
                && (x.o.Garrison > 0
                    || (x.o.Kind == OutpostKind.Permanent && (x.o.RecoverAt is null || x.o.RecoverAt <= now))))
            .OrderBy(x => x.dist)
            .FirstOrDefault();
        if (nearbyOutpost is not null)
        {
            return new MarchRequest
            {
                TargetType = "outpost",
                TargetId = nearbyOutpost.o.Id,
                Infantry = 20
            };
        }

        var others = await _orm.Select<CityEntity>()
            .Where(c => c.Id != city.Id)
            .ToListAsync(cancellationToken);
        var target = others
            .Where(c => !CityStats.IsProtected(c, now))
            .Select(c => new { c, dist = Math.Abs(c.X - city.X) + Math.Abs(c.Y - city.Y), troops = c.Infantry + c.Archer + c.Cavalry })
            .Where(x => x.dist is > 0 and <= 40 && x.troops < city.Infantry + city.Archer + city.Cavalry)
            .OrderBy(x => x.dist)
            .FirstOrDefault();
        if (target is null)
        {
            return null;
        }

        return new MarchRequest
        {
            TargetType = "city",
            TargetId = target.c.Id,
            Infantry = 20
        };
    }
}
