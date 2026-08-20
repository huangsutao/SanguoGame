using System.Data.Common;
using FreeSql;
using Hangfire;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using SanguoGame.Core;
using SanguoGame.Core.Army;
using SanguoGame.Core.Buildings;
using SanguoGame.Core.World;
using SanguoGame.Core.Social;
using SanguoGame.Infrastructure.Entities;
using SanguoGame.Server.Contracts;
using SanguoGame.Server.Hubs;
using SanguoGame.Server.Jobs;

namespace SanguoGame.Server.Services;

public sealed class MarchService
{
    private readonly IFreeSql _orm;
    private readonly IBackgroundJobClient _jobs;
    private readonly IHubContext<GameHub> _hub;
    private readonly WorldMapOptions _map;
    private readonly ArmyService _army;
    private readonly MailService _mail;
    private readonly AllianceService _alliances;

    public MarchService(
        IFreeSql orm,
        IBackgroundJobClient jobs,
        IHubContext<GameHub> hub,
        IOptions<WorldMapOptions> map,
        ArmyService army,
        MailService mail,
        AllianceService alliances)
    {
        _orm = orm;
        _jobs = jobs;
        _hub = hub;
        _map = map.Value;
        _army = army;
        _mail = mail;
        _alliances = alliances;
    }

    public async Task<ArmyOverviewDto> StartAsync(long accountId, MarchRequest request, CancellationToken cancellationToken)
    {
        if (!TryParseTarget(request.TargetType, out var targetType))
        {
            throw new BizException(ErrorCodes.ValidationFailed, "未知目标类型");
        }

        var troops = new TroopCount(request.Infantry, request.Archer, request.Cavalry);
        if (troops.Total <= 0)
        {
            throw new BizException(ErrorCodes.InsufficientTroops, "出征至少需要 1 名士兵");
        }

        var city = await _army.RequireCityAsync(accountId, cancellationToken);
        var (toX, toY) = await ResolveTargetAsync(targetType, request.TargetId, city.Id, DateTime.UtcNow, cancellationToken);

        var marchId = await CityRowLock.RunAsync(_orm, city.Id, async (transaction, locked, ct) =>
        {
            var rows = await LoadBuildingsAsync(transaction, locked.Id, ct);
            var barracksLevel = rows.FirstOrDefault(b => b.Type == "barracks")?.Level ?? 0;
            if (barracksLevel < 1)
            {
                throw new BizException(ErrorCodes.BarracksRequired, "需要兵营 1 级");
            }

            var stationed = CityStats.Troops(locked);
            if (!stationed.CanAfford(troops))
            {
                throw new BizException(ErrorCodes.InsufficientTroops, "兵力不足");
            }

            var marchingCount = await _orm.Select<MarchEntity>()
                .WithTransaction(transaction)
                .Where(m => m.FromCityId == locked.Id && m.Status == MarchStatus.Marching)
                .CountAsync(ct);
            if (marchingCount >= _map.MaxMarchesPerCity)
            {
                throw new BizException(ErrorCodes.MarchLimit, "行军数量已达上限");
            }

            CityStats.ApplyTroops(locked, stationed.Subtract(troops));
            await _orm.Update<CityEntity>()
                .WithTransaction(transaction)
                .SetSource(locked)
                .UpdateColumns(c => new { c.Infantry, c.Archer, c.Cavalry })
                .ExecuteAffrowsAsync(ct);

            var now = DateTime.UtcNow;
            var arrive = now.AddSeconds(MarchTiming.DurationSeconds(
                locked.X, locked.Y, toX, toY, _map.SecondsPerTile, _map.MinMarchSeconds));
            var march = new MarchEntity
            {
                FromCityId = locked.Id,
                TargetType = targetType,
                TargetId = request.TargetId,
                FromX = locked.X,
                FromY = locked.Y,
                ToX = toX,
                ToY = toY,
                Infantry = troops.Infantry,
                Archer = troops.Archer,
                Cavalry = troops.Cavalry,
                DepartAt = now,
                ArriveAt = arrive,
                Status = MarchStatus.Marching
            };
            march.Id = await _orm.Insert(march).WithTransaction(transaction).ExecuteIdentityAsync(ct);
            city.Infantry = locked.Infantry;
            city.Archer = locked.Archer;
            city.Cavalry = locked.Cavalry;
            return march.Id;
        }, cancellationToken);

        var stored = await _orm.Select<MarchEntity>().Where(m => m.Id == marchId).FirstAsync(cancellationToken);
        if (stored is not null)
        {
            _jobs.Schedule<CompleteMarchJob>(
                job => job.Execute(stored.Id),
                UtcSchedule.At(stored.ArriveAt));
        }

        return await _army.BuildOverviewAsync(city, cancellationToken);
    }

