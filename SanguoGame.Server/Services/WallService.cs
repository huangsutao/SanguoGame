using FreeSql;
using SanguoGame.Core;
using SanguoGame.Core.Buildings;
using SanguoGame.Core.Shop;
using SanguoGame.Infrastructure.Entities;
using SanguoGame.Server.Contracts;

namespace SanguoGame.Server.Services;

public sealed class WallService
{
    private readonly IFreeSql _orm;
    private readonly BuildingService _buildings;

    public WallService(IFreeSql orm, BuildingService buildings)
    {
        _orm = orm;
        _buildings = buildings;
    }

    public async Task<WallsOverviewDto> GetOverviewAsync(long accountId, CancellationToken cancellationToken)
    {
        var city = await RequireCityAsync(accountId, cancellationToken);
        return await BuildOverviewAsync(city, cancellationToken);
    }

    public async Task<WallsOverviewDto> UpgradeAsync(
        long accountId,
        string wallType,
        CancellationToken cancellationToken)
    {
        var def = WallCatalog.Find(wallType)
            ?? throw new BizException(ErrorCodes.ValidationFailed, "未知建筑类型");

        await _buildings.StartUpgradeAsync(accountId, def.AsUpgradeDef(), cancellationToken);
        return await GetOverviewAsync(accountId, cancellationToken);
    }

    private async Task<WallsOverviewDto> BuildOverviewAsync(CityEntity city, CancellationToken cancellationToken)
    {
        var rows = await _orm.Select<BuildingEntity>()
            .Where(b => b.CityId == city.Id)
            .ToListAsync(cancellationToken);
        var buffs = await CityBuffStore.LoadAsync(_orm, city.Id, cancellationToken);
        return MapOverview(city, rows, buffs);
    }

    internal static WallsOverviewDto MapOverview(
        CityEntity city,
        IReadOnlyList<BuildingEntity> rows,
        IReadOnlyList<ActiveBuff>? buffs = null)
    {
        buffs ??= [];
        var now = DateTime.UtcNow;
        var upgradeSpeed = ItemCatalog.SpeedPercentOf("arrowTower", buffs, now);
        var byType = rows.ToDictionary(b => b.Type, StringComparer.OrdinalIgnoreCase);
        var palaceLevel = byType.TryGetValue("palace", out var palace) ? palace.Level : 0;
        var warehouseLevel = byType.TryGetValue("warehouse", out var warehouse) ? warehouse.Level : 0;
        var queueRow = rows.FirstOrDefault(b => b.Status == BuildingStatus.Upgrading);
        var stock = CityStats.Stock(city);
        var queueBusy = queueRow is not null;
        var levels = WallCatalog.All.ToDictionary(
            def => def.Type,
            def => byType.TryGetValue(def.Type, out var entity) ? entity.Level : 0,
            StringComparer.OrdinalIgnoreCase);
        var trapLevel = levels.TryGetValue("trap", out var trap) ? trap : 0;
        var defenseHallLevel = byType.TryGetValue(TechBonuses.DefenseHall, out var hall) ? hall.Level : 0;

        BuildingQueueDto? queue = null;
        if (queueRow is { TargetLevel: int qLevel, FinishAt: { } qFinish })
        {
            queue = new BuildingQueueDto(queueRow.Type, qLevel, qFinish);
        }

        var walls = WallCatalog.All.Select(def =>
        {
            byType.TryGetValue(def.Type, out var entity);
            var level = entity?.Level ?? 0;
            var status = entity?.Status ?? BuildingStatus.Idle;
            var nextLevel = level + 1;
            BuildingCostDto? next = null;
            string? blocked = null;

            if (level >= def.MaxLevel)
            {
                blocked = "maxLevel";
            }
            else
            {
                var cost = InnerBuildingCatalog.CostToReach(def.AsUpgradeDef(), nextLevel);
                next = new BuildingCostDto(
                    nextLevel,
                    ItemCatalog.ApplySpeed(InnerBuildingCatalog.DurationSeconds(def.AsUpgradeDef(), nextLevel), upgradeSpeed),
                    new ResourceDto(cost.Grain, cost.Wood, cost.Iron, cost.Copper));

                if (status == BuildingStatus.Upgrading)
                {
                    blocked = null;
                }
                else if (queueBusy)
                {
                    blocked = "queue";
                }
                else if (palaceLevel < def.RequirePalaceLevel)
                {
                    blocked = "prerequisite";
                }
                else if (stock.FirstMissingAgainst(cost) is not null)
                {
                    blocked = "resources";
                }
            }

            return new BuildingItemDto(
                def.Type,
                def.Name,
                BuildingCategory.Wall,
                level,
                def.MaxLevel,
                status,
                entity?.TargetLevel,
                entity?.FinishAt,
                EffectsOf(def, level),
                next,
                blocked);
        }).ToList();

        return new WallsOverviewDto(
            city.Id,
            DateTime.UtcNow,
            new ResourceDto(city.Grain, city.Wood, city.Iron, city.Copper),
            InnerBuildingCatalog.ResourceCap(warehouseLevel),
            WallCatalog.WallDefense(levels, defenseHallLevel),
            WallCatalog.TrapBonus(trapLevel, defenseHallLevel),
            queue,
            walls);
    }

    private async Task<CityEntity> RequireCityAsync(long accountId, CancellationToken cancellationToken)
    {
        var character = await _orm.Select<CharacterEntity>()
            .Where(c => c.AccountId == accountId)
            .FirstAsync(cancellationToken);
        if (character is null)
        {
            throw new BizException(ErrorCodes.NotFound, "尚未创建角色");
        }

        var city = await _orm.Select<CityEntity>()
            .Where(c => c.CharacterId == character.Id)
            .FirstAsync(cancellationToken);
        if (city is null)
        {
            throw new BizException(ErrorCodes.NotFound, "尚未建立主城");
        }

        return city;
    }

    private static IReadOnlyDictionary<string, int> EffectsOf(WallDef def, int level)
    {
        if (level <= 0)
        {
            return new Dictionary<string, int>();
        }

        if (def.TrapBonusPercentPerLevel > 0)
        {
            return new Dictionary<string, int> { ["trapBonus"] = def.TrapBonusPercentPerLevel * level };
        }

        return new Dictionary<string, int> { ["wallDefense"] = def.DefensePerLevel * level };
    }
}
