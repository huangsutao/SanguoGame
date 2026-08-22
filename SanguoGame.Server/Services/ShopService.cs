using System.Data.Common;
using FreeSql;
using Hangfire;
using Microsoft.Extensions.Options;
using SanguoGame.Core;
using SanguoGame.Core.Army;
using SanguoGame.Core.Buildings;
using SanguoGame.Core.Shop;
using SanguoGame.Core.World;
using SanguoGame.Core.Market;
using SanguoGame.Infrastructure;
using SanguoGame.Infrastructure.Entities;
using SanguoGame.Server.Contracts;
using SanguoGame.Server.Jobs;

namespace SanguoGame.Server.Services;

public sealed class ShopService
{
    private const int RelocateRetries = 8;

    private readonly IFreeSql _orm;
    private readonly IBackgroundJobClient _jobs;
    private readonly WorldMapOptions _map;

    public ShopService(IFreeSql orm, IBackgroundJobClient jobs, IOptions<WorldMapOptions> map)
    {
        _orm = orm;
        _jobs = jobs;
        _map = map.Value;
    }

    public async Task<ShopOverviewDto> GetOverviewAsync(long accountId, CancellationToken cancellationToken)
    {
        var city = await RequireCityAsync(accountId, cancellationToken);
        return await BuildOverviewAsync(city, cancellationToken);
    }

    public async Task<ShopOverviewDto> BuyAsync(long accountId, ShopBuyRequest request, CancellationToken cancellationToken)
    {
        var def = ItemCatalog.Find(request.ItemType)
            ?? throw new BizException(ErrorCodes.ValidationFailed, "未知道具");
        if (!ItemCatalog.TryBuyCost(def.Price, request.Count, out var total))
        {
            throw new BizException(ErrorCodes.ValidationFailed, "购买数量为 1～99");
        }

        var city = await RequireCityAsync(accountId, cancellationToken);
        await CityRowLock.RunAsync(_orm, city.Id, async (transaction, locked, ct) =>
        {
            if (locked.Yuanbao < total)
            {
                throw new BizException(ErrorCodes.InsufficientYuanbao, "元宝不足");
            }

            locked.Yuanbao -= total;
            await _orm.Update<CityEntity>()
                .WithTransaction(transaction)
                .SetSource(locked)
                .UpdateColumns(c => c.Yuanbao)
                .ExecuteAffrowsAsync(ct);
            await AddItemAsync(transaction, locked.Id, def.Type, request.Count, ct);
            city.Yuanbao = locked.Yuanbao;
            return 0;
        }, cancellationToken);

        return await BuildOverviewAsync(city, cancellationToken);
    }

