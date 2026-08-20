namespace SanguoGame.Core.Buildings;

public enum BuildingStatus
{
    Idle = 0,
    Upgrading = 1
}

public enum BuildingCategory
{
    Civil,
    Tech,
    Military,
    Wall
}

public sealed record ResourceAmount(int Grain, int Wood, int Iron, int Copper)
{
    public static ResourceAmount Zero { get; } = new(0, 0, 0, 0);

    public string? FirstMissingAgainst(ResourceAmount cost)
    {
        if (Grain < cost.Grain)
        {
            return "粮";
        }

        if (Wood < cost.Wood)
        {
            return "木";
        }

        if (Iron < cost.Iron)
        {
            return "铁";
        }

        if (Copper < cost.Copper)
        {
            return "铜";
        }

        return null;
    }

    public ResourceAmount Subtract(ResourceAmount cost) =>
        new(
            Math.Max(0, Grain - cost.Grain),
            Math.Max(0, Wood - cost.Wood),
            Math.Max(0, Iron - cost.Iron),
            Math.Max(0, Copper - cost.Copper));

    public ResourceAmount Add(ResourceAmount other) =>
        new(Grain + other.Grain, Wood + other.Wood, Iron + other.Iron, Copper + other.Copper);

    public ResourceAmount ScaleFloor(double ratio) =>
        new(
            (int)Math.Floor(Grain * ratio),
            (int)Math.Floor(Wood * ratio),
            (int)Math.Floor(Iron * ratio),
            (int)Math.Floor(Copper * ratio));

    public ResourceAmount Min(ResourceAmount other) =>
        new(
            Math.Min(Grain, other.Grain),
            Math.Min(Wood, other.Wood),
            Math.Min(Iron, other.Iron),
            Math.Min(Copper, other.Copper));

    public ResourceAmount WithCap(int cap) =>
        new(Math.Min(Grain, cap), Math.Min(Wood, cap), Math.Min(Iron, cap), Math.Min(Copper, cap));

    public int Total => Grain + Wood + Iron + Copper;

    public int Get(string resource) => resource switch
    {
        "grain" => Grain,
        "wood" => Wood,
        "iron" => Iron,
        "copper" => Copper,
        _ => throw new ArgumentOutOfRangeException(nameof(resource), resource, "未知资源")
    };

    public ResourceAmount Add(string resource, int delta) => resource switch
    {
        "grain" => this with { Grain = Grain + delta },
        "wood" => this with { Wood = Wood + delta },
        "iron" => this with { Iron = Iron + delta },
        "copper" => this with { Copper = Copper + delta },
        _ => throw new ArgumentOutOfRangeException(nameof(resource), resource, "未知资源")
    };
}

public sealed record InnerBuildingDef(
    string Type,
    string Name,
    BuildingCategory Category,
    int MaxLevel,
    int RequirePalaceLevel,
    int BaseDurationSeconds,
    ResourceAmount BaseCost,
    int RequireAcademyLevel = 0);
