namespace SanguoGame.Core.Buildings;

public sealed record FieldLootInput(string Type, int Level, DateTime? LastCollectedAt);

public sealed record FieldLootOutput(string Type, DateTime LastCollectedAt);

public sealed record PvpLootResult(
    ResourceAmount Actual,
    ResourceAmount DefenderStockAfter,
    ResourceAmount AttackerStockAfter,
    IReadOnlyList<FieldLootOutput> FieldUpdates);

/// <summary>
/// 玩家城掠夺：先田后仓；攻方仓库装不下的部分留在守方，不消失。
/// </summary>
public static class PvpLoot
{
    public const double FieldTakeRatio = 0.5;
    public const double WarehouseTakeRatio = 0.3;
    public const int WarehousePerResourceCap = 2000;

    public static PvpLootResult Compute(
        ResourceAmount defenderStock,
        ResourceAmount attackerStock,
        int attackerCap,
        IReadOnlyList<FieldLootInput> fields,
        DateTime now)
    {
        var byType = fields.ToDictionary(f => f.Type, StringComparer.OrdinalIgnoreCase);
        var space = new ResourceAmount(
            Math.Max(0, attackerCap - attackerStock.Grain),
            Math.Max(0, attackerCap - attackerStock.Wood),
            Math.Max(0, attackerCap - attackerStock.Iron),
            Math.Max(0, attackerCap - attackerStock.Copper));

        var actual = ResourceAmount.Zero;
        var attackerAfter = attackerStock;
        var updates = new List<FieldLootOutput>();

        foreach (var def in OuterFieldCatalog.All)
        {
            if (!byType.TryGetValue(def.Type, out var field) || field.Level < 1 || field.LastCollectedAt is null)
            {
                continue;
            }

            var rate = def.RatePerHour(field.Level);
            var pending = FieldProduction.Pending(rate, def.FieldCap(field.Level), field.LastCollectedAt, now);
            var maxTake = (int)Math.Floor(pending * FieldTakeRatio);
            var fromField = Math.Min(maxTake, space.Get(def.Resource));
            var leftover = pending - fromField;
            updates.Add(new FieldLootOutput(def.Type, FieldProduction.AfterCollect(now, leftover, rate)));
            if (fromField <= 0)
            {
                continue;
            }

            actual = actual.Add(def.Resource, fromField);
            attackerAfter = attackerAfter.Add(def.Resource, fromField);
            space = space.Add(def.Resource, -fromField);
        }

        var fromStore = new ResourceAmount(
            TakeFromWarehouse(defenderStock.Grain, space.Grain),
            TakeFromWarehouse(defenderStock.Wood, space.Wood),
            TakeFromWarehouse(defenderStock.Iron, space.Iron),
            TakeFromWarehouse(defenderStock.Copper, space.Copper));

        actual = actual.Add(fromStore);
        attackerAfter = attackerAfter.Add(fromStore).WithCap(attackerCap);
        var defenderAfter = defenderStock.Subtract(fromStore);
        return new PvpLootResult(actual, defenderAfter, attackerAfter, updates);
    }

    private static int TakeFromWarehouse(int stock, int space) =>
        Math.Min(Math.Min((int)Math.Floor(stock * WarehouseTakeRatio), WarehousePerResourceCap), Math.Max(0, space));
}
