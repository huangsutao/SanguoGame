using FreeSql;
using Hangfire;
using Microsoft.AspNetCore.SignalR;
using SanguoGame.Core;
using SanguoGame.Core.Army;
using SanguoGame.Core.Buildings;
using SanguoGame.Core.Daily;
using SanguoGame.Core.Shop;
using SanguoGame.Infrastructure.Entities;
using SanguoGame.Server.Contracts;
using SanguoGame.Server.Hubs;
using SanguoGame.Server.Jobs;

namespace SanguoGame.Server.Services;

public sealed class ArmyService
{
    private readonly IFreeSql _orm;
    private readonly DailyService _daily;
    private readonly IBackgroundJobClient _jobs;
    private readonly IHubContext<GameHub> _hub;

    public ArmyService(IFreeSql orm, DailyService daily, IBackgroundJobClient jobs, IHubContext<GameHub> hub)
    {
        _orm = orm;
        _daily = daily;
        _jobs = jobs;
        _hub = hub;
    }

    public async Task<ArmyOverviewDto> GetOverviewAsync(long accountId, CancellationToken cancellationToken)
    {
        var city = await RequireCityAsync(accountId, cancellationToken);
        return await BuildOverviewAsync(city, cancellationToken);
    }

    public async Task<ArmyOverviewDto> RecruitAsync(
        long accountId,
        string troopType,
        int count,
        CancellationToken cancellationToken)
    {
        var def = TroopCatalog.Find(troopType)
            ?? throw new BizException(ErrorCodes.ValidationFailed, "未知兵种");
        if (count is < 1 or > 100)
        {
            throw new BizException(ErrorCodes.ValidationFailed, "征兵数量为 1～100");
        }

        var city = await RequireCityAsync(accountId, cancellationToken);
        var planned = await CityRowLock.RunAsync(_orm, city.Id, async (transaction, locked, ct) =>
        {
            var rows = await _orm.Select<BuildingEntity>()
                .WithTransaction(transaction)
                .Where(b => b.CityId == locked.Id)
                .ToListAsync(ct);
            var barracksLevel = CityStats.BuildingLevel(rows, "barracks");
            if (barracksLevel < def.RequireBarracksLevel)
            {
                throw new BizException(ErrorCodes.BarracksRequired, $"需要兵营 {def.RequireBarracksLevel} 级");
            }

            var recruits = await LoadRecruitsAsync(transaction, locked.Id, ct);
            if (recruits.Count >= QueueSlots.Limit(locked, QueueKind.Recruit))
            {
                throw new BizException(ErrorCodes.RecruitQueueBusy, "征兵队列已满");
            }

            var marching = await MarchingTroopsAsync(transaction, locked.Id, ct);
            var stationed = CityStats.Troops(locked);
            var queued = recruits.Sum(r => r.Count);
            var cap = InnerBuildingCatalog.TroopCap(barracksLevel);
            if (stationed.Total + marching.Total + queued + count > cap)
            {
                throw new BizException(ErrorCodes.TroopCapExceeded, "超出带兵上限");
            }

            var drillHallLevel = CityStats.BuildingLevel(rows, TechBonuses.DrillHall);
            var cost = TechBonuses.Discount(
                TroopCatalog.Cost(def, count),
                TechBonuses.RecruitDiscountPercent(drillHallLevel));
            var stock = CityStats.Stock(locked);
            var missing = stock.FirstMissingAgainst(cost);
            if (missing is not null)
            {
                throw new BizException(ErrorCodes.InsufficientResources, $"资源不足（缺{missing}）");
            }

            CityStats.ApplyStock(locked, stock.Subtract(cost));
            var now = DateTime.UtcNow;
            var buffs = await CityBuffStore.LoadAsync(_orm, locked.Id, ct, transaction);
            var speed = ItemCatalog.RecruitSpeedPercent(buffs, now);
            var finish = now.AddSeconds(RecruitTiming.DurationSeconds(def.Type, count, speed));
            var row = new RecruitEntity
            {
                CityId = locked.Id,
                TroopType = def.Type,
                Count = count,
                FinishAt = finish
            };
            row.Id = await _orm.Insert(row).WithTransaction(transaction).ExecuteIdentityAsync(ct);
            await UpdateCityAsync(transaction, locked, ct);
            city.Grain = locked.Grain;
            city.Wood = locked.Wood;
            city.Iron = locked.Iron;
            city.Copper = locked.Copper;
            city.Infantry = locked.Infantry;
            city.Archer = locked.Archer;
            city.Cavalry = locked.Cavalry;
            city.ExtraRecruitSlots = locked.ExtraRecruitSlots;
            return row;
        }, cancellationToken);

        _jobs.Schedule<CompleteRecruitJob>(
            job => job.Execute(city.Id, planned.Id),
            UtcSchedule.At(planned.FinishAt));
        await _daily.AddProgressAsync(city.Id, DailyCatalog.Recruit, count, cancellationToken);
        return await BuildOverviewAsync(city, cancellationToken);
    }

