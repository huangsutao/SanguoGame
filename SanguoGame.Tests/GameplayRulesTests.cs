using SanguoGame.Core.Army;
using SanguoGame.Core.Buildings;
using SanguoGame.Core.Daily;
using SanguoGame.Core.Market;
using SanguoGame.Core.World;
using Xunit;

namespace SanguoGame.Tests;

public class PvpLootTests
{
    private static readonly DateTime Now = new(2026, 8, 20, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void FitsInWarehouse_TakesHalfFieldThenThirtyPercentStore()
    {
        var lastCollected = Now.AddHours(-1);
        var result = PvpLoot.Compute(
            defenderStock: new ResourceAmount(1000, 0, 0, 0),
            attackerStock: ResourceAmount.Zero,
            attackerCap: 20_000,
            fields: [new FieldLootInput("farm", 1, lastCollected)],
            now: Now);

        Assert.Equal(600, result.Actual.Grain);
        Assert.Equal(600, result.AttackerStockAfter.Grain);
        Assert.Equal(700, result.DefenderStockAfter.Grain);
        var farm = Assert.Single(result.FieldUpdates);
        Assert.Equal(300, FieldProduction.Pending(600, 1500, farm.LastCollectedAt, Now));
    }

    [Fact]
    public void SmallAttackerCap_TakesFromFieldFirst_LeavesWarehouseUntouched()
    {
        var lastCollected = Now.AddHours(-1);
        var result = PvpLoot.Compute(
            defenderStock: new ResourceAmount(1000, 0, 0, 0),
            attackerStock: new ResourceAmount(7980, 0, 0, 0),
            attackerCap: 8000,
            fields: [new FieldLootInput("farm", 1, lastCollected)],
            now: Now);

        Assert.Equal(20, result.Actual.Grain);
        Assert.Equal(8000, result.AttackerStockAfter.Grain);
        Assert.Equal(1000, result.DefenderStockAfter.Grain);
        var farm = Assert.Single(result.FieldUpdates);
        Assert.Equal(580, FieldProduction.Pending(600, 1500, farm.LastCollectedAt, Now));
    }

    [Fact]
    public void CapBetweenFieldAndStore_DoesNotOverDeductWarehouse()
    {
        var lastCollected = Now.AddHours(-1);
        var result = PvpLoot.Compute(
            defenderStock: new ResourceAmount(1000, 0, 0, 0),
            attackerStock: ResourceAmount.Zero,
            attackerCap: 50,
            fields: [new FieldLootInput("farm", 1, lastCollected)],
            now: Now);

        Assert.Equal(50, result.Actual.Grain);
        Assert.Equal(50, result.AttackerStockAfter.Grain);
        Assert.Equal(1000, result.DefenderStockAfter.Grain);
        var farm = Assert.Single(result.FieldUpdates);
        Assert.Equal(550, FieldProduction.Pending(600, 1500, farm.LastCollectedAt, Now));
    }

    [Fact]
    public void WarehouseTakeIsCappedAt2000()
    {
        var result = PvpLoot.Compute(
            defenderStock: new ResourceAmount(20_000, 0, 0, 0),
            attackerStock: ResourceAmount.Zero,
            attackerCap: 50_000,
            fields: [],
            now: Now);

        Assert.Equal(2000, result.Actual.Grain);
        Assert.Equal(18_000, result.DefenderStockAfter.Grain);
    }

    [Fact]
    public void ProductionBonus_UsesBoostedFieldRate()
    {
        var lastCollected = Now.AddHours(-1);
        var result = PvpLoot.Compute(
            defenderStock: ResourceAmount.Zero,
            attackerStock: ResourceAmount.Zero,
            attackerCap: 20_000,
            fields: [new FieldLootInput("farm", 1, lastCollected)],
            now: Now,
            productionPercent: 5);

        Assert.Equal(315, result.Actual.Grain);
        var farm = Assert.Single(result.FieldUpdates);
        Assert.Equal(315, FieldProduction.Pending(630, 1575, farm.LastCollectedAt, Now));
    }
}

public class FieldProductionTests
{
    [Fact]
    public void Pending_CapsByFieldCapacity()
    {
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var pending = FieldProduction.Pending(60, 100, now.AddHours(-10), now);
        Assert.Equal(100, pending);
    }

