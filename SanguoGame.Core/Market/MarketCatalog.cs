namespace SanguoGame.Core.Market;

public enum TransportKind
{
    Market = 0,
    Aid = 1
}

public enum TransportStatus
{
    InTransit = 0,
    Settled = 1
}

public static class MarketCatalog
{
    public const double TaxRate = 0.10;
    public const int MinAmount = 100;
    public const int QuoteSampleAmount = 100;
    public const int BaseCargoCap = 2000;
    public const int CargoCapPerWarehouseLevel = 1000;

    public static IReadOnlyList<string> Resources { get; } = ["grain", "wood", "iron", "copper"];

    public static string DisplayName(string resource) => Normalize(resource) switch
    {
        "grain" => "粮",
        "wood" => "木",
        "iron" => "铁",
        "copper" => "铜",
        _ => resource
    };

    public static bool IsResource(string? resource) =>
        resource is not null && Resources.Contains(Normalize(resource));

    public static string Normalize(string resource) => resource.Trim().ToLowerInvariant();

    public static int Value(string resource) => Normalize(resource) switch
    {
        "grain" => 10,
        "wood" => 10,
        "iron" => 15,
        "copper" => 20,
        _ => throw new ArgumentOutOfRangeException(nameof(resource), resource, "未知资源")
    };

    public static int CargoCap(int warehouseLevel) =>
        BaseCargoCap + CargoCapPerWarehouseLevel * Math.Max(0, warehouseLevel);

    public static int Quote(string fromResource, string toResource, int fromAmount)
    {
        if (!IsResource(fromResource) || !IsResource(toResource))
        {
            return 0;
        }

        var from = Normalize(fromResource);
        var to = Normalize(toResource);
        if (from == to || fromAmount < MinAmount)
        {
            return 0;
        }

        return (int)Math.Floor(fromAmount * Value(from) * (1d - TaxRate) / Value(to));
    }
}
