using System.ComponentModel.DataAnnotations;
using SanguoGame.Core.Buildings;

namespace SanguoGame.Server.Contracts;

public sealed class UpgradeFieldRequest
{
    [Required]
    public string FieldType { get; set; } = "";
}

public sealed class CollectFieldsRequest
{
    public string? FieldType { get; set; }
}

public sealed record FieldItemDto(
    string Type,
    string Name,
    string Resource,
    int Level,
    int MaxLevel,
    BuildingStatus Status,
    int? TargetLevel,
    DateTime? FinishAt,
    int RatePerHour,
    int FieldCap,
    int Pending,
    DateTime? LastCollectedAt,
    BuildingCostDto? Next,
    string? BlockedReason);

public sealed record FieldsOverviewDto(
    long CityId,
    DateTime ServerTime,
    ResourceDto Resources,
    int ResourceCap,
    BuildingQueueDto? Queue,
    IReadOnlyList<FieldItemDto> Fields,
    IReadOnlyList<BuildingQueueDto>? Queues = null,
    QueueStateDto? FieldSlots = null);

public sealed record FieldsCollectDto(
    long CityId,
    DateTime ServerTime,
    ResourceDto Resources,
    int ResourceCap,
    ResourceDto Collected,
    IReadOnlyList<FieldItemDto> Fields);
