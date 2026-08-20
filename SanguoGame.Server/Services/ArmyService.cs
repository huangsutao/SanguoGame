using FreeSql;
using SanguoGame.Core;
using SanguoGame.Core.Army;
using SanguoGame.Core.Buildings;
using SanguoGame.Core.Daily;
using SanguoGame.Infrastructure.Entities;
using SanguoGame.Server.Contracts;

namespace SanguoGame.Server.Services;

public sealed class ArmyService
{
    private readonly IFreeSql _orm;
    private readonly DailyService _daily;

    public ArmyService(IFreeSql orm, DailyService daily)
    {
        _orm = orm;
        _daily = daily;
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
        await CityRowLock.RunAsync(_orm, city.Id, async (transaction, locked, ct) =>
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

            var marching = await MarchingTroopsAsync(transaction, locked.Id, ct);
            var stationed = CityStats.Troops(locked);
            var cap = InnerBuildingCatalog.TroopCap(barracksLevel);
            if (stationed.Total + marching.Total + count > cap)
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
            CityStats.ApplyTroops(locked, stationed.Add(def.Type, count));
            await UpdateCityAsync(transaction, locked, ct);
            city.Grain = locked.Grain;
            city.Wood = locked.Wood;
            city.Iron = locked.Iron;
            city.Copper = locked.Copper;
            city.Infantry = locked.Infantry;
            city.Archer = locked.Archer;
            city.Cavalry = locked.Cavalry;
            return 0;
        }, cancellationToken);

        await _daily.AddProgressAsync(city.Id, DailyCatalog.Recruit, count, cancellationToken);
        return await BuildOverviewAsync(city, cancellationToken);
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
            discountPercent);
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
