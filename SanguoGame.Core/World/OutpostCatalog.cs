using SanguoGame.Core.Buildings;

namespace SanguoGame.Core.World;

public sealed record OutpostDef(
    string Type,
    string Name,
    int Garrison,
    int BasePower,
    ResourceAmount Loot);

public static class OutpostCatalog
{
    public static IReadOnlyList<OutpostDef> All { get; } =
    [
        new("village", "村落", 40, 200, new ResourceAmount(80, 80, 40, 20)),
        new("camp", "营寨", 80, 500, new ResourceAmount(150, 150, 80, 40)),
        new("fortress", "关隘", 150, 1000, new ResourceAmount(300, 250, 150, 80))
    ];

    public static OutpostDef? Find(string type) =>
        All.FirstOrDefault(def => def.Type.Equals(type, StringComparison.OrdinalIgnoreCase));

    public static OutpostDef Require(string type) =>
        Find(type) ?? throw new InvalidOperationException($"未知据点类型 {type}");
}

public static class AiTemplates
{
    public static IReadOnlyList<string> CharacterNames { get; } =
    [
        "黄巾甲", "黄巾乙", "董卓部", "袁术军", "吕布营", "公孙部", "张燕寨", "白波军"
    ];

    public static IReadOnlyList<string> UpgradeOrder { get; } =
    [
        "palace", "house", "warehouse", "barracks", "farm", "lumber", "arrowTower", "gate"
    ];
}
