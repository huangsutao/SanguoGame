using SanguoGame.Core;
using SanguoGame.Core.Army;
using SanguoGame.Core.Daily;
using SanguoGame.Core.Social;
using SanguoGame.Server.Contracts;
using Xunit;

namespace SanguoGame.Tests.Api;

[Collection("api")]
public sealed class DailyScoutApiTests
{
    private readonly GameApiFactory _factory;

    public DailyScoutApiTests(GameApiFactory factory)
    {
        _factory = factory;
    }

    [SkippableFact]
    public async Task Daily_UpgradeCollectRecruitTradeRaid_ThenBundle()
    {
        SkipIfUnavailable();
        var api = new ApiClient(_factory.CreateJsonClient());
        var (cityId, x, y) = await api.RegisterCityAsync();

        var (_, listed) = await api.Get<DailyOverviewDto>("/api/daily");
        Assert.Equal(0, listed.Code);
        Assert.Equal(6, listed.Data!.Missions.Count);
        Assert.All(listed.Data.Missions, m => Assert.False(m.Claimed));

        var (_, early) = await api.Post<DailyOverviewDto>("/api/daily/claim", new { missionType = "upgrade" });
        Assert.Equal(ErrorCodes.DailyNotClaimable, early.Code);

        var (_, palace) = await api.Post<BuildingsOverviewDto>("/api/buildings/upgrade", new { buildingType = "palace" });
        Assert.Equal(0, palace.Code);
        await _factory.ForceCompleteBuildingsAsync();

        var (_, afterUpgrade) = await api.Get<DailyOverviewDto>("/api/daily");
        var upgrade = afterUpgrade.Data!.Missions.Single(m => m.Type == "upgrade");
        Assert.Equal(1, upgrade.Progress);
        var woodBefore = afterUpgrade.Data.Resources.Wood;
        var (_, claimedUpgrade) = await api.Post<DailyOverviewDto>("/api/daily/claim", new { missionType = "upgrade" });
        Assert.Equal(0, claimedUpgrade.Code);
        Assert.True(claimedUpgrade.Data!.Missions.Single(m => m.Type == "upgrade").Claimed);
        Assert.Equal(woodBefore + 200, claimedUpgrade.Data.Resources.Wood);
        var (_, again) = await api.Post<DailyOverviewDto>("/api/daily/claim", new { missionType = "upgrade" });
        Assert.Equal(ErrorCodes.DailyNotClaimable, again.Code);

        await _factory.SetBuildingLevelAsync(cityId, "farm", 1);
        await _factory.BackdateFieldAsync(cityId, "farm", TimeSpan.FromHours(2));
        var (_, collected) = await api.Post<FieldsCollectDto>("/api/fields/collect", new { });
        Assert.Equal(0, collected.Code);
        Assert.True(collected.Data!.Collected.Grain > 0);
        var (_, claimCollect) = await api.Post<DailyOverviewDto>("/api/daily/claim", new { missionType = "collect" });
        Assert.Equal(0, claimCollect.Code);

        await _factory.SetBuildingLevelAsync(cityId, "palace", 2);
        await _factory.SetBuildingLevelAsync(cityId, "barracks", 1);
        var (_, recruited) = await api.Post<ArmyOverviewDto>("/api/army/recruit", new { troopType = "infantry", count = 10 });
        Assert.Equal(0, recruited.Code);
        var (_, claimRecruit) = await api.Post<DailyOverviewDto>("/api/daily/claim", new { missionType = "recruit" });
        Assert.Equal(0, claimRecruit.Code);

        var (mx, my) = await _factory.PickEmptyCellAsync(x, y);
        var marketId = await _factory.InsertMarketAsync(mx, my);
        var (_, traded) = await api.Post<MarketsOverviewDto>("/api/markets/trade", new
        {
            marketId,
            fromResource = "grain",
            toResource = "wood",
            amount = 1000
        });
        Assert.Equal(0, traded.Code);
        var (_, claimTrade) = await api.Post<DailyOverviewDto>("/api/daily/claim", new { missionType = "trade" });
        Assert.Equal(0, claimTrade.Code);

        await _factory.SetCityTroopsAsync(cityId, 60);
        var (ox, oy) = await _factory.PickEmptyCellAsync(x, y);
        var outpostId = await _factory.InsertOutpostAsync(ox, oy);
        var (_, marched) = await api.Post<ArmyOverviewDto>("/api/army/march", new
        {
            targetType = "outpost",
            targetId = outpostId,
            infantry = 60,
            archer = 0,
            cavalry = 0
        });
        Assert.Equal(0, marched.Code);
        await _factory.ForceCompleteMarchesAsync();
        var (_, reports) = await api.Get<PagedResult<BattleReportDto>>("/api/reports?page=1&pageSize=5");
        Assert.True(reports.Data!.Items[0].AttackerWon);
        var (_, claimRaid) = await api.Post<DailyOverviewDto>("/api/daily/claim", new { missionType = "raid" });
        Assert.Equal(0, claimRaid.Code);

        var bundle = claimRaid.Data!.Missions.Single(m => m.Type == "bundle");
        Assert.Equal(5, bundle.Progress);
        var grainBefore = claimRaid.Data.Resources.Grain;
        var (_, claimBundle) = await api.Post<DailyOverviewDto>("/api/daily/claim", new { missionType = "bundle" });
        Assert.Equal(0, claimBundle.Code);
        Assert.True(claimBundle.Data!.Missions.Single(m => m.Type == "bundle").Claimed);
        Assert.Equal(grainBefore + 400, claimBundle.Data.Resources.Grain);
    }