    public async Task CompleteAsync(long marchId, CancellationToken cancellationToken)
    {
        var march = await _orm.Select<MarchEntity>().Where(m => m.Id == marchId).FirstAsync(cancellationToken);
        if (march is null || march.Status != MarchStatus.Marching)
        {
            return;
        }

        if (march.ArriveAt > DateTime.UtcNow.AddSeconds(2))
        {
            _jobs.Schedule<CompleteMarchJob>(job => job.Execute(marchId), UtcSchedule.At(march.ArriveAt));
            return;
        }

        BattleReportDto report;
        if (march.TargetType == MarchTargetType.Outpost)
        {
            report = await SettleOutpostAsync(march, cancellationToken);
        }
        else
        {
            report = await SettleCityAsync(march, cancellationToken);
        }

        await _hub.Clients.Group($"city:{march.FromCityId}")
            .SendAsync("MarchArrived", ApiResult.Ok(report), cancellationToken);
        if (march.TargetType == MarchTargetType.City)
        {
            await _hub.Clients.Group($"city:{march.TargetId}")
                .SendAsync("CityAttacked", ApiResult.Ok(report), cancellationToken);
        }
    }

    public async Task RecoverDueAsync(CancellationToken cancellationToken)
    {
        var due = await _orm.Select<MarchEntity>()
            .Where(m => m.Status == MarchStatus.Marching && m.ArriveAt <= DateTime.UtcNow)
            .ToListAsync(cancellationToken);
        foreach (var march in due)
        {
            await CompleteAsync(march.Id, cancellationToken);
        }
    }

    public async Task<PagedResult<BattleReportDto>> ListReportsAsync(
        long accountId,
        PagedQuery query,
        CancellationToken cancellationToken)
    {
        var city = await _army.RequireCityAsync(accountId, cancellationToken);
        var filter = _orm.Select<BattleReportEntity>()
            .Where(r => r.AttackerCityId == city.Id
                || (r.DefenderType == MarchTargetType.City && r.DefenderId == city.Id));
        var total = (int)await filter.CountAsync(cancellationToken);
        var rows = await filter
            .OrderByDescending(r => r.Id)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(cancellationToken);
        return new PagedResult<BattleReportDto>
        {
            Items = rows.Select(MapReport).ToList(),
            Page = query.Page,
            PageSize = query.PageSize,
            Total = total
        };
    }

