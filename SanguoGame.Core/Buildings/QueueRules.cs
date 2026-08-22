namespace SanguoGame.Core.Buildings;

public enum QueueKind
{
    Build = 0,
    Field = 1,
    Tech = 2,
    Recruit = 3
}

public static class QueueRules
{
    public const int BaseSlots = 5;
    public const int MaxExtra = 1;

    public static int Limit(int extra) =>
        BaseSlots + Math.Clamp(extra, 0, MaxExtra);

    public static QueueKind OfBuilding(string buildingType)
    {
        if (OuterFieldCatalog.IsField(buildingType))
        {
            return QueueKind.Field;
        }

        var inner = InnerBuildingCatalog.Find(buildingType);
        if (inner is { Category: BuildingCategory.Tech })
        {
            return QueueKind.Tech;
        }

        return QueueKind.Build;
    }
}
