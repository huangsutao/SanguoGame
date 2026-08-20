using System.Data.Common;
using FreeSql;
using SanguoGame.Core;
using SanguoGame.Core.Buildings;
using SanguoGame.Core.Daily;
using SanguoGame.Infrastructure.Entities;
using SanguoGame.Server.Contracts;

namespace SanguoGame.Server.Services;

public sealed class FieldService
{
    private readonly IFreeSql _orm;
    private readonly BuildingService _buildings;
    private readonly DailyService _daily;

    public FieldService(IFreeSql orm, BuildingService buildings, DailyService daily)
    {
        _orm = orm;
        _buildings = buildings;
        _daily = daily;
    }

    public async Task<FieldsOverviewDto> GetOverviewAsync(long accountId, CancellationToken cancellationToken)
    {
        var city = await RequireCityAsync(accountId, cancellationToken);
        return await BuildOverviewAsync(city, DateTime.UtcNow, cancellationToken);
    }

    public async Task<FieldsOverviewDto> UpgradeAsync(
        long accountId,
        string fieldType,
        CancellationToken cancellationToken)
    {
        var def = OuterFieldCatalog.Find(fieldType)
            ?? throw new BizException(ErrorCodes.ValidationFailed, "未知建筑类型");

        await _buildings.StartUpgradeAsync(accountId, def.AsUpgradeDef(), cancellationToken);
        return await GetOverviewAsync(accountId, cancellationToken);
    }

    public async Task<(FieldsCollectDto Data, string Message)> CollectAsync(
        long accountId,
        string? fieldType,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<OuterFieldDef> targets;
        if (string.IsNullOrWhiteSpace(fieldType))
        {
            targets = OuterFieldCatalog.All;
        }
        else
        {
            var def = OuterFieldCatalog.Find(fieldType)
                ?? throw new BizException(ErrorCodes.ValidationFailed, "未知建筑类型");
            targets = [def];
        }

        var city = await RequireCityAsync(accountId, cancellationToken);
        var now = DateTime.UtcNow;

        var (gained, warehouseFull) = await CityRowLock.RunAsync(_orm, city.Id, async (transaction, lockedCity, ct) =>
        {
            var rows = await LoadBuildingsAsync(transaction, lockedCity.Id, ct);
            var byType = rows.ToDictionary(b => b.Type, StringComparer.OrdinalIgnoreCase);
            var warehouseLevel = byType.TryGetValue("warehouse", out var warehouse) ? warehouse.Level : 0;
            var resourceHallLevel = byType.TryGetValue(TechBonuses.ResourceHall, out var hall) ? hall.Level : 0;
            var resourceCap = InnerBuildingCatalog.ResourceCap(warehouseLevel);
            var stock = ToAmount(lockedCity);
            var gained = ResourceAmount.Zero;
            var warehouseFull = false;

            foreach (var def in targets)
            {
                if (!byType.TryGetValue(def.Type, out var entity) || entity.Level < 1)
                {
                    continue;
                }

                if (entity.LastCollectedAt is null)
                {
                    entity.LastCollectedAt = now;
                    entity.UpdatedAt = now;
                    await UpdateBuildingAsync(transaction, entity, ct);
                    continue;
                }

                var rate = TechBonuses.BoostedRate(def, entity.Level, resourceHallLevel);
                var pending = FieldProduction.Pending(
                    rate,
                    TechBonuses.BoostedCap(def, entity.Level, resourceHallLevel),
                    entity.LastCollectedAt,
                    now);
                if (pending <= 0)
                {
                    continue;
                }

                var space = resourceCap - stock.Get(def.Resource);
                var take = Math.Min(pending, Math.Max(0, space));
                if (take == 0)
                {
                    warehouseFull = true;
                }

                stock = stock.Add(def.Resource, take);
                gained = gained.Add(def.Resource, take);
                entity.LastCollectedAt = FieldProduction.AfterCollect(now, pending - take, rate);
                entity.UpdatedAt = now;
                await UpdateBuildingAsync(transaction, entity, ct);
            }

            ApplyStock(lockedCity, stock);
            await _orm.Update<CityEntity>()
                .WithTransaction(transaction)
                .SetSource(lockedCity)
                .UpdateColumns(c => new { c.Grain, c.Wood, c.Iron, c.Copper })
                .ExecuteAffrowsAsync(ct);

            city.Grain = lockedCity.Grain;
            city.Wood = lockedCity.Wood;
            city.Iron = lockedCity.Iron;
            city.Copper = lockedCity.Copper;
            return (gained, warehouseFull);
        }, cancellationToken);

        if (gained.Total > 0)
        {
            await _daily.AddProgressAsync(city.Id, DailyCatalog.Collect, 1, cancellationToken);
        }

        var overview = await BuildOverviewAsync(city, now, cancellationToken);
        var data = new FieldsCollectDto(
            overview.CityId,
            overview.ServerTime,
            overview.Resources,
            overview.ResourceCap,
            new ResourceDto(gained.Grain, gained.Wood, gained.Iron, gained.Copper),
            overview.Fields);
        var message = warehouseFull ? "仓库已满" : "ok";
        return (data, message);
    }