    private async Task<BattleReportDto> SettleOutpostAsync(MarchEntity march, CancellationToken cancellationToken)
    {
        return await CityRowLock.RunAsync(_orm, march.FromCityId, async (transaction, attacker, ct) =>
        {
            var current = await LoadMarchAsync(transaction, march.Id, ct);
            if (current is null || current.Status != MarchStatus.Marching)
            {
                return await LoadReportAsync(transaction, march.Id, ct)
                    ?? throw new BizException(ErrorCodes.NotFound, "行军不存在");
            }

            var outpost = await _orm.Select<OutpostEntity>()
                .WithTransaction(transaction)
                .ForUpdate()
                .Where(o => o.Id == current.TargetId)
                .FirstAsync(ct);
            if (outpost is null || OutpostCatalog.IsExpired(outpost.Kind, outpost.ExpiresAt, DateTime.UtcNow))
            {
                if (outpost is not null)
                {
                    await _orm.Delete<OutpostEntity>().WithTransaction(transaction).Where(o => o.Id == outpost.Id).ExecuteAffrowsAsync(ct);
                }

                return await FinishEmptyAsync(transaction, current, attacker, "目标据点已消失", ct);
            }

            EnsureRecovered(outpost, DateTime.UtcNow);
            var attackerTroops = new TroopCount(current.Infantry, current.Archer, current.Cavalry);
            var defenderTroops = new TroopCount(outpost.Garrison, 0, 0);
            var buildings = await LoadBuildingsAsync(transaction, attacker.Id, ct);
            var academy = CityStats.BuildingLevel(buildings, "academy");
            var warehouse = CityStats.BuildingLevel(buildings, "warehouse");
            var barracks = CityStats.BuildingLevel(buildings, "barracks");
            var drillHall = CityStats.BuildingLevel(buildings, TechBonuses.DrillHall);
            var def = OutpostCatalog.Require(outpost.Type);
            var outcome = BattleCalculator.Resolve(new BattleInput(
                attackerTroops,
                defenderTroops,
                academy,
                0,
                def.BasePower,
                0,
                SeedOf(current.Id),
                TechBonuses.TroopPowerPercent(drillHall)));

            var loot = ResourceAmount.Zero;
            if (outcome.AttackerWon)
            {
                loot = Deposit(attacker, def.Loot, InnerBuildingCatalog.ResourceCap(warehouse));
                ReturnTroops(attacker, outcome.AttackerAfter, InnerBuildingCatalog.TroopCap(barracks));
                await SaveCityAsync(transaction, attacker, ct);
                if (outpost.Kind == OutpostKind.Roaming)
                {
                    await _orm.Delete<OutpostEntity>().WithTransaction(transaction).Where(o => o.Id == outpost.Id).ExecuteAffrowsAsync(ct);
                }
                else
                {
                    outpost.Garrison = 0;
                    outpost.RecoverAt = DateTime.UtcNow.AddSeconds(_map.OutpostRecoverSeconds);
                    await _orm.Update<OutpostEntity>().WithTransaction(transaction).SetSource(outpost).ExecuteAffrowsAsync(ct);
                }

                var summary = $"攻克{outpost.Name}，缴获粮{loot.Grain} 木{loot.Wood} 铁{loot.Iron} 铜{loot.Copper}";
                return await PersistAsync(
                    transaction, current, outcome, loot, summary, attacker.CharacterId, null, ct);
            }

            outpost.Garrison = outcome.DefenderAfter.Infantry;
            ReturnTroops(attacker, outcome.AttackerAfter, InnerBuildingCatalog.TroopCap(barracks));
            await SaveCityAsync(transaction, attacker, ct);
            await _orm.Update<OutpostEntity>().WithTransaction(transaction).SetSource(outpost).ExecuteAffrowsAsync(ct);
            return await PersistAsync(
                transaction, current, outcome, loot, $"攻打{outpost.Name}失利", attacker.CharacterId, null, ct);
        }, cancellationToken);
    }

