using FreeSql;
using Hangfire;
using Microsoft.AspNetCore.SignalR;
using SanguoGame.Core;
using SanguoGame.Core.Buildings;
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

    public BuildingService(IFreeSql orm, IBackgroundJobClient jobs, IHubContext<GameHub> hub)
    {
        _orm = orm;
        _jobs = jobs;
        _hub = hub;
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

        var city = await RequireCityAsync(accountId, cancellationToken);
        var rows = await _orm.Select<BuildingEntity>()
            .Where(b => b.CityId == city.Id)
            .ToListAsync(cancellationToken);
        var byType = rows.ToDictionary(b => b.Type, StringComparer.OrdinalIgnoreCase);

        if (rows.Any(b => b.Status == BuildingStatus.Upgrading))
        {
            throw new BizException(ErrorCodes.BuildingQueueBusy, "本城正在建造或升级");
        }

        byType.TryGetValue(def.Type, out var entity);
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

        var targetLevel = level + 1;
        var cost = InnerBuildingCatalog.CostToReach(def, targetLevel);
        var stock = ToAmount(city);
        var missing = stock.FirstMissingAgainst(cost);
        if (missing is not null)
        {
            throw new BizException(ErrorCodes.InsufficientResources, $"资源不足（缺{missing}）");
        }

        var now = DateTime.UtcNow;
        var finishAt = now.AddSeconds(InnerBuildingCatalog.DurationSeconds(def, targetLevel));
        var remain = stock.Subtract(cost);
        city.Grain = remain.Grain;
        city.Wood = remain.Wood;
        city.Iron = remain.Iron;
        city.Copper = remain.Copper;

        if (entity is null)
        {
            entity = new BuildingEntity
            {
                CityId = city.Id,
                Type = def.Type,
                Level = 0,
                Status = BuildingStatus.Upgrading,
                TargetLevel = targetLevel,
                FinishAt = finishAt,
                UpdatedAt = now
            };
        }
        else
        {
            entity.Status = BuildingStatus.Upgrading;
            entity.TargetLevel = targetLevel;
            entity.FinishAt = finishAt;
            entity.UpdatedAt = now;
        }

        try
        {
            await _orm.Update<CityEntity>()
                .SetSource(city)
                .UpdateColumns(c => new { c.Grain, c.Wood, c.Iron, c.Copper })
                .ExecuteAffrowsAsync(cancellationToken);

            if (entity.Id == 0)
            {
                entity.Id = await _orm.Insert(entity).ExecuteIdentityAsync(cancellationToken);
            }
            else
            {
                await _orm.Update<BuildingEntity>().SetSource(entity).ExecuteAffrowsAsync(cancellationToken);
            }
        }
        catch (Exception ex) when (DbErrors.IsUniqueViolation(ex))
        {
            throw new BizException(ErrorCodes.BuildingQueueBusy, "本城正在建造或升级");
        }

        _jobs.Schedule<CompleteInnerBuildingJob>(
            job => job.Execute(city.Id, def.Type, targetLevel),
            new DateTimeOffset(DateTime.SpecifyKind(finishAt, DateTimeKind.Utc)));

        return await BuildOverviewAsync(city, cancellationToken);
    }

    public async Task CompleteAsync(long cityId, string buildingType, int targetLevel, CancellationToken cancellationToken)
    {
        var entity = await _orm.Select<BuildingEntity>()
            .Where(b => b.CityId == cityId && b.Type == buildingType)
            .FirstAsync(cancellationToken);
        if (entity is null)
        {
            return;
        }

        if (entity.Level >= targetLevel || entity.Status != BuildingStatus.Upgrading)
        {
            return;
        }

        entity.Level = targetLevel;
        entity.Status = BuildingStatus.Idle;
        entity.TargetLevel = null;
        entity.FinishAt = null;
        entity.UpdatedAt = DateTime.UtcNow;
        await _orm.Update<BuildingEntity>().SetSource(entity).ExecuteAffrowsAsync(cancellationToken);

        var city = await _orm.Select<CityEntity>().Where(c => c.Id == cityId).FirstAsync(cancellationToken);
        if (city is null)
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
        var houseLevel = byType.TryGetValue("house", out var house) ? house.Level : 0;
        var warehouseLevel = byType.TryGetValue("warehouse", out var warehouse) ? warehouse.Level : 0;
        var queueRow = rows.FirstOrDefault(b => b.Status == BuildingStatus.Upgrading);
        var stock = ToAmount(city);
        var queueBusy = queueRow is not null;

        BuildingQueueDto? queue = null;
        if (queueRow is { TargetLevel: int qLevel, FinishAt: { } qFinish })
        {
            queue = new BuildingQueueDto(queueRow.Type, qLevel, qFinish);
        }

        var items = InnerBuildingCatalog.All.Select(def =>
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
                var cost = InnerBuildingCatalog.CostToReach(def, nextLevel);
                next = new BuildingCostDto(
                    nextLevel,
                    InnerBuildingCatalog.DurationSeconds(def, nextLevel),
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
            DateTime.UtcNow,
            new ResourceDto(city.Grain, city.Wood, city.Iron, city.Copper),
            InnerBuildingCatalog.ResourceCap(warehouseLevel),
            InnerBuildingCatalog.PopulationCap(houseLevel),
            queue,
            items);
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
            "academy" => new Dictionary<string, int> { ["researchSpeedBonus"] = 0 },
            _ => new Dictionary<string, int>()
        };
    }
}
