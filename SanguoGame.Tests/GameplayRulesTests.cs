using SanguoGame.Core.Army;
using SanguoGame.Core.Buildings;
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