    private async Task<BattleReportDto> SettleCityAsync(MarchEntity march, CancellationToken cancellationToken)
    {
        return await CityRowLock.RunTwoAsync(_orm, march.FromCityId, march.TargetId, async (transaction, attacker, defender, ct) =>
        {
            var current = await LoadMarchAsync(transaction, march.Id, ct);
            if (current is null || current.Status != MarchStatus.Marching)
            {
                return await LoadReportAsync(transaction, march.Id, ct)
                    ?? throw new BizException(ErrorCodes.NotFound, "行军不存在");
            }

            var now = DateTime.UtcNow;
            var atkBuildings = await LoadBuildingsAsync(transaction, attacker.Id, ct);
            var atkBarracks = atkBuildings.FirstOrDefault(b => b.Type == "barracks")?.Level ?? 0;
            var allied = await _alliances.AreAlliedByCityAsync(attacker.Id, defender.Id, cancellationToken);
            var protectedCity = CityStats.IsProtected(defender, now);
            if (allied || protectedCity)
            {
                ReturnTroops(attacker, new TroopCount(current.Infantry, current.Archer, current.Cavalry), InnerBuildingCatalog.TroopCap(atkBarracks));
                await SaveCityAsync(transaction, attacker, ct);
                var skipped = new BattleOutcome(
                    false,
                    new TroopCount(current.Infantry, current.Archer, current.Cavalry),
                    new TroopCount(current.Infantry, current.Archer, current.Cavalry),
                    CityStats.Troops(defender),
                    CityStats.Troops(defender),
                    SeedOf(current.Id));
                var reason = allied ? "同联盟不可交战" : "目标已进入保护";
                return await PersistAsync(
                    transaction, current, skipped, ResourceAmount.Zero, reason, attacker.CharacterId, defender.CharacterId, ct);
            }

            var defBuildings = await LoadBuildingsAsync(transaction, defender.Id, ct);
            var academy = CityStats.BuildingLevel(atkBuildings, "academy");
            var atkWarehouse = CityStats.BuildingLevel(atkBuildings, "warehouse");
            var atkDrill = CityStats.BuildingLevel(atkBuildings, TechBonuses.DrillHall);
            var defBarracks = CityStats.BuildingLevel(defBuildings, "barracks");
            var defDrill = CityStats.BuildingLevel(defBuildings, TechBonuses.DrillHall);
            var defDefenseHall = CityStats.BuildingLevel(defBuildings, TechBonuses.DefenseHall);
            var defLevels = CityStats.WallLevels(defBuildings);
            var trapLevel = defLevels.TryGetValue("trap", out var trap) ? trap : 0;
            var attackerTroops = new TroopCount(current.Infantry, current.Archer, current.Cavalry);
            var defenderTroops = CityStats.Troops(defender);
            var outcome = BattleCalculator.Resolve(new BattleInput(
                attackerTroops,
                defenderTroops,
                academy,
                WallCatalog.WallDefense(defLevels, defDefenseHall),
                0,
                WallCatalog.TrapBonus(trapLevel, defDefenseHall),
                SeedOf(current.Id),
                TechBonuses.TroopPowerPercent(atkDrill),
                TechBonuses.TroopPowerPercent(defDrill)));

            var loot = ResourceAmount.Zero;
            if (outcome.AttackerWon)
            {
                loot = LootPlayer(defender, defBuildings, attacker, InnerBuildingCatalog.ResourceCap(atkWarehouse), now);
                defender.ProtectionUntil = now.AddSeconds(_map.ProtectionSeconds);
                foreach (var building in defBuildings.Where(b => OuterFieldCatalog.IsField(b.Type)))
                {
                    await _orm.Update<BuildingEntity>()
                        .WithTransaction(transaction)
                        .SetSource(building)
                        .UpdateColumns(b => new { b.LastCollectedAt, b.UpdatedAt })
                        .ExecuteAffrowsAsync(ct);
                }
            }

            ReturnTroops(attacker, outcome.AttackerAfter, InnerBuildingCatalog.TroopCap(atkBarracks));
            CityStats.ApplyTroops(defender, CityStats.FitCap(outcome.DefenderAfter, InnerBuildingCatalog.TroopCap(defBarracks)));
            await SaveCityAsync(transaction, attacker, ct);
            await SaveCityAsync(transaction, defender, ct);
            var summary = outcome.AttackerWon
                ? $"攻打{defender.Name}获胜，掠夺粮{loot.Grain} 木{loot.Wood} 铁{loot.Iron} 铜{loot.Copper}"
                : $"攻打{defender.Name}失利";
            return await PersistAsync(
                transaction, current, outcome, loot, summary, attacker.CharacterId, defender.CharacterId, ct);
        }, cancellationToken);
    }

    private async Task<(int X, int Y)> ResolveTargetAsync(
        MarchTargetType targetType,
        long targetId,
        long fromCityId,
        DateTime now,
        CancellationToken cancellationToken)
    {
        if (targetType == MarchTargetType.Outpost)
        {
            var outpost = await _orm.Select<OutpostEntity>().Where(o => o.Id == targetId).FirstAsync(cancellationToken);
            if (outpost is null || OutpostCatalog.IsExpired(outpost.Kind, outpost.ExpiresAt, DateTime.UtcNow))
            {
                throw new BizException(ErrorCodes.NotFound, "据点不存在");
            }

            return (outpost.X, outpost.Y);
        }

        var city = await _orm.Select<CityEntity>().Where(c => c.Id == targetId).FirstAsync(cancellationToken);
        if (city is null)
        {
            throw new BizException(ErrorCodes.NotFound, "目标城不存在");
        }

        if (city.Id == fromCityId)
        {
            throw new BizException(ErrorCodes.CannotAttackSelf, "不能进攻自己的城");
        }

        if (await _alliances.AreAlliedByCityAsync(fromCityId, targetId, cancellationToken))
        {
            throw new BizException(ErrorCodes.SameAlliance, "同联盟不可交战");
        }

        if (CityStats.IsProtected(city, now))
        {
            throw new BizException(ErrorCodes.CityProtected, "目标处于保护期");
        }

        return (city.X, city.Y);
    }

