using System.ComponentModel.DataAnnotations;
using SanguoGame.Core.Market;

namespace SanguoGame.Server.Contracts;

public sealed class MarketTradeRequest
{
    [Range(1, long.MaxValue)]
    public long MarketId { get; set; }

    [Required]
    public string FromResource { get; set; } = "";

    [Required]
    public string ToResource { get; set; } = "";

    [Range(1, 100_000_000)]
    public int Amount { get; set; }
}

public sealed class MarketAidRequest
{
    [Range(1, long.MaxValue)]
    public long TargetCityId { get; set; }

    [Range(0, 100_000_000)]
    public int Grain { get; set; }

    [Range(0, 100_000_000)]
    public int Wood { get; set; }

    [Range(0, 100_000_000)]
    public int Iron { get; set; }

    [Range(0, 100_000_000)]
    public int Copper { get; set; }
}

public sealed record MarketValueDto(int Grain, int Wood, int Iron, int Copper);

public sealed record MarketRateDto(string FromResource, string ToResource, int FromAmount, int ToAmount);

public sealed record WorldMarketDto(long Id, string Name, int X, int Y);

public sealed record MarketItemDto(
    long Id,
    string Name,
    int X,
    int Y,
    int DurationSeconds,
    int RoundTripSeconds);

public sealed record TransportDto(
    long Id,
    TransportKind Kind,
    long FromCityId,
    long ToCityId,
    long TargetId,
    int FromX,
    int FromY,
    int ToX,
    int ToY,
    ResourceDto Cargo,
    ResourceDto Credit,
    DateTime DepartAt,
    DateTime ArriveAt,
    TransportStatus Status,
    bool Mine);

public sealed record MarketsOverviewDto(
    long CityId,
    DateTime ServerTime,
    ResourceDto Resources,
    int ResourceCap,
    int CargoCap,
    double TaxRate,
    int MinAmount,
    MarketValueDto Values,
    IReadOnlyList<MarketRateDto> Rates,
    IReadOnlyList<MarketItemDto> Markets,
    IReadOnlyList<TransportDto> Transports);

public sealed record TransportCompleteDto(
    long TransportId,
    TransportKind Kind,
    ResourceDto Credited,
    ResourceDto Overflow,
    ResourceDto Resources,
    int ResourceCap,
    string Summary);
