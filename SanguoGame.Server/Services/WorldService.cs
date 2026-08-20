using FreeSql;
using Microsoft.Extensions.Options;
using SanguoGame.Core.Army;
using SanguoGame.Core.Market;
using SanguoGame.Core.World;
using SanguoGame.Infrastructure.Entities;
using SanguoGame.Server.Contracts;

namespace SanguoGame.Server.Services;

public sealed class WorldService
{
    private readonly IFreeSql _orm;
    private readonly WorldMapOptions _map;

    public WorldService(IFreeSql orm, IOptions<WorldMapOptions> map)
    {
        _orm = orm;
        _map = map.Value;
    }

    public async Task<WorldDto> GetAsync(long accountId, CancellationToken cancellationToken)
    {
        long? myCityId = null;
        var origin = new WorldOriginDto(0, 0);
        var character = await _orm.Select<CharacterEntity>()
            .Where(c => c.AccountId == accountId)
            .FirstAsync(cancellationToken);
        if (character is not null)
        {
            var mine = await _orm.Select<CityEntity>()
                .Where(c => c.CharacterId == character.Id)
                .FirstAsync(cancellationToken);
            if (mine is not null)
            {
                myCityId = mine.Id;
                origin = new WorldOriginDto(mine.X, mine.Y);
            }
        }

        var now = DateTime.UtcNow;
        var cityRows = await _orm.Select<CityEntity>().ToListAsync(cancellationToken);
        var characterOwners = (await _orm.Select<CharacterEntity>().ToListAsync(cancellationToken))
            .ToDictionary(c => c.Id, c => c.AccountId);
        var aiAccountIds = (await _orm.Select<AccountEntity>()
                .Where(a => a.IsAi)
                .ToListAsync(cancellationToken))
            .Select(a => a.Id)
            .ToHashSet();

        var cityDtos = cityRows.Select(city =>
        {
            var isAi = characterOwners.TryGetValue(city.CharacterId, out var accountId)
                && aiAccountIds.Contains(accountId);
            var owner = myCityId == city.Id ? "self" : isAi ? "ai" : "player";
            return new WorldCityDto(
                city.Id,
                city.Name,
                city.X,
                city.Y,
                owner,
                owner != "self" && CityStats.IsProtected(city, now));
        }).ToList();

        var outposts = await _orm.Select<OutpostEntity>().ToListAsync(cancellationToken);
        var outpostDtos = outposts.Select(o =>
        {
            var garrison = o.Garrison;
            if (o.RecoverAt is { } until && until <= now)
            {
                garrison = OutpostCatalog.Require(o.Type).Garrison;
            }

            return new WorldOutpostDto(o.Id, o.Type, o.Name, o.X, o.Y, garrison);
        }).ToList();

        var marches = await _orm.Select<MarchEntity>()
            .Where(m => m.Status == MarchStatus.Marching)
            .ToListAsync(cancellationToken);
        var marchDtos = marches.Select(m =>
        {
            var mine = myCityId is long id && m.FromCityId == id;
            return ArmyService.MapMarch(m, mine, includeTroops: mine);
        }).ToList();

        var markets = await _orm.Select<MarketEntity>().ToListAsync(cancellationToken);
        var marketDtos = markets.Select(m => new WorldMarketDto(m.Id, m.Name, m.X, m.Y)).ToList();

        var transports = await _orm.Select<TransportEntity>()
            .Where(t => t.Status == TransportStatus.InTransit)
            .ToListAsync(cancellationToken);
        var transportDtos = transports.Select(t =>
        {
            var mine = myCityId is long id && (t.FromCityId == id || t.ToCityId == id);
            return TransportService.MapTransport(t, mine);
        }).ToList();

        return new WorldDto(_map.Width, _map.Height, now, origin, cityDtos, outpostDtos, marchDtos, marketDtos, transportDtos);
    }

    public async Task RecoverDueOutpostsAsync(CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        using var conn = await _orm.Ado.MasterPool.GetAsync();
        await using var transaction = await conn.Value.BeginTransactionAsync(cancellationToken);
        try
        {
            var due = await _orm.Select<OutpostEntity>()
                .WithTransaction(transaction)
                .ForUpdate()
                .Where(o => o.RecoverAt != null && o.RecoverAt <= now)
                .ToListAsync(cancellationToken);
            foreach (var outpost in due)
            {
                var def = OutpostCatalog.Require(outpost.Type);
                outpost.Garrison = def.Garrison;
                outpost.RecoverAt = null;
                await _orm.Update<OutpostEntity>()
                    .WithTransaction(transaction)
                    .SetSource(outpost)
                    .UpdateColumns(o => new { o.Garrison, o.RecoverAt })
                    .ExecuteAffrowsAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }
}