    [SkippableFact]
    public async Task Scout_WritesMail_ReturnsInfantry_RejectsSelf()
    {
        SkipIfUnavailable();
        var api = new ApiClient(_factory.CreateJsonClient());
        var (cityId, x, y) = await api.RegisterCityAsync();
        await _factory.SetBuildingLevelAsync(cityId, "barracks", 1);
        await _factory.SetCityTroopsAsync(cityId, 3);

        var (_, self) = await api.Post<ArmyOverviewDto>("/api/army/scout", new { targetType = "city", targetId = cityId });
        Assert.Equal(ErrorCodes.ScoutNotAllowed, self.Code);

        var (ox, oy) = await _factory.PickEmptyCellAsync(x, y);
        var outpostId = await _factory.InsertOutpostAsync(ox, oy, garrison: 40);
        var (_, scouted) = await api.Post<ArmyOverviewDto>("/api/army/scout", new
        {
            targetType = "outpost",
            targetId = outpostId
        });
        Assert.Equal(0, scouted.Code);
        Assert.Equal(2, scouted.Data!.Troops.Infantry);
        var scoutMarch = Assert.Single(scouted.Data.Marches, m => m.Kind == MarchKind.Scout);
        Assert.Equal(1, scoutMarch.Troops?.Infantry);

        var (_, world) = await api.Get<WorldDto>("/api/world");
        Assert.Contains(world.Data!.Marches, m => m.Id == scoutMarch.Id && m.Kind == MarchKind.Scout);

        await _factory.ForceCompleteMarchesAsync();
        var (_, army) = await api.Get<ArmyOverviewDto>("/api/army");
        Assert.Equal(3, army.Data!.Troops.Infantry);
        Assert.Empty(army.Data.Marches);

        var (_, mail) = await api.Get<MailListDto>("/api/mail");
        var report = Assert.Single(mail.Data!.Items, m => m.Type == MailType.Scout);
        Assert.Contains("驻军 40", report.Body);
        Assert.Equal("march", report.RelatedType);
        Assert.Equal(scoutMarch.Id, report.RelatedId);

        var (_, reports) = await api.Get<PagedResult<BattleReportDto>>("/api/reports?page=1&pageSize=5");
        Assert.Equal(0, reports.Data!.Total);
    }