    public async Task<ShopOverviewDto> UseAsync(long accountId, ShopUseRequest request, CancellationToken cancellationToken)
    {
        var def = ItemCatalog.Find(request.ItemType)
            ?? throw new BizException(ErrorCodes.ValidationFailed, "未知道具");
        if (request.Count is < 1 or > ItemCatalog.MaxBuyCount)
        {
            throw new BizException(ErrorCodes.ValidationFailed, "使用数量为 1～99");
        }

        if (def.Kind == ItemKind.Unlock)
        {
            if (request.Count != 1)
            {
                throw new BizException(ErrorCodes.ValidationFailed, "队列令一次只能使用 1 张");
            }

            return await ExpandQueueAsync(accountId, def, cancellationToken);
        }

        if (def.Kind == ItemKind.Consumable)
        {
            if (request.Count != 1)
            {
                throw new BizException(ErrorCodes.ValidationFailed, "迁城令一次只能使用 1 张");
            }

            return await RelocateAsync(accountId, def, request.X, request.Y, cancellationToken);
        }

        var city = await RequireCityAsync(accountId, cancellationToken);
        var planned = await CityRowLock.RunAsync(_orm, city.Id, async (transaction, locked, ct) =>
        {
            await ConsumeItemAsync(transaction, locked.Id, def.Type, request.Count, ct);
            var now = DateTime.UtcNow;
            var buffs = await CityBuffStore.LoadAsync(_orm, locked.Id, ct, transaction);
            var oldPercent = ItemCatalog.ActivePercent(buffs, def.Type, now);
            var current = buffs.FirstOrDefault(b => b.Type.Equals(def.Type, StringComparison.OrdinalIgnoreCase));
            var expire = ItemCatalog.StackExpireAt(now, current?.ExpireAt, def.DurationHours, request.Count);
            await UpsertBuffAsync(transaction, locked.Id, def.Type, expire, ct);

            BuildingEntity[] buildings = [];
            RecruitEntity[] recruits = [];
            if (def.Type is ItemCatalog.SpeedBuild or ItemCatalog.SpeedUpgrade or ItemCatalog.SpeedTech)
            {
                buildings = await ShortenBuildingsAsync(transaction, locked.Id, def.Type, now, oldPercent, def.SpeedPercent, ct);
            }
            else if (def.Type == ItemCatalog.SpeedRecruit)
            {
                recruits = await ShortenRecruitsAsync(transaction, locked.Id, now, oldPercent, def.SpeedPercent, ct);
            }
            else if (def.Type == ItemCatalog.ResourceBoost && oldPercent <= 0)
            {
                await RecalibrateFieldsAsync(transaction, locked.Id, now, 0, def.SpeedPercent, ct);
            }

            city.Yuanbao = locked.Yuanbao;
            city.X = locked.X;
            city.Y = locked.Y;
            city.ProtectionUntil = locked.ProtectionUntil;
            city.ExtraBuildSlots = locked.ExtraBuildSlots;
            city.ExtraFieldSlots = locked.ExtraFieldSlots;
            city.ExtraTechSlots = locked.ExtraTechSlots;
            city.ExtraRecruitSlots = locked.ExtraRecruitSlots;
            return (Buildings: buildings, Recruits: recruits);
        }, cancellationToken);

        foreach (var building in planned.Buildings)
        {
            if (building.TargetLevel is int target && building.FinishAt is { } finishAt)
            {
                var buildingType = building.Type;
                _jobs.Schedule<CompleteInnerBuildingJob>(
                    job => job.Execute(city.Id, buildingType, target),
                    UtcSchedule.At(finishAt));
            }
        }

        foreach (var recruit in planned.Recruits)
        {
            var recruitId = recruit.Id;
            _jobs.Schedule<CompleteRecruitJob>(
                job => job.Execute(city.Id, recruitId),
                UtcSchedule.At(recruit.FinishAt));
        }

        return await BuildOverviewAsync(city, cancellationToken);
    }

    private async Task<ShopOverviewDto> ExpandQueueAsync(
        long accountId,
        ItemDef def,
        CancellationToken cancellationToken)
    {
        var kind = ItemCatalog.QueueKindOf(def.Type)
            ?? throw new BizException(ErrorCodes.ValidationFailed, "未知道具");
        var city = await RequireCityAsync(accountId, cancellationToken);
        await CityRowLock.RunAsync(_orm, city.Id, async (transaction, locked, ct) =>
        {
            var extra = QueueSlots.Extra(locked, kind);
            if (extra >= QueueRules.MaxExtra)
            {
                throw new BizException(ErrorCodes.QueueSlotMaxed, "该队列已额外扩充");
            }

            await ConsumeItemAsync(transaction, locked.Id, def.Type, 1, ct);
            QueueSlots.SetExtra(locked, kind, extra + 1);
            await _orm.Update<CityEntity>()
                .WithTransaction(transaction)
                .SetSource(locked)
                .UpdateColumns(c => new
                {
                    c.ExtraBuildSlots,
                    c.ExtraFieldSlots,
                    c.ExtraTechSlots,
                    c.ExtraRecruitSlots
                })
                .ExecuteAffrowsAsync(ct);
            city.ExtraBuildSlots = locked.ExtraBuildSlots;
            city.ExtraFieldSlots = locked.ExtraFieldSlots;
            city.ExtraTechSlots = locked.ExtraTechSlots;
            city.ExtraRecruitSlots = locked.ExtraRecruitSlots;
            city.Yuanbao = locked.Yuanbao;
            return 0;
        }, cancellationToken);

        return await BuildOverviewAsync(city, cancellationToken);
    }

