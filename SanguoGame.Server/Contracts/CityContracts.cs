namespace SanguoGame.Server.Contracts;

public sealed record CityZonesDto(
    IReadOnlyList<object> Inner,
    IReadOnlyList<object> Wall,
    IReadOnlyList<object> Outer);

public sealed record CityResponse(
    long Id,
    long CharacterId,
    string Name,
    int X,
    int Y,
    DateTime CreatedAt,
    CityZonesDto Zones);