    public Task CompleteRecruitAsync(long cityId, string troopType, int count, CancellationToken cancellationToken) =>
        CompleteRecruitCoreAsync(cityId, null, troopType, count, cancellationToken);

    public Task CompleteRecruitAsync(long cityId, long recruitId, CancellationToken cancellationToken) =>
        CompleteRecruitCoreAsync(cityId, recruitId, null, 0, cancellationToken);

    private async Task CompleteRecruitCoreAsync(
        long cityId,
        long? recruitId,
        string? troopType,
        int count,
        CancellationToken cancellationToken)
    {
        CityEntity? city;
        string? completedType = null;
        var completedCount = 0;
        try
        {
            city = await CityRowLock.RunAsync(_orm, cityId, async (transaction, locked, ct) =>
            {
                RecruitEntity? row;
                if (recruitId is long id)
                {
                    row = await _orm.Select<RecruitEntity>()
                        .WithTransaction(transaction)
                        .Where(r => r.CityId == cityId && r.Id == id)
                        .FirstAsync(ct);
                }
                else
                {
                    row = await _orm.Select<RecruitEntity>()
                        .WithTransaction(transaction)
                        .Where(r =>
                            r.CityId == cityId
                            && r.TroopType == troopType
                            && r.Count == count)
                        .OrderBy(r => r.FinishAt)
                        .FirstAsync(ct);
                }

                if (row is null)
                {
                    return locked;
                }

                if (row.FinishAt > DateTime.UtcNow.AddSeconds(2))
                {
                    var pendingId = row.Id;
                    var pendingFinish = row.FinishAt;
                    _jobs.Schedule<CompleteRecruitJob>(
                        job => job.Execute(cityId, pendingId),
                        UtcSchedule.At(pendingFinish));
                    return locked;
                }

                var rows = await _orm.Select<BuildingEntity>()
                    .WithTransaction(transaction)
                    .Where(b => b.CityId == locked.Id)
                    .ToListAsync(ct);
                var barracksLevel = CityStats.BuildingLevel(rows, "barracks");
                var cap = InnerBuildingCatalog.TroopCap(barracksLevel);
                var marching = await MarchingTroopsAsync(transaction, locked.Id, ct);
                var others = await LoadRecruitsAsync(transaction, locked.Id, ct);
                var otherQueued = others.Where(r => r.Id != row.Id).Sum(r => r.Count);
                var stationed = CityStats.Troops(locked);
                var room = Math.Max(0, cap - marching.Total - otherQueued);
                CityStats.ApplyTroops(
                    locked,
                    CityStats.FitCap(stationed.Add(row.TroopType, row.Count), room));
                completedType = row.TroopType;
                completedCount = row.Count;
                await _orm.Delete<RecruitEntity>()
                    .WithTransaction(transaction)
                    .Where(r => r.Id == row.Id)
                    .ExecuteAffrowsAsync(ct);
                await UpdateCityAsync(transaction, locked, ct);
                return locked;
            }, cancellationToken);
        }
        catch (BizException ex) when (ex.Code == ErrorCodes.NotFound)
        {
            return;
        }

        if (completedType is null || city is null)
        {
            return;
        }

        var overview = await BuildOverviewAsync(city, cancellationToken);
        var payload = new RecruitCompleteDto(
            city.Id,
            completedType,
            completedCount,
            overview.ServerTime,
            overview.Troops);
        await _hub.Clients.Group($"city:{cityId}")
            .SendAsync("RecruitComplete", ApiResult.Ok(payload), cancellationToken);
    }

    public async Task RecoverDueAsync(CancellationToken cancellationToken)
    {
        var due = await _orm.Select<RecruitEntity>()
            .Where(r => r.FinishAt <= DateTime.UtcNow)
            .ToListAsync(cancellationToken);
        foreach (var row in due)
        {
            await CompleteRecruitAsync(row.CityId, row.Id, cancellationToken);
        }
    }

