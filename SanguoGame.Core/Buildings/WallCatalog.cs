namespace SanguoGame.Core.Buildings;

public sealed record WallDef(
    string Type,
    string Name,
    int MaxLevel,
    int RequirePalaceLevel,
    int BaseDurationSeconds,
    ResourceAmount BaseCost,
    int DefensePerLevel,
    int TrapBonusPercentPerLevel)
{
    public InnerBuildingDef AsUpgradeDef() =>
        new(Type, Name, BuildingCategory.Wall, MaxLevel, RequirePalaceLevel, BaseDurationSeconds, BaseCost);
}

public static class WallCatalog
{
    public static IReadOnlyList<WallDef> All { get; } =
    [
        new("arrowTower", "箭塔", 10, 2, 18, new ResourceAmount(120, 160, 80, 20), 8, 0),
        new("gate", "城门", 10, 2, 15, new ResourceAmount(150, 200, 40, 20), 6, 0),
        new("trap", "陷阱", 10, 3, 20, new ResourceAmount(80, 80, 120, 40), 0, 2)
    ];

    public static WallDef? Find(string wallType) =>
        All.FirstOrDefault(def => def.Type.Equals(wallType, StringComparison.OrdinalIgnoreCase));

    public static bool IsWall(string type) => Find(type) is not null;

    public static int WallDefense(IReadOnlyDictionary<string, int> levels)
    {
        var total = 0;
        foreach (var def in All)
        {
            var level = levels.TryGetValue(def.Type, out var value) ? value : 0;
            total += def.DefensePerLevel * Math.Max(0, level);
        }

        return total;
    }

    public static double TrapBonus(int trapLevel) =>
        Math.Max(0, trapLevel) * 0.02;
}
