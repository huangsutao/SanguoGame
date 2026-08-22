using SanguoGame.Core.Buildings;
using SanguoGame.Infrastructure.Entities;
using SanguoGame.Server.Contracts;

namespace SanguoGame.Server.Services;

internal static class QueueSlots
{
    public static int Extra(CityEntity city, QueueKind kind) => kind switch
    {
        QueueKind.Build => city.ExtraBuildSlots,
        QueueKind.Field => city.ExtraFieldSlots,
        QueueKind.Tech => city.ExtraTechSlots,
        QueueKind.Recruit => city.ExtraRecruitSlots,
        _ => 0
    };

    public static void SetExtra(CityEntity city, QueueKind kind, int value)
    {
        switch (kind)
        {
            case QueueKind.Build:
                city.ExtraBuildSlots = value;
                break;
            case QueueKind.Field:
                city.ExtraFieldSlots = value;
                break;
            case QueueKind.Tech:
                city.ExtraTechSlots = value;
                break;
            case QueueKind.Recruit:
                city.ExtraRecruitSlots = value;
                break;
        }
    }

    public static int Used(IEnumerable<BuildingEntity> rows, QueueKind kind) =>
        rows.Count(b => b.Status == BuildingStatus.Upgrading && QueueRules.OfBuilding(b.Type) == kind);

    public static int Limit(CityEntity city, QueueKind kind) =>
        QueueRules.Limit(Extra(city, kind));

    public static bool IsFull(CityEntity city, IEnumerable<BuildingEntity> rows, QueueKind kind) =>
        Used(rows, kind) >= Limit(city, kind);

    public static QueueStateDto State(CityEntity city, QueueKind kind, int used) =>
        new(used, Limit(city, kind), Math.Clamp(Extra(city, kind), 0, QueueRules.MaxExtra));

    public static List<BuildingQueueDto> OfKind(IEnumerable<BuildingEntity> rows, QueueKind kind) =>
        rows
            .Where(b =>
                b.Status == BuildingStatus.Upgrading
                && QueueRules.OfBuilding(b.Type) == kind
                && b.TargetLevel is not null
                && b.FinishAt is not null)
            .OrderBy(b => b.FinishAt)
            .Select(b => new BuildingQueueDto(b.Type, b.TargetLevel!.Value, b.FinishAt!.Value))
            .ToList();

    public static List<BuildingQueueDto> Inner(IEnumerable<BuildingEntity> rows) =>
        rows
            .Where(b =>
                b.Status == BuildingStatus.Upgrading
                && InnerBuildingCatalog.Find(b.Type) is not null
                && b.TargetLevel is not null
                && b.FinishAt is not null)
            .OrderBy(b => b.FinishAt)
            .Select(b => new BuildingQueueDto(b.Type, b.TargetLevel!.Value, b.FinishAt!.Value))
            .ToList();
}
