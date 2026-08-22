using System.ComponentModel.DataAnnotations;
using SanguoGame.Core.Shop;

namespace SanguoGame.Server.Contracts;

public sealed class ShopBuyRequest
{
    [Required]
    public string ItemType { get; set; } = "";

    [Range(1, 99)]
    public int Count { get; set; } = 1;
}

public sealed class ShopUseRequest
{
    [Required]
    public string ItemType { get; set; } = "";

    [Range(1, 99)]
    public int Count { get; set; } = 1;

    public int? X { get; set; }

    public int? Y { get; set; }
}

public sealed record ShopCatalogItemDto(
    string Type,
    string Name,
    ItemKind Kind,
    int Price,
    int? DurationHours,
    int? SpeedPercent,
    int Owned,
    string Description);

public sealed record ShopBuffDto(
    string Type,
    string Name,
    DateTime ExpireAt,
    int SpeedPercent);

public sealed record ShopOverviewDto(
    long CityId,
    DateTime ServerTime,
    int Yuanbao,
    int X,
    int Y,
    DateTime? ProtectionUntil,
    IReadOnlyList<ShopCatalogItemDto> Catalog,
    IReadOnlyList<ShopBuffDto> Buffs,
    CityQueueSlotsDto? Slots = null);

public sealed record RecruitQueueDto(
    string TroopType,
    int Count,
    DateTime FinishAt);

public sealed record RecruitCompleteDto(
    long CityId,
    string TroopType,
    int Count,
    DateTime ServerTime,
    TroopDto Troops);