    [Fact]
    public void AfterCollect_PreservesLeftover()
    {
        var now = new DateTime(2026, 1, 1, 1, 0, 0, DateTimeKind.Utc);
        var last = FieldProduction.AfterCollect(now, 30, 60);
        Assert.Equal(30, FieldProduction.Pending(60, 300, last, now));
    }
}

public class BattleCalculatorTests
{
    [Fact]
    public void SameSeed_IsDeterministic()
    {
        var input = new BattleInput(
            new TroopCount(20, 0, 0),
            new TroopCount(10, 0, 0),
            AcademyLevel: 0,
            WallDefense: 0,
            OutpostBasePower: 0,
            TrapBonus: 0,
            Seed: 12345);
        var a = BattleCalculator.Resolve(input);
        var b = BattleCalculator.Resolve(input);
        Assert.Equal(a, b);
        Assert.True(a.AttackerAfter.Infantry <= a.AttackerBefore.Infantry);
        Assert.True(a.DefenderAfter.Infantry <= a.DefenderBefore.Infantry);
    }

    [Fact]
    public void TroopPowerPercent_RaisesAttackerPowerBeforeAcademy()
    {
        var troops = new TroopCount(10, 0, 0);
        Assert.Equal(100, BattleCalculator.Power(troops));
        Assert.Equal(103, TechBonuses.ApplyPercent(100, 3));
    }
}

public class CatalogPlaytestNumbersTests
{
    [Fact]
    public void PalaceLevel1_TakesFifteenSeconds()
    {
        var palace = InnerBuildingCatalog.Find("palace");
        Assert.NotNull(palace);
        Assert.Equal(15, InnerBuildingCatalog.DurationSeconds(palace, 1));
        Assert.Equal(27, InnerBuildingCatalog.DurationSeconds(palace, 2));
    }

    [Fact]
    public void FarmLevel1_ProducesSixHundredPerHour()
    {
        var farm = OuterFieldCatalog.Find("farm");
        Assert.NotNull(farm);
        Assert.Equal(600, farm.RatePerHour(1));
        Assert.Equal(1500, farm.FieldCap(1));
    }

    [Fact]
    public void TechHalls_AreInCatalog_WithAcademyPrerequisite()
    {
        Assert.Equal(8, InnerBuildingCatalog.All.Count);
        var drill = InnerBuildingCatalog.Find("drillHall");
        Assert.NotNull(drill);
        Assert.Equal(3, drill.RequirePalaceLevel);
        Assert.Equal(1, drill.RequireAcademyLevel);
        Assert.Equal(20, InnerBuildingCatalog.DurationSeconds(drill, 1));
        Assert.Equal(new ResourceAmount(180, 100, 120, 40), InnerBuildingCatalog.CostToReach(drill, 1));
    }

    [Fact]
    public void RoamingCatalog_HasThreeTypes_AndExpiresByKind()
    {
        Assert.Equal(3, OutpostCatalog.Permanent.Count);
        Assert.Equal(3, OutpostCatalog.Roaming.Count);
        Assert.Equal(6, OutpostCatalog.All.Count);
        Assert.NotNull(OutpostCatalog.Find("bandit"));
        var now = new DateTime(2026, 8, 20, 12, 0, 0, DateTimeKind.Utc);
        Assert.False(OutpostCatalog.IsExpired(OutpostKind.Permanent, now, now));
        Assert.False(OutpostCatalog.IsExpired(OutpostKind.Roaming, now.AddMinutes(1), now));
        Assert.True(OutpostCatalog.IsExpired(OutpostKind.Roaming, now, now));
    }
}

public class MarketCatalogTests
{
    [Fact]
    public void ThousandGrainToWood_IsNineHundred()
    {
        Assert.Equal(900, MarketCatalog.Quote("grain", "wood", 1000));
    }

