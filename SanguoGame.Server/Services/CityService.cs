using FreeSql;
using Microsoft.Extensions.Options;
using SanguoGame.Core;
using SanguoGame.Core.Buildings;
using SanguoGame.Core.World;
using SanguoGame.Infrastructure;
using SanguoGame.Infrastructure.Entities;
using SanguoGame.Server.Contracts;

namespace SanguoGame.Server.Services;

public sealed class CityService
{
    private const int InsertRetries = 8;

    private static readonly CityZonesDto EmptyZones = new([], [], []);

    private readonly IFreeSql _orm;
    private readonly WorldMapOptions _map;

    public CityService(IFreeSql orm, IOptions<WorldMapOptions> map)
    {
        _orm = orm;
        _map = map.Value;
    }

    public async Task<CityResponse> FoundAsync(long accountId, CancellationToken cancellationToken)
    {
        var character = await RequireCharacterAsync(accountId, cancellationToken);
        if (await _orm.Select<CityEntity>().AnyAsync(c => c.CharacterId == character.Id, cancellationToken))
        {
            throw new BizException(ErrorCodes.CityExists, "该角色已有主城");
        }

        for (var round = 0; round < InsertRetries; round++)
        {
            var cell = await MapPlacement.TryPickEmptyCellAsync(
                _map.Width,
                _map.Height,
                _map.PlacementMaxAttempts,
                (x, y, ct) => WorldOccupancy.IsOccupiedAsync(_orm, x, y, ct),
                cancellationToken);
            if (cell is null)
            {
                throw new BizException(ErrorCodes.MapFull, "暂无空地可建城");
            }

            var city = new CityEntity
            {
                CharacterId = character.Id,
                Name = $"{character.Name}的城",
                X = cell.Value.X,
                Y = cell.Value.Y,
                Grain = InnerBuildingCatalog.StartingResource,
                Wood = InnerBuildingCatalog.StartingResource,
                Iron = InnerBuildingCatalog.StartingResource,
                Copper = InnerBuildingCatalog.StartingResource,
                CreatedAt = DateTime.UtcNow
            };

            try
            {
                var placed = await WorldOccupancy.TryInsertOccupiedAsync(
                    _orm,
                    cell.Value.X,
                    cell.Value.Y,
                    MapCellKinds.City,
                    async transaction =>
                    {
                        city.Id = await _orm.Insert(city)
                            .WithTransaction(transaction)
                            .ExecuteIdentityAsync(cancellationToken);
                        return city.Id;
                    },
                    cancellationToken);
                if (placed is null)
                {
                    continue;
                }

                city.Id = placed.Value;
                return ToResponse(city);
            }
            catch (Exception ex) when (DbErrors.IsUniqueViolation(ex))
            {
                if (await _orm.Select<CityEntity>().AnyAsync(c => c.CharacterId == character.Id, cancellationToken))
                {
                    throw new BizException(ErrorCodes.CityExists, "该角色已有主城");
                }
            }
        }

        throw new BizException(ErrorCodes.MapFull, "暂无空地可建城");
    }

    public async Task<CityResponse> GetMineAsync(long accountId, CancellationToken cancellationToken)
    {
        var character = await RequireCharacterAsync(accountId, cancellationToken);
        var city = await _orm.Select<CityEntity>()
            .Where(c => c.CharacterId == character.Id)
            .FirstAsync(cancellationToken);
        if (city is null)
        {
            throw new BizException(ErrorCodes.NotFound, "尚未建立主城");
        }

        return ToResponse(city);
    }

    private async Task<CharacterEntity> RequireCharacterAsync(long accountId, CancellationToken cancellationToken)
    {
        var character = await _orm.Select<CharacterEntity>()
            .Where(c => c.AccountId == accountId)
            .FirstAsync(cancellationToken);
        if (character is null)
        {
            throw new BizException(ErrorCodes.NotFound, "尚未创建角色");
        }

        return character;
    }

    private static CityResponse ToResponse(CityEntity city) =>
        new(city.Id, city.CharacterId, city.Name, city.X, city.Y, city.CreatedAt, EmptyZones);
}