    private static bool TryParseTarget(string value, out MarchTargetType targetType)
    {
        if (value.Equals("outpost", StringComparison.OrdinalIgnoreCase))
        {
            targetType = MarchTargetType.Outpost;
            return true;
        }

        if (value.Equals("city", StringComparison.OrdinalIgnoreCase))
        {
            targetType = MarchTargetType.City;
            return true;
        }

        targetType = default;
        return false;
    }

    private static void EnsureRecovered(OutpostEntity outpost, DateTime now)
    {
        if (outpost.Kind != OutpostKind.Permanent)
        {
            return;
        }

        if (outpost.RecoverAt is { } until && until <= now)
        {
            var def = OutpostCatalog.Require(outpost.Type);
            outpost.Garrison = def.Garrison;
            outpost.RecoverAt = null;
        }
    }

    private static ResourceAmount LootPlayer(
        CityEntity defender,
        IReadOnlyList<BuildingEntity> buildings,
        CityEntity attacker,
        int attackerCap,
        DateTime now)
    {
        var byType = buildings.ToDictionary(b => b.Type, StringComparer.OrdinalIgnoreCase);
        var fields = new List<FieldLootInput>();
        foreach (var def in OuterFieldCatalog.All)
        {
            if (!byType.TryGetValue(def.Type, out var entity) || entity.Level < 1 || entity.LastCollectedAt is null)
            {
                continue;
            }

            fields.Add(new FieldLootInput(def.Type, entity.Level, entity.LastCollectedAt));
        }

        var hallLevel = byType.TryGetValue(TechBonuses.ResourceHall, out var hall) ? hall.Level : 0;
        var result = PvpLoot.Compute(
            CityStats.Stock(defender),
            CityStats.Stock(attacker),
            attackerCap,
            fields,
            now,
            TechBonuses.ProductionPercent(hallLevel));
        CityStats.ApplyStock(attacker, result.AttackerStockAfter);
        CityStats.ApplyStock(defender, result.DefenderStockAfter);
        foreach (var update in result.FieldUpdates)
        {
            if (!byType.TryGetValue(update.Type, out var entity))
            {
                continue;
            }

            entity.LastCollectedAt = update.LastCollectedAt;
            entity.UpdatedAt = now;
        }

        return result.Actual;
    }

    private static ResourceAmount Deposit(CityEntity city, ResourceAmount loot, int cap)
    {
        var space = new ResourceAmount(
            Math.Max(0, cap - city.Grain),
            Math.Max(0, cap - city.Wood),
            Math.Max(0, cap - city.Iron),
            Math.Max(0, cap - city.Copper));
        var actual = loot.Min(space);
        CityStats.ApplyStock(city, CityStats.Stock(city).Add(actual));
        return actual;
    }

    private static void ReturnTroops(CityEntity city, TroopCount returned, int cap) =>
        CityStats.ApplyTroops(city, CityStats.FitCap(CityStats.Troops(city).Add(returned), cap));

    private async Task<BattleReportDto> FinishEmptyAsync(
        DbTransaction transaction,
        MarchEntity march,
        CityEntity attacker,
        string summary,
        CancellationToken cancellationToken)
    {
        var troops = new TroopCount(march.Infantry, march.Archer, march.Cavalry);
        var buildings = await LoadBuildingsAsync(transaction, attacker.Id, cancellationToken);
        var barracks = buildings.FirstOrDefault(b => b.Type == "barracks")?.Level ?? 0;
        ReturnTroops(attacker, troops, InnerBuildingCatalog.TroopCap(barracks));
        await SaveCityAsync(transaction, attacker, cancellationToken);
        var outcome = new BattleOutcome(false, troops, troops, TroopCount.Zero, TroopCount.Zero, SeedOf(march.Id));
        return await PersistAsync(transaction, march, outcome, ResourceAmount.Zero, summary, attacker.CharacterId, null, cancellationToken);
    }

