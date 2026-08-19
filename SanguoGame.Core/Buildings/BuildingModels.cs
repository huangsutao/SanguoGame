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
    Military
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
        new(Grain - cost.Grain, Wood - cost.Wood, Iron - cost.Iron, Copper - cost.Copper);
}

public sealed record InnerBuildingDef(
    string Type,
    string Name,
    BuildingCategory Category,
    int MaxLevel,
    int RequirePalaceLevel,
    int BaseDurationSeconds,
    ResourceAmount BaseCost);
