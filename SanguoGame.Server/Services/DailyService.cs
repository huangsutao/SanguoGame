using System.Data.Common;
using FreeSql;
using SanguoGame.Core;
using SanguoGame.Core.Buildings;
using SanguoGame.Core.Daily;
using SanguoGame.Infrastructure;
using SanguoGame.Infrastructure.Entities;
using SanguoGame.Server.Contracts;

namespace SanguoGame.Server.Services;

public sealed class DailyService
{
    private readonly IFreeSql _orm;

    public DailyService(IFreeSql orm)
    {
        _orm = orm;
    }

    public async Task<DailyOverviewDto> GetOverviewAsync(long accountId, CancellationToken cancellationToken)
    {
        var city = await RequireCityAsync(accountId, cancellationToken);
        await EnsureDayAsync(city.Id, DateTime.UtcNow, cancellationToken);
        return await BuildOverviewAsync(city, cancellationToken);
    }

    public async Task<DailyOverviewDto> ClaimAsync(long accountId, string missionType, CancellationToken cancellationToken)
    {
        var def = DailyCatalog.Find(missionType)
            ?? throw new BizException(ErrorCodes.ValidationFailed, "未知军务");
        var city = await RequireCityAsync(accountId, cancellationToken);
        await CityRowLock.RunAsync(_orm, city.Id, async (transaction, locked, ct) =>
        {
            var now = DateTime.UtcNow;
            var day = DailyCatalog.DayKey(now);
            await EnsureDayAsync(locked.Id, now, ct, transaction);
            var rows = await LoadDayAsync(transaction, locked.Id, day, ct);
            var target = rows.FirstOrDefault(r => r.Type.Equals(def.Type, StringComparison.OrdinalIgnoreCase))
                ?? throw new BizException(ErrorCodes.NotFound, "军务不存在");
            if (target.Claimed)
            {
                throw new BizException(ErrorCodes.DailyNotClaimable, "军务已领取");
            }

            var progress = ProgressOf(def, rows);
            if (progress < def.Required)
            {
                throw new BizException(ErrorCodes.DailyNotClaimable, "军务尚未完成");
            }

            var buildings = await _orm.Select<BuildingEntity>()
                .WithTransaction(transaction)
                .Where(b => b.CityId == locked.Id)
                .ToListAsync(ct);
            var warehouse = buildings.FirstOrDefault(b => b.Type == "warehouse")?.Level ?? 0;
            var cap = InnerBuildingCatalog.ResourceCap(warehouse);
            Deposit(locked, def.Reward, cap);
            target.Claimed = true;
            await _orm.Update<DailyQuestEntity>()
                .WithTransaction(transaction)
                .SetSource(target)
                .UpdateColumns(q => q.Claimed)
                .ExecuteAffrowsAsync(ct);
            await _orm.Update<CityEntity>()
                .WithTransaction(transaction)
                .SetSource(locked)
                .UpdateColumns(c => new { c.Grain, c.Wood, c.Iron, c.Copper })
                .ExecuteAffrowsAsync(ct);
            city.Grain = locked.Grain;
            city.Wood = locked.Wood;
            city.Iron = locked.Iron;
            city.Copper = locked.Copper;
            return 0;
        }, cancellationToken);

        return await BuildOverviewAsync(city, cancellationToken);
    }

    public Task AddProgressAsync(long cityId, string missionType, int delta, CancellationToken cancellationToken) =>
        AddProgressAsync(cityId, missionType, delta, cancellationToken, transaction: null);

    public async Task AddProgressAsync(
        long cityId,
        string missionType,
        int delta,
        CancellationToken cancellationToken,
        DbTransaction? transaction)
    {
        if (delta <= 0 || DailyCatalog.IsBundle(missionType) || DailyCatalog.Find(missionType) is null)
        {
            return;
        }

        var now = DateTime.UtcNow;
        var day = DailyCatalog.DayKey(now);
        await EnsureDayAsync(cityId, now, cancellationToken, transaction);
        var update = _orm.Update<DailyQuestEntity>()
            .Where(q => q.CityId == cityId && q.Day == day && q.Type == missionType && !q.Claimed)
            .Set(q => q.Progress + delta);
        if (transaction is not null)
        {
            update = update.WithTransaction(transaction);
        }

        await update.ExecuteAffrowsAsync(cancellationToken);
        var cap = DailyCatalog.Require(missionType).Required;
        var clamp = _orm.Update<DailyQuestEntity>()
            .Where(q => q.CityId == cityId && q.Day == day && q.Type == missionType && q.Progress > cap)
            .Set(q => q.Progress, cap);
        if (transaction is not null)
        {
            clamp = clamp.WithTransaction(transaction);
        }

        await clamp.ExecuteAffrowsAsync(cancellationToken);
    }

