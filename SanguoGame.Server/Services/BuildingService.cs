using FreeSql;
using Hangfire;
using Microsoft.AspNetCore.SignalR;
using SanguoGame.Core;
using SanguoGame.Core.Buildings;
using SanguoGame.Core.Daily;
using SanguoGame.Core.Shop;
using SanguoGame.Infrastructure;
using SanguoGame.Infrastructure.Entities;
using SanguoGame.Server.Contracts;
using SanguoGame.Server.Hubs;
using SanguoGame.Server.Jobs;

namespace SanguoGame.Server.Services;

public sealed class BuildingService
{
    private readonly IFreeSql _orm;
    private readonly IBackgroundJobClient _jobs;
    private readonly IHubContext<GameHub> _hub;
    private readonly DailyService _daily;

    public BuildingService(IFreeSql orm, IBackgroundJobClient jobs, IHubContext<GameHub> hub, DailyService daily)
    {
        _orm = orm;
        _jobs = jobs;
        _hub = hub;
        _daily = daily;
    }

    public async Task<BuildingsOverviewDto> GetOverviewAsync(long accountId, CancellationToken cancellationToken) =>
        await BuildOverviewAsync(await RequireCityAsync(accountId, cancellationToken), cancellationToken);

    public async Task<BuildingsOverviewDto> UpgradeAsync(
        long accountId,
        string buildingType,
        CancellationToken cancellationToken)
    {
        var def = InnerBuildingCatalog.Find(buildingType)
            ?? throw new BizException(ErrorCodes.ValidationFailed, "未知建筑类型");

        await StartUpgradeAsync(accountId, def, cancellationToken);
        return await GetOverviewAsync(accountId, cancellationToken);
    }

    public async Task StartUpgradeAsync(
        long accountId,
        InnerBuildingDef def,
        CancellationToken cancellationToken)
    {
        var city = await RequireCityAsync(accountId, cancellationToken);
        var planned = await CityRowLock.RunAsync(_orm, city.Id, async (transaction, lockedCity, ct) =>
        {
            var rows = await _orm.Select<BuildingEntity>()
                .WithTransaction(transaction)
                .Where(b => b.CityId == lockedCity.Id)
                .ToListAsync(ct);
            var byType = rows.ToDictionary(b => b.Type, StringComparer.OrdinalIgnoreCase);
            var kind = QueueRules.OfBuilding(def.Type);

            byType.TryGetValue(def.Type, out var entity);
            if (entity is { Status: BuildingStatus.Upgrading })
            {
                throw new BizException(ErrorCodes.BuildingQueueBusy, "该建筑正在升级");
            }

            if (QueueSlots.IsFull(lockedCity, rows, kind))
            {
                throw new BizException(ErrorCodes.BuildingQueueBusy, "该队列已满");
            }
            var level = entity?.Level ?? 0;
            if (level >= def.MaxLevel)
            {
                throw new BizException(ErrorCodes.BuildingMaxLevel, "建筑已满级");
            }

            var palaceLevel = byType.TryGetValue("palace", out var palace) ? palace.Level : 0;
            if (palaceLevel < def.RequirePalaceLevel)
            {
                throw new BizException(ErrorCodes.BuildingPrerequisite, $"需要主殿 {def.RequirePalaceLevel} 级");
            }

            var academyLevel = byType.TryGetValue("academy", out var academy) ? academy.Level : 0;
            if (academyLevel < def.RequireAcademyLevel)
            {
                throw new BizException(ErrorCodes.BuildingPrerequisite, $"需要书院 {def.RequireAcademyLevel} 级");
            }

            var targetLevel = level + 1;
            var cost = InnerBuildingCatalog.CostToReach(def, targetLevel);
            var stock = ToAmount(lockedCity);
            var missing = stock.FirstMissingAgainst(cost);
            if (missing is not null)
            {
                throw new BizException(ErrorCodes.InsufficientResources, $"资源不足（缺{missing}）");
            }

            var now = DateTime.UtcNow;
            var buffs = await CityBuffStore.LoadAsync(_orm, lockedCity.Id, ct, transaction);
            var speed = ItemCatalog.SpeedPercentOf(def.Type, buffs, now);
            var plannedFinish = now.AddSeconds(
                ItemCatalog.ApplySpeed(InnerBuildingCatalog.DurationSeconds(def, targetLevel), speed));
            var remain = stock.Subtract(cost);
            lockedCity.Grain = remain.Grain;
            lockedCity.Wood = remain.Wood;
            lockedCity.Iron = remain.Iron;
            lockedCity.Copper = remain.Copper;

            if (entity is null)
            {
                entity = new BuildingEntity
                {
                    CityId = lockedCity.Id,
                    Type = def.Type,
                    Level = 0,
                    Status = BuildingStatus.Upgrading,
                    TargetLevel = targetLevel,
                    FinishAt = plannedFinish,
                    UpdatedAt = now
                };
            }
            else
            {
                entity.Status = BuildingStatus.Upgrading;
                entity.TargetLevel = targetLevel;
                entity.FinishAt = plannedFinish;
                entity.UpdatedAt = now;
            }

            try
            {
                await _orm.Update<CityEntity>()
                    .WithTransaction(transaction)
                    .SetSource(lockedCity)
                    .UpdateColumns(c => new { c.Grain, c.Wood, c.Iron, c.Copper })
                    .ExecuteAffrowsAsync(ct);

                if (entity.Id == 0)
                {
                    entity.Id = await _orm.Insert(entity).WithTransaction(transaction).ExecuteIdentityAsync(ct);
                }
                else
                {
                    await _orm.Update<BuildingEntity>()
                        .WithTransaction(transaction)
                        .SetSource(entity)
                        .ExecuteAffrowsAsync(ct);
                }
            }
            catch (Exception ex) when (DbErrors.IsUniqueViolation(ex))
            {
                throw new BizException(ErrorCodes.BuildingQueueBusy, "该队列已满");
            }

            return (def.Type, targetLevel, plannedFinish);
        }, cancellationToken);

        var buildingType = planned.Item1;
        var targetLevel = planned.Item2;
        var finishAt = planned.Item3;
        _jobs.Schedule<CompleteInnerBuildingJob>(
            job => job.Execute(city.Id, buildingType, targetLevel),
            UtcSchedule.At(finishAt));
        await _daily.AddProgressAsync(city.Id, DailyCatalog.Upgrade, 1, cancellationToken);
    }