    private async Task<ShopOverviewDto> RelocateAsync(
        long accountId,
        ItemDef def,
        int? x,
        int? y,
        CancellationToken cancellationToken)
    {
        var city = await RequireCityAsync(accountId, cancellationToken);
        for (var round = 0; round < RelocateRetries; round++)
        {
            try
            {
                var moved = await CityRowLock.RunAsync(_orm, city.Id, async (transaction, locked, ct) =>
                {
                    await EnsureCanRelocateAsync(transaction, locked.Id, ct);
                    var cell = await ResolveRelocateCellAsync(transaction, locked, def, x, y, ct);
                    if (!await WorldOccupancy.TryClaimAsync(
                            _orm, cell.X, cell.Y, MapCellKinds.City, locked.Id, ct, transaction))
                    {
                        return false;
                    }

                    await ConsumeItemAsync(transaction, locked.Id, def.Type, 1, ct);
                    var fromX = locked.X;
                    var fromY = locked.Y;
                    locked.X = cell.X;
                    locked.Y = cell.Y;
                    locked.ProtectionUntil = DateTime.UtcNow.AddSeconds(_map.ProtectionSeconds);
                    await _orm.Update<CityEntity>()
                        .WithTransaction(transaction)
                        .SetSource(locked)
                        .UpdateColumns(c => new { c.X, c.Y, c.ProtectionUntil })
                        .ExecuteAffrowsAsync(ct);
                    await WorldOccupancy.ReleaseAsync(_orm, fromX, fromY, ct, transaction);
                    city.X = locked.X;
                    city.Y = locked.Y;
                    city.ProtectionUntil = locked.ProtectionUntil;
                    city.Yuanbao = locked.Yuanbao;
                    return true;
                }, cancellationToken);
                if (!moved)
                {
                    if (def.Type == ItemCatalog.RelocateRandom)
                    {
                        continue;
                    }

                    throw new BizException(ErrorCodes.InvalidRelocateTarget, "目标格已被占用");
                }

                return await BuildOverviewAsync(city, cancellationToken);
            }
            catch (Exception ex) when (DbErrors.IsUniqueViolation(ex) && def.Type == ItemCatalog.RelocateRandom)
            {
                // 随机格被并发占走，再抽一次
            }
            catch (Exception ex) when (DbErrors.IsUniqueViolation(ex))
            {
                throw new BizException(ErrorCodes.InvalidRelocateTarget, "目标格已被占用");
            }
        }

        throw new BizException(ErrorCodes.MapFull, "暂无空地可迁城");
    }

    private async Task<(int X, int Y)> ResolveRelocateCellAsync(
        DbTransaction transaction,
        CityEntity city,
        ItemDef def,
        int? x,
        int? y,
        CancellationToken cancellationToken)
    {
        if (def.Type == ItemCatalog.RelocateTarget)
        {
            if (x is null || y is null)
            {
                throw new BizException(ErrorCodes.ValidationFailed, "高级迁城令需要指定坐标");
            }

            if (x.Value < 0 || y.Value < 0 || x.Value >= _map.Width || y.Value >= _map.Height)
            {
                throw new BizException(ErrorCodes.InvalidRelocateTarget, "坐标超出地图范围");
            }

            if (x.Value == city.X && y.Value == city.Y)
            {
                throw new BizException(ErrorCodes.InvalidRelocateTarget, "不能迁到当前坐标");
            }

            if (await WorldOccupancy.IsOccupiedAsync(_orm, x.Value, y.Value, cancellationToken, transaction, city.Id))
            {
                throw new BizException(ErrorCodes.InvalidRelocateTarget, "目标格已被占用");
            }

            return (x.Value, y.Value);
        }

        var cell = await MapPlacement.TryPickEmptyCellAsync(
            _map.Width,
            _map.Height,
            _map.PlacementMaxAttempts,
            async (cx, cy, ct) =>
                (cx == city.X && cy == city.Y)
                || await WorldOccupancy.IsOccupiedAsync(_orm, cx, cy, ct, transaction, city.Id),
            cancellationToken);
        if (cell is null)
        {
            throw new BizException(ErrorCodes.MapFull, "暂无空地可迁城");
        }

        return cell.Value;
    }

    private async Task EnsureCanRelocateAsync(DbTransaction transaction, long cityId, CancellationToken cancellationToken)
    {
        var outgoing = await _orm.Select<MarchEntity>()
            .WithTransaction(transaction)
            .Where(m => m.FromCityId == cityId && m.Status == MarchStatus.Marching)
            .AnyAsync(cancellationToken);
        if (outgoing)
        {
            throw new BizException(ErrorCodes.RelocateBlocked, "部队行军中，不能迁城");
        }

        var incoming = await _orm.Select<MarchEntity>()
            .WithTransaction(transaction)
            .Where(m => m.TargetType == MarchTargetType.City && m.TargetId == cityId && m.Status == MarchStatus.Marching)
            .AnyAsync(cancellationToken);
        if (incoming)
        {
            throw new BizException(ErrorCodes.RelocateBlocked, "敌方正在进军本城，不能迁城");
        }

        var transport = await _orm.Select<TransportEntity>()
            .WithTransaction(transaction)
            .Where(t => t.FromCityId == cityId && t.Status == TransportStatus.InTransit)
            .AnyAsync(cancellationToken);
        if (transport)
        {
            throw new BizException(ErrorCodes.RelocateBlocked, "运输未完成，不能迁城");
        }
    }

