namespace SanguoGame.Core.Buildings;

public static class InnerBuildingCatalog
{
    public const int StartingResource = 2000;
    public const int DefaultResourceCap = 8000;

    public static IReadOnlyList<InnerBuildingDef> All { get; } =
    [
        new("palace", "主殿", BuildingCategory.Civil, 10, 0, 30, new ResourceAmount(200, 200, 80, 40)),
        new("house", "民居", BuildingCategory.Civil, 10, 1, 20, new ResourceAmount(120, 80, 20, 10)),
        new("warehouse", "仓库", BuildingCategory.Civil, 10, 1, 25, new ResourceAmount(100, 160, 40, 20)),
        new("academy", "书院", BuildingCategory.Tech, 10, 2, 40, new ResourceAmount(150, 150, 60, 80)),
        new("barracks", "兵营", BuildingCategory.Military, 10, 2, 40, new ResourceAmount(180, 100, 120, 30))
    ];

    public static InnerBuildingDef? Find(string buildingType) =>
        All.FirstOrDefault(def => def.Type.Equals(buildingType, StringComparison.OrdinalIgnoreCase));

    public static int DurationSeconds(InnerBuildingDef def, int targetLevel) =>
        (int)Math.Ceiling(def.BaseDurationSeconds * Math.Pow(1.8, targetLevel - 1));

    public static ResourceAmount CostToReach(InnerBuildingDef def, int targetLevel)
    {
        var factor = Math.Pow(1.5, targetLevel - 1);
        return new ResourceAmount(
            (int)Math.Ceiling(def.BaseCost.Grain * factor),
            (int)Math.Ceiling(def.BaseCost.Wood * factor),
            (int)Math.Ceiling(def.BaseCost.Iron * factor),
            (int)Math.Ceiling(def.BaseCost.Copper * factor));
    }

    public static int ResourceCap(int warehouseLevel) =>
        DefaultResourceCap + 4000 * warehouseLevel;

    public static int PopulationCap(int houseLevel) =>
        50 + 100 * houseLevel;

    public static int TroopCap(int barracksLevel) =>
        30 + 40 * barracksLevel;
}