    private async Task<BattleReportDto> PersistAsync(
        DbTransaction transaction,
        MarchEntity march,
        BattleOutcome outcome,
        ResourceAmount loot,
        string summary,
        long attackerCharacterId,
        long? defenderCharacterId,
        CancellationToken cancellationToken)
    {
        march.Status = MarchStatus.Settled;
        await _orm.Update<MarchEntity>()
            .WithTransaction(transaction)
            .SetSource(march)
            .UpdateColumns(m => m.Status)
            .ExecuteAffrowsAsync(cancellationToken);

        var entity = new BattleReportEntity
        {
            MarchId = march.Id,
            AttackerCityId = march.FromCityId,
            DefenderType = march.TargetType,
            DefenderId = march.TargetId,
            AttackerWon = outcome.AttackerWon,
            AtkInfBefore = outcome.AttackerBefore.Infantry,
            AtkArcBefore = outcome.AttackerBefore.Archer,
            AtkCavBefore = outcome.AttackerBefore.Cavalry,
            AtkInfAfter = outcome.AttackerAfter.Infantry,
            AtkArcAfter = outcome.AttackerAfter.Archer,
            AtkCavAfter = outcome.AttackerAfter.Cavalry,
            DefInfBefore = outcome.DefenderBefore.Infantry,
            DefArcBefore = outcome.DefenderBefore.Archer,
            DefCavBefore = outcome.DefenderBefore.Cavalry,
            DefInfAfter = outcome.DefenderAfter.Infantry,
            DefArcAfter = outcome.DefenderAfter.Archer,
            DefCavAfter = outcome.DefenderAfter.Cavalry,
            LootGrain = loot.Grain,
            LootWood = loot.Wood,
            LootIron = loot.Iron,
            LootCopper = loot.Copper,
            Seed = outcome.Seed,
            Summary = summary,
            CreatedAt = DateTime.UtcNow
        };
        entity.Id = await _orm.Insert(entity).WithTransaction(transaction).ExecuteIdentityAsync(cancellationToken);
        var report = MapReport(entity);
        await _mail.SendAsync(
            attackerCharacterId,
            MailType.Battle,
            outcome.AttackerWon ? "出征获胜" : "出征结束",
            summary,
            "report",
            entity.Id,
            cancellationToken,
            transaction);
        if (defenderCharacterId is long defenderId && defenderId != attackerCharacterId)
        {
            await _mail.SendAsync(
                defenderId,
                MailType.Battle,
                "本城遭到攻击",
                summary,
                "report",
                entity.Id,
                cancellationToken,
                transaction);
        }

        return report;
    }

    private async Task<MarchEntity?> LoadMarchAsync(DbTransaction transaction, long id, CancellationToken cancellationToken)
    {
        return await _orm.Select<MarchEntity>().WithTransaction(transaction).Where(m => m.Id == id).ToOneAsync(cancellationToken);
    }

    private async Task<BattleReportDto?> LoadReportAsync(DbTransaction transaction, long marchId, CancellationToken cancellationToken)
    {
        var row = await _orm.Select<BattleReportEntity>()
            .WithTransaction(transaction)
            .Where(r => r.MarchId == marchId)
            .FirstAsync(cancellationToken);
        return row is null ? null : MapReport(row);
    }

    private Task<List<BuildingEntity>> LoadBuildingsAsync(
        DbTransaction transaction,
        long cityId,
        CancellationToken cancellationToken) =>
        _orm.Select<BuildingEntity>()
            .WithTransaction(transaction)
            .Where(b => b.CityId == cityId)
            .ToListAsync(cancellationToken);

    private Task<int> SaveCityAsync(DbTransaction transaction, CityEntity city, CancellationToken cancellationToken) =>
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
                c.Cavalry,
                c.ProtectionUntil
            })
            .ExecuteAffrowsAsync(cancellationToken);

    private static BattleReportDto MapReport(BattleReportEntity row) =>
        new(
            row.Id,
            row.MarchId,
            row.AttackerCityId,
            row.DefenderType,
            row.DefenderId,
            row.AttackerWon,
            new TroopDto(row.AtkInfBefore, row.AtkArcBefore, row.AtkCavBefore),
            new TroopDto(row.AtkInfAfter, row.AtkArcAfter, row.AtkCavAfter),
            new TroopDto(row.DefInfBefore, row.DefArcBefore, row.DefCavBefore),
            new TroopDto(row.DefInfAfter, row.DefArcAfter, row.DefCavAfter),
            new ResourceDto(row.LootGrain, row.LootWood, row.LootIron, row.LootCopper),
            row.Seed,
            row.Summary,
            row.CreatedAt);

    private static int SeedOf(long marchId) => unchecked((int)(marchId * 1103515245 + 12345));
}
