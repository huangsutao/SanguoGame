using FreeSql;
using Microsoft.Extensions.Options;
using SanguoGame.Core.Army;
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

        await RecoverDueOutpostsAsync(cancellationToken);

        var now = DateTime.UtcNow;
        var cityRows = await _orm.Select<CityEntity>().ToListAsync(cancellationToken);
        var characters = (await _orm.Select<CharacterEntity>().ToListAsync(cancellationToken))
            .ToDictionary(c => c.Id);
        var accounts = (await _orm.Select<AccountEntity>().ToListAsync(cancellationToken))
            .ToDictionary(a => a.Id);

        var cityDtos = cityRows.Select(city =>
        {
            var isAi = characters.TryGetValue(city.CharacterId, out var ch)
                && accounts.TryGetValue(ch.AccountId, out var acc)
                && acc.IsAi;
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
            new WorldOutpostDto(o.Id, o.Type, o.Name, o.X, o.Y, o.Garrison)).ToList();

        var marches = await _orm.Select<MarchEntity>()
            .Where(m => m.Status == MarchStatus.Marching)
            .ToListAsync(cancellationToken);
        var marchDtos = marches.Select(m =>
            ArmyService.MapMarch(m, myCityId is long id && m.FromCityId == id)).ToList();

        return new WorldDto(_map.Width, _map.Height, now, origin, cityDtos, outpostDtos, marchDtos);
    }

    public async Task RecoverDueOutpostsAsync(CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var due = await _orm.Select<OutpostEntity>()
            .Where(o => o.RecoverAt != null && o.RecoverAt <= now)
            .ToListAsync(cancellationToken);
        foreach (var outpost in due)
        {
            var def = OutpostCatalog.Require(outpost.Type);
            outpost.Garrison = def.Garrison;
            outpost.RecoverAt = null;
            await _orm.Update<OutpostEntity>()
                .SetSource(outpost)
                .UpdateColumns(o => new { o.Garrison, o.RecoverAt })
                .ExecuteAffrowsAsync(cancellationToken);
        }
    }
}