    private async Task<BuildingEntity[]> ShortenBuildingsAsync(
        DbTransaction transaction,
        long cityId,
        string speedKind,
        DateTime now,
        int oldPercent,
        int newPercent,
        CancellationToken cancellationToken)
    {
        var rows = await _orm.Select<BuildingEntity>()
            .WithTransaction(transaction)
            .Where(b => b.CityId == cityId && b.Status == BuildingStatus.Upgrading)
            .ToListAsync(cancellationToken);
        var changed = new List<BuildingEntity>();
        foreach (var row in rows)
        {
            if (row.FinishAt is null || ItemCatalog.SpeedKindOf(row.Type) != speedKind)
            {
                continue;
            }

            var shortened = ItemCatalog.ShortenRemaining(row.FinishAt.Value, now, oldPercent, newPercent);
            if (shortened == row.FinishAt)
            {
                continue;
            }

            row.FinishAt = shortened;
            row.UpdatedAt = now;
            await _orm.Update<BuildingEntity>()
                .WithTransaction(transaction)
                .SetSource(row)
                .UpdateColumns(b => new { b.FinishAt, b.UpdatedAt })
                .ExecuteAffrowsAsync(cancellationToken);
            changed.Add(row);
        }

        return [.. changed];
    }

    private async Task<RecruitEntity[]> ShortenRecruitsAsync(
        DbTransaction transaction,
        long cityId,
        DateTime now,
        int oldPercent,
        int newPercent,
        CancellationToken cancellationToken)
    {
        var rows = await _orm.Select<RecruitEntity>()
            .WithTransaction(transaction)
            .Where(r => r.CityId == cityId)
            .ToListAsync(cancellationToken);
        var changed = new List<RecruitEntity>();
        foreach (var row in rows)
        {
            var shortened = ItemCatalog.ShortenRemaining(row.FinishAt, now, oldPercent, newPercent);
            if (shortened == row.FinishAt)
            {
                continue;
            }

            row.FinishAt = shortened;
            await _orm.Update<RecruitEntity>()
                .WithTransaction(transaction)
                .SetSource(row)
                .UpdateColumns(r => r.FinishAt)
                .ExecuteAffrowsAsync(cancellationToken);
            changed.Add(row);
        }

        return [.. changed];
    }

    private async Task RecalibrateFieldsAsync(
        DbTransaction transaction,
        long cityId,
        DateTime now,
        int oldBoost,
        int newBoost,
        CancellationToken cancellationToken)
    {
        var rows = await _orm.Select<BuildingEntity>()
            .WithTransaction(transaction)
            .Where(b => b.CityId == cityId)
            .ToListAsync(cancellationToken);
        var hallLevel = CityStats.BuildingLevel(rows, TechBonuses.ResourceHall);
        foreach (var field in rows)
        {
            var def = OuterFieldCatalog.Find(field.Type);
            if (def is null || field.Level < 1 || field.LastCollectedAt is null)
            {
                continue;
            }

            var hallRate = TechBonuses.BoostedRate(def, field.Level, hallLevel);
            var hallCap = TechBonuses.BoostedCap(def, field.Level, hallLevel);
            var pending = FieldProduction.Pending(hallRate, hallCap, field.LastCollectedAt, now, oldBoost, null);
            var newRate = TechBonuses.ApplyPercent(hallRate, newBoost);
            field.LastCollectedAt = FieldProduction.AfterCollect(now, pending, newRate);
            field.UpdatedAt = now;
            await _orm.Update<BuildingEntity>()
                .WithTransaction(transaction)
                .SetSource(field)
                .UpdateColumns(b => new { b.LastCollectedAt, b.UpdatedAt })
                .ExecuteAffrowsAsync(cancellationToken);
        }
    }

    private async Task AddItemAsync(
        DbTransaction transaction,
        long cityId,
        string itemType,
        int count,
        CancellationToken cancellationToken)
    {
        var row = await _orm.Select<ItemEntity>()
            .WithTransaction(transaction)
            .Where(i => i.CityId == cityId && i.ItemType == itemType)
            .FirstAsync(cancellationToken);
        if (row is null)
        {
            await _orm.Insert(new ItemEntity
            {
                CityId = cityId,
                ItemType = itemType,
                Count = count
            }).WithTransaction(transaction).ExecuteAffrowsAsync(cancellationToken);
            return;
        }

        var next = YuanbaoLoot.Grant(row.Count, count);
        row.Count = next;
        await _orm.Update<ItemEntity>()
            .WithTransaction(transaction)
            .SetSource(row)
            .UpdateColumns(i => i.Count)
            .ExecuteAffrowsAsync(cancellationToken);
    }