    [Fact]
    public void ThousandGrainToIron_IsSixHundred()
    {
        Assert.Equal(600, MarketCatalog.Quote("grain", "iron", 1000));
    }

    [Fact]
    public void SameResourceOrTooSmall_IsZero()
    {
        Assert.Equal(0, MarketCatalog.Quote("grain", "grain", 1000));
        Assert.Equal(0, MarketCatalog.Quote("grain", "wood", 99));
    }

    [Fact]
    public void CargoCap_GrowsWithWarehouse()
    {
        Assert.Equal(2000, MarketCatalog.CargoCap(0));
        Assert.Equal(3000, MarketCatalog.CargoCap(1));
    }
}

public class MapPlacementTests
{
    [Fact]
    public void PicksEmptyCell()
    {
        var occupied = new HashSet<(int, int)> { (0, 0) };
        var ok = MapPlacement.TryPickEmptyCell(2, 2, 32, (x, y) => occupied.Contains((x, y)), out var x, out var y, new Random(1));
        Assert.True(ok);
        Assert.DoesNotContain((x, y), occupied);
    }
}

public class TechBonusesTests
{
    [Fact]
    public void DrillHall_GivesThreePercentPowerAndTwoPercentRecruitDiscount()
    {
        Assert.Equal(3, TechBonuses.TroopPowerPercent(1));
        Assert.Equal(2, TechBonuses.RecruitDiscountPercent(1));
        Assert.Equal(50, TechBonuses.RecruitDiscountPercent(40));
        Assert.Equal(98, TechBonuses.Discount(new ResourceAmount(100, 25, 50, 0), 2).Grain);
        Assert.Equal(24, TechBonuses.Discount(new ResourceAmount(100, 25, 50, 0), 2).Wood);
    }

    [Fact]
    public void ResourceHall_BoostsFarmSixHundredToSixThirty()
    {
        var farm = OuterFieldCatalog.Find("farm");
        Assert.NotNull(farm);
        Assert.Equal(5, TechBonuses.ProductionPercent(1));
        Assert.Equal(630, TechBonuses.BoostedRate(farm, 1, 1));
        Assert.Equal(1575, TechBonuses.BoostedCap(farm, 1, 1));
    }

    [Fact]
    public void DefenseHall_AddsTwoWallDefense()
    {
        var levels = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["arrowTower"] = 1,
            ["gate"] = 0,
            ["trap"] = 0
        };
        Assert.Equal(8, WallCatalog.WallDefense(levels));
        Assert.Equal(10, WallCatalog.WallDefense(levels, 1));
        Assert.Equal(0.03, WallCatalog.TrapBonus(1, 1), 3);
    }
}

public class DailyScoutRulesTests
{
    [Fact]
    public void DayKey_UsesUtcDate()
    {
        var now = new DateTime(2026, 8, 20, 23, 30, 0, DateTimeKind.Utc);
        Assert.Equal(new DateTime(2026, 8, 20, 0, 0, 0, DateTimeKind.Utc), DailyCatalog.DayKey(now));
    }

    [Fact]
    public void Catalog_HasSixMissions_AndBundleNeedsFive()
    {
        Assert.Equal(6, DailyCatalog.All.Count);
        Assert.Equal(5, DailyCatalog.Require(DailyCatalog.Bundle).Required);
        Assert.All(DailyCatalog.All.Where(d => d.Type != DailyCatalog.Bundle), d => Assert.True(d.Required >= 1));
    }

    [Fact]
    public void Scout_IsHalfMarchRoundedUpMin()
    {
        Assert.Equal(15, MarchTiming.ScoutDurationSeconds(0, 0, 3, 0, 10, 30));
        Assert.Equal(5, MarchTiming.ScoutDurationSeconds(0, 0, 0, 1, 5, 10));
    }
}