    private async Task<CityEntity> RequireCityAsync(long accountId, CancellationToken cancellationToken)
    {
        var character = await _orm.Select<CharacterEntity>()
            .Where(c => c.AccountId == accountId)
            .FirstAsync(cancellationToken)
            ?? throw new BizException(ErrorCodes.NotFound, "尚未创建角色");
        return await _orm.Select<CityEntity>()
            .Where(c => c.CharacterId == character.Id)
            .FirstAsync(cancellationToken)
            ?? throw new BizException(ErrorCodes.NotFound, "尚未建立主城");
    }

    private async Task<DailyOverviewDto> BuildOverviewAsync(CityEntity city, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var day = DailyCatalog.DayKey(now);
        var rows = await _orm.Select<DailyQuestEntity>()
            .Where(q => q.CityId == city.Id && q.Day == day)
            .ToListAsync(cancellationToken);
        var buildings = await _orm.Select<BuildingEntity>()
            .Where(b => b.CityId == city.Id)
            .ToListAsync(cancellationToken);
        var warehouse = buildings.FirstOrDefault(b => b.Type == "warehouse")?.Level ?? 0;
        var missions = DailyCatalog.All.Select(def =>
        {
            var row = rows.FirstOrDefault(r => r.Type.Equals(def.Type, StringComparison.OrdinalIgnoreCase));
            var progress = ProgressOf(def, rows);
            return new DailyMissionDto(
                def.Type,
                def.Name,
                def.Detail,
                progress,
                def.Required,
                row?.Claimed ?? false,
                new ResourceDto(def.Reward.Grain, def.Reward.Wood, def.Reward.Iron, def.Reward.Copper));
        }).ToList();

        return new DailyOverviewDto(
            now,
            day,
            new ResourceDto(city.Grain, city.Wood, city.Iron, city.Copper),
            InnerBuildingCatalog.ResourceCap(warehouse),
            missions);
    }

    private static int ProgressOf(DailyMissionDef def, IReadOnlyList<DailyQuestEntity> rows)
    {
        if (DailyCatalog.IsBundle(def.Type))
        {
            return DailyCatalog.All.Count(item =>
                !DailyCatalog.IsBundle(item.Type)
                && (rows.FirstOrDefault(r => r.Type == item.Type)?.Progress ?? 0) >= item.Required);
        }

        var row = rows.FirstOrDefault(r => r.Type.Equals(def.Type, StringComparison.OrdinalIgnoreCase));
        return Math.Min(def.Required, Math.Max(0, row?.Progress ?? 0));
    }

    private async Task EnsureDayAsync(
        long cityId,
        DateTime now,
        CancellationToken cancellationToken,
        DbTransaction? transaction = null)
    {
        var day = DailyCatalog.DayKey(now);
        foreach (var def in DailyCatalog.All)
        {
            var select = _orm.Select<DailyQuestEntity>()
                .Where(q => q.CityId == cityId && q.Day == day && q.Type == def.Type);
            if (transaction is not null)
            {
                select = select.WithTransaction(transaction);
            }

            if (await select.AnyAsync(cancellationToken))
            {
                continue;
            }

            var insert = _orm.Insert(new DailyQuestEntity
            {
                CityId = cityId,
                Day = day,
                Type = def.Type,
                Progress = 0
            });
            if (transaction is not null)
            {
                insert = insert.WithTransaction(transaction);
            }

            try
            {
                await insert.ExecuteAffrowsAsync(cancellationToken);
            }
            catch (Exception ex) when (DbErrors.IsUniqueViolation(ex))
            {
                // 并发插入同一天同一条时忽略
            }
        }
    }

    private async Task<List<DailyQuestEntity>> LoadDayAsync(
        DbTransaction transaction,
        long cityId,
        DateTime day,
        CancellationToken cancellationToken) =>
        await _orm.Select<DailyQuestEntity>()
            .WithTransaction(transaction)
            .Where(q => q.CityId == cityId && q.Day == day)
            .ToListAsync(cancellationToken);

    private static void Deposit(CityEntity city, ResourceAmount loot, int cap)
    {
        city.Grain = Math.Min(cap, city.Grain + loot.Grain);
        city.Wood = Math.Min(cap, city.Wood + loot.Wood);
        city.Iron = Math.Min(cap, city.Iron + loot.Iron);
        city.Copper = Math.Min(cap, city.Copper + loot.Copper);
    }
}
