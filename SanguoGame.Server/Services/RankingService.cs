using FreeSql;
using SanguoGame.Core;
using SanguoGame.Core.Social;
using SanguoGame.Infrastructure.Entities;
using SanguoGame.Server.Contracts;

namespace SanguoGame.Server.Services;

public sealed class RankingService
{
    private readonly IFreeSql _orm;

    public RankingService(IFreeSql orm)
    {
        _orm = orm;
    }

    public async Task<RankingDto> GetAsync(long accountId, string type, CancellationToken cancellationToken)
    {
        if (!TryParseType(type, out var rankingType))
        {
            throw new BizException(ErrorCodes.ValidationFailed, "未知排行类型");
        }

        var myCity = await LoadMyCityAsync(accountId, cancellationToken);
        var cities = await _orm.Select<CityEntity>().ToListAsync(cancellationToken);
        var characters = (await _orm.Select<CharacterEntity>().ToListAsync(cancellationToken))
            .ToDictionary(c => c.Id);
        var accounts = (await _orm.Select<AccountEntity>().ToListAsync(cancellationToken))
            .ToDictionary(a => a.Id);
        var buildings = await _orm.Select<BuildingEntity>().ToListAsync(cancellationToken);
        var levelsByCity = buildings
            .GroupBy(b => b.CityId)
            .ToDictionary(g => g.Key, g => g.Sum(b => b.Level));
        var members = await _orm.Select<AllianceMemberEntity>().ToListAsync(cancellationToken);
        var alliances = (await _orm.Select<AllianceEntity>().ToListAsync(cancellationToken))
            .ToDictionary(a => a.Id);
        var allianceNameByCharacter = members
            .Where(m => alliances.ContainsKey(m.AllianceId))
            .ToDictionary(m => m.CharacterId, m => alliances[m.AllianceId].Name);

        Dictionary<long, int> lootByCity = [];
        if (rankingType == RankingType.Loot)
        {
            var reports = await _orm.Select<BattleReportEntity>()
                .Where(r => r.AttackerWon)
                .ToListAsync(cancellationToken);
            lootByCity = reports
                .GroupBy(r => r.AttackerCityId)
                .ToDictionary(
                    g => g.Key,
                    g => g.Sum(r => r.LootGrain + r.LootWood + r.LootIron + r.LootCopper));
        }

        var scored = new List<(CityEntity City, CharacterEntity Character, AccountEntity Account, int Score)>();
        foreach (var city in cities)
        {
            if (!characters.TryGetValue(city.CharacterId, out var character) ||
                !accounts.TryGetValue(character.AccountId, out var account))
            {
                continue;
            }

            var score = rankingType switch
            {
                RankingType.Power => RankingRules.PowerScore(
                    levelsByCity.GetValueOrDefault(city.Id),
                    CityStats.Troops(city).Total),
                RankingType.Troops => CityStats.Troops(city).Total,
                RankingType.Loot => lootByCity.GetValueOrDefault(city.Id),
                _ => 0
            };
            scored.Add((city, character, account, score));
        }

        var ordered = scored
            .OrderByDescending(row => row.Score)
            .ThenBy(row => row.City.Id)
            .Select((row, index) => new
            {
                Rank = index + 1,
                row.City,
                row.Character,
                row.Account,
                row.Score
            })
            .ToList();

        int? myRank = null;
        var myScore = 0;
        if (myCity is not null)
        {
            var mine = ordered.FirstOrDefault(row => row.City.Id == myCity.Id);
            if (mine is not null)
            {
                myRank = mine.Rank;
                myScore = mine.Score;
            }
        }

        var items = ordered
            .Take(RankingRules.TopSize)
            .Select(row => new RankingEntryDto(
                row.Rank,
                row.City.Id,
                row.Character.Name,
                row.City.Name,
                row.Score,
                row.Account.IsAi,
                allianceNameByCharacter.GetValueOrDefault(row.Character.Id)))
            .ToList();

        return new RankingDto(rankingType, DateTime.UtcNow, myRank, myScore, items);
    }

    private async Task<CityEntity?> LoadMyCityAsync(long accountId, CancellationToken cancellationToken)
    {
        var character = await _orm.Select<CharacterEntity>()
            .Where(c => c.AccountId == accountId)
            .FirstAsync(cancellationToken);
        if (character is null)
        {
            return null;
        }

        return await _orm.Select<CityEntity>()
            .Where(c => c.CharacterId == character.Id)
            .FirstAsync(cancellationToken);
    }

    private static bool TryParseType(string value, out RankingType type)
    {
        if (value.Equals("power", StringComparison.OrdinalIgnoreCase))
        {
            type = RankingType.Power;
            return true;
        }

        if (value.Equals("troops", StringComparison.OrdinalIgnoreCase))
        {
            type = RankingType.Troops;
            return true;
        }

        if (value.Equals("loot", StringComparison.OrdinalIgnoreCase))
        {
            type = RankingType.Loot;
            return true;
        }

        type = default;
        return false;
    }
}
