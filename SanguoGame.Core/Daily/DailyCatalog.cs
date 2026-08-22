using SanguoGame.Core.Buildings;

namespace SanguoGame.Core.Daily;

public sealed record DailyMissionDef(
    string Type,
    string Name,
    string Detail,
    int Required,
    ResourceAmount Reward);

public static class DailyCatalog
{
    public const string Collect = "collect";
    public const string Upgrade = "upgrade";
    public const string Recruit = "recruit";
    public const string Raid = "raid";
    public const string Trade = "trade";
    public const string Bundle = "bundle";

    public static IReadOnlyList<DailyMissionDef> All { get; } =
    [
        new(Collect, "开仓收粮", "收取一次城外产出", 1, new ResourceAmount(200, 0, 0, 0)),
        new(Upgrade, "营建城池", "下达一次建造或升级", 1, new ResourceAmount(0, 200, 0, 0)),
        new(Recruit, "扩军备战", "累计征兵 10 名", 10, new ResourceAmount(0, 0, 100, 0)),
        new(Raid, "讨伐据点", "战胜一座据点", 1, new ResourceAmount(0, 0, 0, 80)),
        new(Trade, "市集贸易", "成功出发一次市集兑换", 1, new ResourceAmount(150, 150, 0, 0)),
        new(Bundle, "今日犒赏", "完成其余全部军务", 5, new ResourceAmount(400, 400, 80, 40))
    ];

    public static DailyMissionDef? Find(string type) =>
        All.FirstOrDefault(def => def.Type.Equals(type, StringComparison.OrdinalIgnoreCase));

    public static DailyMissionDef Require(string type) =>
        Find(type) ?? throw new ArgumentOutOfRangeException(nameof(type), type, "未知军务");

    public static DateTime DayKey(DateTime utcNow)
    {
        var utc = utcNow.Kind == DateTimeKind.Utc ? utcNow : utcNow.ToUniversalTime();
        return DateTime.SpecifyKind(utc.Date, DateTimeKind.Utc);
    }

    public static bool IsBundle(string type) =>
        type.Equals(Bundle, StringComparison.OrdinalIgnoreCase);
}