    private async Task ConsumeItemAsync(
        DbTransaction transaction,
        long cityId,
        string itemType,
        int count,
        CancellationToken cancellationToken)
    {
        var row = await _orm.Select<ItemEntity>()
            .WithTransaction(transaction)
            .ForUpdate()
            .Where(i => i.CityId == cityId && i.ItemType == itemType)
            .FirstAsync(cancellationToken);
        if (row is null || row.Count < count)
        {
            throw new BizException(ErrorCodes.ItemNotEnough, "道具数量不足");
        }

        row.Count -= count;
        if (row.Count <= 0)
        {
            await _orm.Delete<ItemEntity>()
                .WithTransaction(transaction)
                .Where(i => i.Id == row.Id)
                .ExecuteAffrowsAsync(cancellationToken);
            return;
        }

        await _orm.Update<ItemEntity>()
            .WithTransaction(transaction)
            .SetSource(row)
            .UpdateColumns(i => i.Count)
            .ExecuteAffrowsAsync(cancellationToken);
    }

    private async Task UpsertBuffAsync(
        DbTransaction transaction,
        long cityId,
        string buffType,
        DateTime expireAt,
        CancellationToken cancellationToken)
    {
        var row = await _orm.Select<BuffEntity>()
            .WithTransaction(transaction)
            .Where(b => b.CityId == cityId && b.BuffType == buffType)
            .FirstAsync(cancellationToken);
        if (row is null)
        {
            await _orm.Insert(new BuffEntity
            {
                CityId = cityId,
                BuffType = buffType,
                ExpireAt = expireAt
            }).WithTransaction(transaction).ExecuteAffrowsAsync(cancellationToken);
            return;
        }

        row.ExpireAt = expireAt;
        await _orm.Update<BuffEntity>()
            .WithTransaction(transaction)
            .SetSource(row)
            .UpdateColumns(b => b.ExpireAt)
            .ExecuteAffrowsAsync(cancellationToken);
    }

    private async Task<ShopOverviewDto> BuildOverviewAsync(CityEntity city, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var items = await _orm.Select<ItemEntity>()
            .Where(i => i.CityId == city.Id)
            .ToListAsync(cancellationToken);
        var owned = items.ToDictionary(i => i.ItemType, i => i.Count, StringComparer.OrdinalIgnoreCase);
        var buffs = await CityBuffStore.LoadAsync(_orm, city.Id, cancellationToken);
        var buildings = await _orm.Select<BuildingEntity>()
            .Where(b => b.CityId == city.Id)
            .ToListAsync(cancellationToken);
        var recruitUsed = await _orm.Select<RecruitEntity>()
            .Where(r => r.CityId == city.Id)
            .CountAsync(cancellationToken);
        var catalog = ItemCatalog.All.Select(def =>
            new ShopCatalogItemDto(
                def.Type,
                def.Name,
                def.Kind,
                def.Price,
                def.Kind == ItemKind.Buff ? def.DurationHours : null,
                def.Kind == ItemKind.Buff ? def.SpeedPercent : null,
                owned.TryGetValue(def.Type, out var count) ? count : 0,
                def.Description)).ToList();
        var active = ItemCatalog.All
            .Where(def => def.Kind == ItemKind.Buff)
            .Select(def =>
            {
                var buff = buffs.FirstOrDefault(b => b.Type.Equals(def.Type, StringComparison.OrdinalIgnoreCase));
                if (buff is null || buff.ExpireAt <= now)
                {
                    return null;
                }

                return new ShopBuffDto(def.Type, def.Name, buff.ExpireAt, def.SpeedPercent);
            })
            .Where(dto => dto is not null)
            .Select(dto => dto!)
            .ToList();

        var slots = new CityQueueSlotsDto(
            QueueSlots.State(city, QueueKind.Build, QueueSlots.Used(buildings, QueueKind.Build)),
            QueueSlots.State(city, QueueKind.Field, QueueSlots.Used(buildings, QueueKind.Field)),
            QueueSlots.State(city, QueueKind.Tech, QueueSlots.Used(buildings, QueueKind.Tech)),
            QueueSlots.State(city, QueueKind.Recruit, (int)recruitUsed));

        return new ShopOverviewDto(
            city.Id,
            now,
            city.Yuanbao,
            city.X,
            city.Y,
            city.ProtectionUntil,
            catalog,
            active,
            slots);
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
}
