using SanguoGame.Core;
using SanguoGame.Core.Social;
using SanguoGame.Server.Contracts;
using Xunit;

namespace SanguoGame.Tests.Api;

[Collection("api")]
public sealed class SocialApiTests
{
    private readonly GameApiFactory _factory;

    public SocialApiTests(GameApiFactory factory)
    {
        _factory = factory;
    }

    [SkippableFact]
    public async Task Rankings_IncludeSelfAfterFounding()
    {
        SkipIfUnavailable();
        var api = new ApiClient(_factory.CreateJsonClient());
        var (cityId, _, _) = await api.RegisterCityAsync();
        var (_, power) = await api.Get<RankingDto>("/api/rankings?type=power");
        Assert.Equal(0, power.Code);
        Assert.NotNull(power.Data?.MyRank);
        Assert.True(power.Data!.Items.Count <= RankingRules.TopSize);
        var mine = power.Data.Items.FirstOrDefault(item => item.CityId == cityId);
        if (power.Data.MyRank <= RankingRules.TopSize)
        {
            Assert.NotNull(mine);
            Assert.Equal(power.Data.MyRank, mine.Rank);
        }
        else
        {
            Assert.Null(mine);
        }

        var (_, bad) = await api.Get<RankingDto>("/api/rankings?type=unknown");
        Assert.Equal(ErrorCodes.ValidationFailed, bad.Code);
    }

    [SkippableFact]
    public async Task Alliance_CreateApplyAccept_BlocksFriendlyFire()
    {
        SkipIfUnavailable();
        var leader = new ApiClient(_factory.CreateJsonClient());
        await leader.RegisterCityAsync("a");
        var tag = Guid.NewGuid().ToString("N")[..6];
        var (_, created) = await leader.Post<AllianceDetailDto>("/api/alliances", new { name = "盟" + tag });
        Assert.Equal(0, created.Code);
        Assert.Equal(1, created.Data?.MemberCount);

        var member = new ApiClient(_factory.CreateJsonClient());
        var (memberCityId, _, _) = await member.RegisterCityAsync("b");
        var (_, apply) = await member.Post<object?>($"/api/alliances/{created.Data!.Id}/apply");
        Assert.Equal(0, apply.Code);

        var (_, pending) = await leader.Get<AlliancePendingDto>("/api/alliances/pending");
        var application = Assert.Single(pending.Data!.Applications);
        var (_, accepted) = await leader.Post<object?>($"/api/alliances/applications/{application.Id}/accept");
        Assert.Equal(0, accepted.Code);

        var (_, mine) = await member.Get<AllianceDetailDto>("/api/alliances/me");
        Assert.Equal(2, mine.Data?.MemberCount);

        await UpgradeArmy(leader);
        var (_, march) = await leader.Post<ArmyOverviewDto>("/api/army/march", new
        {
            targetType = "city",
            targetId = memberCityId,
            infantry = 1,
            archer = 0,
            cavalry = 0
        });
        Assert.Equal(ErrorCodes.SameAlliance, march.Code);
    }

    [SkippableFact]
    public async Task Alliance_InviteFlow()
    {
        SkipIfUnavailable();
        var leader = new ApiClient(_factory.CreateJsonClient());
        await leader.RegisterCityAsync("c");
        var tag = Guid.NewGuid().ToString("N")[..6];
        var (_, created) = await leader.Post<AllianceDetailDto>("/api/alliances", new { name = "邀" + tag });
        Assert.Equal(0, created.Code);

        var guest = new ApiClient(_factory.CreateJsonClient());
        await guest.RegisterCityAsync("d");
        var (_, session) = await guest.Get<SessionResponse>("/api/auth/me");
        var (_, invited) = await leader.Post<object?>("/api/alliances/invite", new { characterName = session.Data!.Character!.Name });
        Assert.Equal(0, invited.Code);

        var (_, pending) = await guest.Get<AlliancePendingDto>("/api/alliances/pending");
        var invite = Assert.Single(pending.Data!.Invites);
        var (_, accept) = await guest.Post<object?>($"/api/alliances/invites/{invite.Id}/accept");
        Assert.Equal(0, accept.Code);
        var (_, mine) = await guest.Get<AllianceDetailDto>("/api/alliances/me");
        Assert.Equal(created.Data!.Id, mine.Data?.Id);
    }

    private async Task UpgradeArmy(ApiClient api)
    {
        await UpgradeAndFinish(api, "palace");
        await UpgradeAndFinish(api, "palace");
        await UpgradeAndFinish(api, "barracks");
        var (_, recruited) = await api.Post<ArmyOverviewDto>("/api/army/recruit", new { troopType = "infantry", count = 5 });
        Assert.Equal(0, recruited.Code);
    }

    private async Task UpgradeAndFinish(ApiClient api, string buildingType)
    {
        var (_, body) = await api.Post<BuildingsOverviewDto>("/api/buildings/upgrade", new { buildingType });
        Assert.Equal(0, body.Code);
        await _factory.ForceCompleteBuildingsAsync();
    }

    private void SkipIfUnavailable() =>
        Skip.If(!_factory.Available, _factory.UnavailableReason ?? "需要 PostgreSQL 或 Docker");
}
