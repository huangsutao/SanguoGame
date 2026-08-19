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
    IReadOnlyList<BuildingItemDto> Buildings);

public sealed record BuildCompleteDto(
    long CityId,
    string BuildingType,
    int Level,
    DateTime ServerTime,
    ResourceDto Resources,
    int ResourceCap,
    int PopulationCap);
