namespace SanguoGame.Core.Buildings;

public sealed record OuterFieldDef(
    string Type,
    string Name,
    string Resource,
    int MaxLevel,
    int RequirePalaceLevel,
    int BaseDurationSeconds,
    ResourceAmount BaseCost,
    int BaseRatePerHour,
    int BaseFieldCap)
{
    public int RatePerHour(int level) => level < 1 ? 0 : BaseRatePerHour * level;

    public int FieldCap(int level) => level < 1 ? 0 : BaseFieldCap * level;

    public InnerBuildingDef AsUpgradeDef() =>
        new(Type, Name, BuildingCategory.Civil, MaxLevel, RequirePalaceLevel, BaseDurationSeconds, BaseCost);
}

public static class OuterFieldCatalog
{
    public static IReadOnlyList<OuterFieldDef> All { get; } =
    [
        new("farm", "良田", "grain", 10, 1, 25, new ResourceAmount(150, 80, 20, 10), 60, 300),
        new("lumber", "木场", "wood", 10, 1, 25, new ResourceAmount(80, 150, 20, 10), 50, 300),
        new("ironMine", "铁矿", "iron", 10, 1, 30, new ResourceAmount(100, 100, 80, 20), 40, 300),
        new("copperMine", "铜矿", "copper", 10, 1, 30, new ResourceAmount(100, 80, 40, 80), 30, 300)
    ];

    public static OuterFieldDef? Find(string fieldType) =>
        All.FirstOrDefault(def => def.Type.Equals(fieldType, StringComparison.OrdinalIgnoreCase));

    public static bool IsField(string type) => Find(type) is not null;
}

public static class FieldProduction
{
    public static int Pending(int ratePerHour, int fieldCap, DateTime? lastCollectedAt, DateTime now)
    {
        if (ratePerHour <= 0 || fieldCap <= 0 || lastCollectedAt is null)
        {
            return 0;
        }

        var elapsed = Math.Max(0d, (AsUtc(now) - AsUtc(lastCollectedAt.Value)).TotalSeconds);
        var pending = (int)Math.Floor(ratePerHour * elapsed / 3600d);
        return Math.Min(pending, fieldCap);
    }

    public static DateTime AfterCollect(DateTime now, int leftover, int ratePerHour)
    {
        now = AsUtc(now);
        if (leftover <= 0 || ratePerHour <= 0)
        {
            return now;
        }

        var seconds = (int)Math.Floor(leftover * 3600d / ratePerHour);
        return now.AddSeconds(-seconds);
    }

    private static DateTime AsUtc(DateTime value) =>
        value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);
}
