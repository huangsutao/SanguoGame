using System.ComponentModel.DataAnnotations;
using SanguoGame.Core.Army;
using SanguoGame.Core.Buildings;
using SanguoGame.Core.World;

namespace SanguoGame.Server.Contracts;

public sealed class UpgradeWallRequest
{
    [Required]
    public string WallType { get; set; } = "";
}

public sealed record WallsOverviewDto(
    long CityId,
    DateTime ServerTime,
    ResourceDto Resources,
    int ResourceCap,
    int WallDefense,
    double TrapBonus,
    BuildingQueueDto? Queue,
    IReadOnlyList<BuildingItemDto> Walls);

public sealed class RecruitRequest
{
    [Required]
    public string TroopType { get; set; } = "";

    [Range(1, 100)]
    public int Count { get; set; } = 1;
}

public sealed class MarchRequest
{
    [Required]
    public string TargetType { get; set; } = "";

    [Range(1, long.MaxValue)]
    public long TargetId { get; set; }

    [Range(0, 100000)]
    public int Infantry { get; set; }

    [Range(0, 100000)]
    public int Archer { get; set; }

    [Range(0, 100000)]
    public int Cavalry { get; set; }
}

public sealed record TroopDto(int Infantry, int Archer, int Cavalry);

public sealed record TroopTypeDto(
    string Type,
    string Name,
    int RequireBarracksLevel,
    ResourceDto UnitCost);

public sealed record MarchDto(
    long Id,
    MarchTargetType TargetType,
    long TargetId,
    int FromX,
    int FromY,
    int ToX,
    int ToY,
    TroopDto? Troops,
    DateTime DepartAt,
    DateTime ArriveAt,
    MarchStatus Status,
    bool Mine,
    MarchKind Kind = MarchKind.Attack);

public sealed record ArmyOverviewDto(
    long CityId,
    DateTime ServerTime,
    ResourceDto Resources,
    int ResourceCap,
    TroopDto Troops,
    int TroopCap,
    int BarracksLevel,
    int WallDefense,
    DateTime? ProtectionUntil,
    IReadOnlyList<MarchDto> Marches,
    IReadOnlyList<TroopTypeDto> TroopTypes,
    int TroopPowerBonusPercent = 0,
    int RecruitDiscountPercent = 0);

public sealed record BattleReportDto(
    long Id,
    long MarchId,
    long AttackerCityId,
    MarchTargetType DefenderType,
    long DefenderId,
    bool AttackerWon,
    TroopDto AttackerBefore,
    TroopDto AttackerAfter,
    TroopDto DefenderBefore,
    TroopDto DefenderAfter,
    ResourceDto Loot,
    int Seed,
    string Summary,
    DateTime CreatedAt);

public sealed record WorldCityDto(
    long Id,
    string Name,
    int X,
    int Y,
    string Owner,
    bool Protected);

public sealed record WorldOutpostDto(
    long Id,
    string Type,
    string Name,
    int X,
    int Y,
    int Garrison,
    OutpostKind Kind,
    DateTime? ExpiresAt);

public sealed record WorldOriginDto(int X, int Y);

public sealed record WorldDto(
    int Width,
    int Height,
    DateTime ServerTime,
    WorldOriginDto Origin,
    IReadOnlyList<WorldCityDto> Cities,
    IReadOnlyList<WorldOutpostDto> Outposts,
    IReadOnlyList<MarchDto> Marches,
    IReadOnlyList<WorldMarketDto> Markets,
    IReadOnlyList<TransportDto> Transports);
