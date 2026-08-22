using System.ComponentModel.DataAnnotations;
using SanguoGame.Core.Buildings;

namespace SanguoGame.Server.Contracts;

public sealed class UpgradeBuildingRequest
{
    [Required]
    public string BuildingType { get; set; } = "";
}

public sealed record ResourceDto(int Grain, int Wood, int Iron, int Copper);

public sealed record BuildingCostDto(int Level, int DurationSeconds, ResourceDto Cost);

public sealed record BuildingQueueDto(string BuildingType, int TargetLevel, DateTime FinishAt);

public sealed record QueueStateDto(int Used, int Limit, int Extra);

public sealed record CityQueueSlotsDto(
    QueueStateDto Build,
    QueueStateDto Field,
    QueueStateDto Tech,
    QueueStateDto Recruit);

public sealed record BuildingItemDto(
    string Type,
    string Name,
    BuildingCategory Category,
    int Level,
    int MaxLevel,
    BuildingStatus Status,
    int? TargetLevel,
    DateTime? FinishAt,
    IReadOnlyDictionary<string, int> Effects,
    BuildingCostDto? Next,
    string? BlockedReason);

public sealed record BuildingsOverviewDto(
    long CityId,
    DateTime ServerTime,
    ResourceDto Resources,
    int ResourceCap,
    int PopulationCap,
    BuildingQueueDto? Queue,
    IReadOnlyList<BuildingItemDto> Buildings,
    IReadOnlyList<BuildingQueueDto>? Queues = null,
    QueueStateDto? BuildSlots = null,
    QueueStateDto? TechSlots = null);

public sealed record BuildCompleteDto(
    long CityId,
    string BuildingType,
    int Level,
    DateTime ServerTime,
    ResourceDto Resources,
    int ResourceCap,
    int PopulationCap);