    internal async Task<ArmyOverviewDto> BuildOverviewAsync(CityEntity city, CancellationToken cancellationToken)
    {
        var rows = await _orm.Select<BuildingEntity>()
            .Where(b => b.CityId == city.Id)
            .ToListAsync(cancellationToken);
        var barracksLevel = CityStats.BuildingLevel(rows, "barracks");
        var warehouseLevel = CityStats.BuildingLevel(rows, "warehouse");
        var drillHallLevel = CityStats.BuildingLevel(rows, TechBonuses.DrillHall);
        var defenseHallLevel = CityStats.BuildingLevel(rows, TechBonuses.DefenseHall);
        var discountPercent = TechBonuses.RecruitDiscountPercent(drillHallLevel);
        var levels = CityStats.WallLevels(rows);
        var marches = await _orm.Select<MarchEntity>()
            .Where(m => m.FromCityId == city.Id && m.Status == MarchStatus.Marching)
            .OrderBy(m => m.ArriveAt)
            .ToListAsync(cancellationToken);
        var recruits = await _orm.Select<RecruitEntity>()
            .Where(r => r.CityId == city.Id)
            .OrderBy(r => r.FinishAt)
            .ToListAsync(cancellationToken);
        var queues = recruits
            .Select(r => new RecruitQueueDto(r.TroopType, r.Count, r.FinishAt))
            .ToList();

        return new ArmyOverviewDto(
            city.Id,
            DateTime.UtcNow,
            new ResourceDto(city.Grain, city.Wood, city.Iron, city.Copper),
            InnerBuildingCatalog.ResourceCap(warehouseLevel),
            new TroopDto(city.Infantry, city.Archer, city.Cavalry),
            InnerBuildingCatalog.TroopCap(barracksLevel),
            barracksLevel,
            WallCatalog.WallDefense(levels, defenseHallLevel),
            city.ProtectionUntil,
            marches.Select(m => MapMarch(m, true)).ToList(),
            TroopCatalog.All.Select(def =>
            {
                var unit = TechBonuses.Discount(def.UnitCost, discountPercent);
                return new TroopTypeDto(
                    def.Type,
                    def.Name,
                    def.RequireBarracksLevel,
                    new ResourceDto(unit.Grain, unit.Wood, unit.Iron, unit.Copper));
            }).ToList(),
            TechBonuses.TroopPowerPercent(drillHallLevel),
            discountPercent,
            queues.FirstOrDefault(),
            queues,
            QueueSlots.State(city, QueueKind.Recruit, queues.Count));
    }

    internal static MarchDto MapMarch(MarchEntity march, bool mine, bool includeTroops = true) =>
        new(
            march.Id,
            march.TargetType,
            march.TargetId,
            march.FromX,
            march.FromY,
            march.ToX,
            march.ToY,
            includeTroops ? new TroopDto(march.Infantry, march.Archer, march.Cavalry) : null,
            march.DepartAt,
            march.ArriveAt,
            march.Status,
            mine,
            march.Kind);

    internal async Task<CityEntity> RequireCityAsync(long accountId, CancellationToken cancellationToken)
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

    private async Task<TroopCount> MarchingTroopsAsync(
        System.Data.Common.DbTransaction transaction,
        long cityId,
        CancellationToken cancellationToken)
    {
        var rows = await _orm.Select<MarchEntity>()
            .WithTransaction(transaction)
            .Where(m => m.FromCityId == cityId && m.Status == MarchStatus.Marching)
            .ToListAsync(cancellationToken);
        return rows.Aggregate(TroopCount.Zero, (sum, row) =>
            sum.Add(new TroopCount(row.Infantry, row.Archer, row.Cavalry)));
    }

    private Task<List<RecruitEntity>> LoadRecruitsAsync(
        System.Data.Common.DbTransaction transaction,
        long cityId,
        CancellationToken cancellationToken) =>
        _orm.Select<RecruitEntity>()
            .WithTransaction(transaction)
            .Where(r => r.CityId == cityId)
            .ToListAsync(cancellationToken);

    private Task<int> UpdateCityAsync(
        System.Data.Common.DbTransaction transaction,
        CityEntity city,
        CancellationToken cancellationToken) =>
        _orm.Update<CityEntity>()
            .WithTransaction(transaction)
            .SetSource(city)
            .UpdateColumns(c => new
            {
                c.Grain,
                c.Wood,
                c.Iron,
                c.Copper,
                c.Infantry,
                c.Archer,
                c.Cavalry
            })
            .ExecuteAffrowsAsync(cancellationToken);
}