    [SkippableFact]
    public async Task Scout_WithoutBarracksOrTroops_Fails()
    {
        SkipIfUnavailable();
        var api = new ApiClient(_factory.CreateJsonClient());
        var (cityId, x, y) = await api.RegisterCityAsync();
        var (ox, oy) = await _factory.PickEmptyCellAsync(x, y);
        var outpostId = await _factory.InsertOutpostAsync(ox, oy);

        var (_, noCamp) = await api.Post<ArmyOverviewDto>("/api/army/scout", new
        {
            targetType = "outpost",
            targetId = outpostId
        });
        Assert.Equal(ErrorCodes.BarracksRequired, noCamp.Code);

        await _factory.SetBuildingLevelAsync(cityId, "barracks", 1);
        var (_, noTroop) = await api.Post<ArmyOverviewDto>("/api/army/scout", new
        {
            targetType = "outpost",
            targetId = outpostId
        });
        Assert.Equal(ErrorCodes.InsufficientTroops, noTroop.Code);
    }

    [SkippableFact]
    public async Task Scout_Market_And_RaidLoss_And_OtherCityMail()
    {
        SkipIfUnavailable();
        var api = new ApiClient(_factory.CreateJsonClient());
        var (cityId, x, y) = await api.RegisterCityAsync();
        await _factory.SetBuildingLevelAsync(cityId, "barracks", 1);
        await _factory.SetCityTroopsAsync(cityId, 4);

        var (mx, my) = await _factory.PickEmptyCellAsync(x, y);
        var marketId = await _factory.InsertMarketAsync(mx, my);
        var (_, market) = await api.Post<ArmyOverviewDto>("/api/army/scout", new
        {
            targetType = "market",
            targetId = marketId
        });
        Assert.Equal(ErrorCodes.ScoutNotAllowed, market.Code);

        var other = new ApiClient(_factory.CreateJsonClient());
        var target = await other.RegisterPlayerAsync("sc");
        await _factory.SetCityTroopsAsync(target.CityId, 7, 2, 1);
        var (_, cityScout) = await api.Post<ArmyOverviewDto>("/api/army/scout", new
        {
            targetType = "city",
            targetId = target.CityId
        });
        Assert.Equal(0, cityScout.Code);
        await _factory.ForceCompleteMarchesAsync();
        var (_, mail) = await api.Get<MailListDto>("/api/mail");
        var report = Assert.Single(mail.Data!.Items, m => m.Type == MailType.Scout);
        Assert.Contains("驻城 步7", report.Body);
        Assert.DoesNotContain(target.Username, report.Body);
        var (_, otherMail) = await other.Get<MailListDto>("/api/mail");
        Assert.DoesNotContain(otherMail.Data!.Items, m => m.Type == MailType.Scout);

        var (ox, oy) = await _factory.PickEmptyCellAsync(x, y);
        var outpostId = await _factory.InsertOutpostAsync(ox, oy, garrison: 40);
        var (_, marched) = await api.Post<ArmyOverviewDto>("/api/army/march", new
        {
            targetType = "outpost",
            targetId = outpostId,
            infantry = 1,
            archer = 0,
            cavalry = 0
        });
        Assert.Equal(0, marched.Code);
        await _factory.ForceCompleteMarchesAsync();
        var (_, daily) = await api.Get<DailyOverviewDto>("/api/daily");
        Assert.Equal(0, daily.Data!.Missions.Single(m => m.Type == "raid").Progress);
        var (_, claimRaid) = await api.Post<DailyOverviewDto>("/api/daily/claim", new { missionType = "raid" });
        Assert.Equal(ErrorCodes.DailyNotClaimable, claimRaid.Code);
    }

    private void SkipIfUnavailable() =>
        Skip.If(!_factory.Available, _factory.UnavailableReason ?? "需要 PostgreSQL 或 Docker");
}