    public async Task CompleteAsync(long cityId, string buildingType, int targetLevel, CancellationToken cancellationToken)
    {
        BuildingEntity? entity;
        CityEntity city;
        try
        {
            (entity, city) = await CityRowLock.RunAsync(_orm, cityId, async (transaction, lockedCity, ct) =>
            {
                var row = await _orm.Select<BuildingEntity>()
                    .WithTransaction(transaction)
                    .Where(b => b.CityId == cityId && b.Type == buildingType)
                    .FirstAsync(ct);
                if (row is null || row.Level >= targetLevel || row.Status != BuildingStatus.Upgrading)
                {
                    return ((BuildingEntity?)null, lockedCity);
                }

                if (row.FinishAt is { } finish && finish > DateTime.UtcNow.AddSeconds(2))
                {
                    _jobs.Schedule<CompleteInnerBuildingJob>(
                        job => job.Execute(cityId, buildingType, targetLevel),
                        UtcSchedule.At(finish));
                    return ((BuildingEntity?)null, lockedCity);
                }

                var now = DateTime.UtcNow;
                var previousLevel = row.Level;
                row.Level = targetLevel;
                row.Status = BuildingStatus.Idle;
                row.TargetLevel = null;
                row.FinishAt = null;
                row.UpdatedAt = now;
                if (OuterFieldCatalog.IsField(buildingType) && targetLevel >= 1)
                {
                    var def = OuterFieldCatalog.Find(buildingType);
                    var hall = await _orm.Select<BuildingEntity>()
                        .WithTransaction(transaction)
                        .Where(b => b.CityId == cityId && b.Type == TechBonuses.ResourceHall)
                        .FirstAsync(ct);
                    var prod = TechBonuses.ProductionPercent(hall?.Level ?? 0);
                    var buffs = await CityBuffStore.LoadAsync(_orm, cityId, ct, transaction);
                    var itemPercent = ItemCatalog.ResourceBoostOf(buffs, now);
                    var itemExpire = ItemCatalog.ResourceBoostExpireAt(buffs, now);
                    if (def is not null && row.LastCollectedAt is not null && previousLevel >= 1)
                    {
                        var pending = FieldProduction.Pending(
                            TechBonuses.ApplyPercent(def.RatePerHour(previousLevel), prod),
                            TechBonuses.ApplyPercent(def.FieldCap(previousLevel), prod),
                            row.LastCollectedAt,
                            now,
                            itemPercent,
                            itemExpire);
                        var newRate = TechBonuses.ApplyPercent(def.RatePerHour(targetLevel), prod);
                        var collectRate = itemExpire is { } until && until > now
                            ? TechBonuses.ApplyPercent(newRate, itemPercent)
                            : newRate;
                        row.LastCollectedAt = FieldProduction.AfterCollect(now, pending, collectRate);
                    }
                    else if (row.LastCollectedAt is null)
                    {
                        row.LastCollectedAt = now;
                    }
                }

                await _orm.Update<BuildingEntity>()
                    .WithTransaction(transaction)
                    .SetSource(row)
                    .ExecuteAffrowsAsync(ct);

                if (buildingType == TechBonuses.ResourceHall)
                {
                    var buffs = await CityBuffStore.LoadAsync(_orm, cityId, ct, transaction);
                    await RecalibrateFieldsAsync(
                        transaction,
                        cityId,
                        TechBonuses.ProductionPercent(previousLevel),
                        TechBonuses.ProductionPercent(targetLevel),
                        now,
                        buffs,
                        ct);
                }

                return (row, lockedCity);
            }, cancellationToken);
        }
        catch (BizException ex) when (ex.Code == ErrorCodes.NotFound)
        {
            return;
        }

        if (entity is null)
        {
            return;
        }

        var overview = await BuildOverviewAsync(city, cancellationToken);
        var payload = new BuildCompleteDto(
            city.Id,
            buildingType,
            entity.Level,
            overview.ServerTime,
            overview.Resources,
            overview.ResourceCap,
            overview.PopulationCap);

        await _hub.Clients.Group($"city:{cityId}")
            .SendAsync("BuildComplete", ApiResult.Ok(payload), cancellationToken);
    }