    private async Task<FieldsOverviewDto> BuildOverviewAsync(
        CityEntity city,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var rows = await _orm.Select<BuildingEntity>()
            .Where(b => b.CityId == city.Id)
            .ToListAsync(cancellationToken);
        return MapOverview(city, rows, now);
    }

    internal static FieldsOverviewDto MapOverview(CityEntity city, IReadOnlyList<BuildingEntity> rows, DateTime now)
    {
        var byType = rows.ToDictionary(b => b.Type, StringComparer.OrdinalIgnoreCase);
        var palaceLevel = byType.TryGetValue("palace", out var palace) ? palace.Level : 0;
        var warehouseLevel = byType.TryGetValue("warehouse", out var warehouse) ? warehouse.Level : 0;
        var resourceHallLevel = byType.TryGetValue(TechBonuses.ResourceHall, out var hall) ? hall.Level : 0;
        var queueRow = rows.FirstOrDefault(b => b.Status == BuildingStatus.Upgrading);
        var stock = ToAmount(city);
        var queueBusy = queueRow is not null;
        var resourceCap = InnerBuildingCatalog.ResourceCap(warehouseLevel);

        BuildingQueueDto? queue = null;
        if (queueRow is { TargetLevel: int qLevel, FinishAt: { } qFinish })
        {
            queue = new BuildingQueueDto(queueRow.Type, qLevel, qFinish);
        }

        var fields = OuterFieldCatalog.All.Select(def =>
        {
            byType.TryGetValue(def.Type, out var entity);
            var level = entity?.Level ?? 0;
            var status = entity?.Status ?? BuildingStatus.Idle;
            var nextLevel = level + 1;
            BuildingCostDto? next = null;
            string? blocked = null;
            var rate = TechBonuses.BoostedRate(def, level, resourceHallLevel);
            var fieldCap = TechBonuses.BoostedCap(def, level, resourceHallLevel);
            var pending = FieldProduction.Pending(rate, fieldCap, entity?.LastCollectedAt, now);

            if (level >= def.MaxLevel)
            {
                blocked = "maxLevel";
            }
            else
            {
                var cost = InnerBuildingCatalog.CostToReach(def.AsUpgradeDef(), nextLevel);
                next = new BuildingCostDto(
                    nextLevel,
                    InnerBuildingCatalog.DurationSeconds(def.AsUpgradeDef(), nextLevel),
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

            return new FieldItemDto(
                def.Type,
                def.Name,
                def.Resource,
                level,
                def.MaxLevel,
                status,
                entity?.TargetLevel,
                entity?.FinishAt,
                rate,
                fieldCap,
                pending,
                entity?.LastCollectedAt,
                next,
                blocked);
        }).ToList();

        return new FieldsOverviewDto(
            city.Id,
            now,
            new ResourceDto(city.Grain, city.Wood, city.Iron, city.Copper),
            resourceCap,
            queue,
            fields);
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

    private Task<List<BuildingEntity>> LoadBuildingsAsync(
        DbTransaction transaction,
        long cityId,
        CancellationToken cancellationToken) =>
        _orm.Select<BuildingEntity>()
            .WithTransaction(transaction)
            .Where(b => b.CityId == cityId)
            .ToListAsync(cancellationToken);

    private Task<int> UpdateBuildingAsync(
        DbTransaction transaction,
        BuildingEntity entity,
        CancellationToken cancellationToken) =>
        _orm.Update<BuildingEntity>()
            .WithTransaction(transaction)
            .SetSource(entity)
            .ExecuteAffrowsAsync(cancellationToken);

    private static ResourceAmount ToAmount(CityEntity city) =>
        new(city.Grain, city.Wood, city.Iron, city.Copper);

    private static void ApplyStock(CityEntity city, ResourceAmount stock)
    {
        city.Grain = stock.Grain;
        city.Wood = stock.Wood;
        city.Iron = stock.Iron;
        city.Copper = stock.Copper;
    }
}