    public async Task RecoverDueAsync(CancellationToken cancellationToken)
    {
        var due = await _orm.Select<BuildingEntity>()
            .Where(b => b.Status == BuildingStatus.Upgrading && b.FinishAt != null && b.FinishAt <= DateTime.UtcNow)
            .ToListAsync(cancellationToken);

        foreach (var row in due)
        {
            if (row.TargetLevel is int target)
            {
                await CompleteAsync(row.CityId, row.Type, target, cancellationToken);
            }
        }
    }

    private async Task RecalibrateFieldsAsync(
        System.Data.Common.DbTransaction transaction,
        long cityId,
        int oldPercent,
        int newPercent,
        DateTime now,
        IReadOnlyList<SanguoGame.Core.Shop.ActiveBuff> buffs,
        CancellationToken cancellationToken)
    {
        if (oldPercent == newPercent)
        {
            return;
        }

        var rows = await _orm.Select<BuildingEntity>()
            .WithTransaction(transaction)
            .Where(b => b.CityId == cityId)
            .ToListAsync(cancellationToken);
        foreach (var field in rows)
        {
            var def = OuterFieldCatalog.Find(field.Type);
            if (def is null || field.Level < 1 || field.LastCollectedAt is null)
            {
                continue;
            }

            var itemPercent = ItemCatalog.ResourceBoostOf(buffs, now);
            var itemExpire = ItemCatalog.ResourceBoostExpireAt(buffs, now);
            var pending = FieldProduction.Pending(
                TechBonuses.ApplyPercent(def.RatePerHour(field.Level), oldPercent),
                TechBonuses.ApplyPercent(def.FieldCap(field.Level), oldPercent),
                field.LastCollectedAt,
                now,
                itemPercent,
                itemExpire);
            var newRate = TechBonuses.ApplyPercent(def.RatePerHour(field.Level), newPercent);
            var collectRate = itemExpire is { } until && until > now
                ? TechBonuses.ApplyPercent(newRate, itemPercent)
                : newRate;
            field.LastCollectedAt = FieldProduction.AfterCollect(now, pending, collectRate);
            field.UpdatedAt = now;
            await _orm.Update<BuildingEntity>()
                .WithTransaction(transaction)
                .SetSource(field)
                .UpdateColumns(b => new { b.LastCollectedAt, b.UpdatedAt })
                .ExecuteAffrowsAsync(cancellationToken);
        }
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

    private async Task<BuildingsOverviewDto> BuildOverviewAsync(CityEntity city, CancellationToken cancellationToken)
    {
        var rows = await _orm.Select<BuildingEntity>()
            .Where(b => b.CityId == city.Id)
            .ToListAsync(cancellationToken);
        var byType = rows.ToDictionary(b => b.Type, StringComparer.OrdinalIgnoreCase);
        var palaceLevel = byType.TryGetValue("palace", out var palace) ? palace.Level : 0;
        var academyLevel = byType.TryGetValue("academy", out var academy) ? academy.Level : 0;
        var houseLevel = byType.TryGetValue("house", out var house) ? house.Level : 0;
        var warehouseLevel = byType.TryGetValue("warehouse", out var warehouse) ? warehouse.Level : 0;
        var innerQueues = QueueSlots.Inner(rows);
        var buildUsed = QueueSlots.Used(rows, QueueKind.Build);
        var techUsed = QueueSlots.Used(rows, QueueKind.Tech);
        var stock = ToAmount(city);
        var now = DateTime.UtcNow;
        var buffs = await CityBuffStore.LoadAsync(_orm, city.Id, cancellationToken);

        var items = InnerBuildingCatalog.All.Select(def =>
        {
            byType.TryGetValue(def.Type, out var entity);
            var level = entity?.Level ?? 0;
            var status = entity?.Status ?? BuildingStatus.Idle;
            var nextLevel = level + 1;
            BuildingCostDto? next = null;
            string? blocked = null;
            var kind = QueueRules.OfBuilding(def.Type);
            var queueBusy = kind == QueueKind.Tech
                ? techUsed >= QueueSlots.Limit(city, QueueKind.Tech)
                : buildUsed >= QueueSlots.Limit(city, QueueKind.Build);

            if (level >= def.MaxLevel)
            {
                blocked = "maxLevel";
            }
            else
            {
                var cost = InnerBuildingCatalog.CostToReach(def, nextLevel);
                var speed = ItemCatalog.SpeedPercentOf(def.Type, buffs, now);
                next = new BuildingCostDto(
                    nextLevel,
                    ItemCatalog.ApplySpeed(InnerBuildingCatalog.DurationSeconds(def, nextLevel), speed),
                    new ResourceDto(cost.Grain, cost.Wood, cost.Iron, cost.Copper));

                if (status == BuildingStatus.Upgrading)
                {
                    blocked = null;
                }
                else if (queueBusy)
                {
                    blocked = "queue";
                }
                else if (palaceLevel < def.RequirePalaceLevel || academyLevel < def.RequireAcademyLevel)
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
                def.Category,
                level,
                def.MaxLevel,
                status,
                entity?.TargetLevel,
                entity?.FinishAt,
                EffectsOf(def.Type, level),
                next,
                blocked);
        }).ToList();

        return new BuildingsOverviewDto(
            city.Id,
            now,
            new ResourceDto(city.Grain, city.Wood, city.Iron, city.Copper),
            InnerBuildingCatalog.ResourceCap(warehouseLevel),
            InnerBuildingCatalog.PopulationCap(houseLevel),
            innerQueues.FirstOrDefault(),
            items,
            innerQueues,
            QueueSlots.State(city, QueueKind.Build, buildUsed),
            QueueSlots.State(city, QueueKind.Tech, techUsed));
    }

    private static ResourceAmount ToAmount(CityEntity city) =>
        new(city.Grain, city.Wood, city.Iron, city.Copper);

    private static IReadOnlyDictionary<string, int> EffectsOf(string type, int level)
    {
        if (level <= 0)
        {
            return new Dictionary<string, int>();
        }

        return type switch
        {
            "house" => new Dictionary<string, int> { ["populationCap"] = InnerBuildingCatalog.PopulationCap(level) },
            "warehouse" => new Dictionary<string, int> { ["resourceCap"] = InnerBuildingCatalog.ResourceCap(level) },
            "academy" => new Dictionary<string, int> { ["attackBonusPercent"] = TechBonuses.AcademyAttackPercent(level) },
            "drillHall" => new Dictionary<string, int>
            {
                ["troopPowerBonusPercent"] = TechBonuses.TroopPowerPercent(level),
                ["recruitDiscountPercent"] = TechBonuses.RecruitDiscountPercent(level)
            },
            "defenseHall" => new Dictionary<string, int>
            {
                ["wallDefenseFlat"] = TechBonuses.WallDefenseFlat(level),
                ["trapBonusPercent"] = (int)Math.Round(TechBonuses.TrapBonus(level) * 100)
            },
            "resourceHall" => new Dictionary<string, int>
            {
                ["productionBonusPercent"] = TechBonuses.ProductionPercent(level)
            },
            "barracks" => new Dictionary<string, int> { ["troopCap"] = InnerBuildingCatalog.TroopCap(level) },
            _ => new Dictionary<string, int>()
        };
    }
}
