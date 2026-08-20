using SanguoGame.Core;
using SanguoGame.Core.Army;
using SanguoGame.Core.Social;
using SanguoGame.Server.Contracts;
using Xunit;

namespace SanguoGame.Tests.Api;

[Collection("api")]
public sealed class GameplayApiTests
{
    private readonly GameApiFactory _factory;

    public GameplayApiTests(GameApiFactory factory)
    {
        _factory = factory;
    }

    [SkippableFact]
    public async Task UpgradePalace_QueueBusy_ThenComplete()
    {
        SkipIfUnavailable();
        var api = new ApiClient(_factory.CreateJsonClient());
        await api.RegisterCityAsync();

        var (_, first) = await api.Post<BuildingsOverviewDto>("/api/buildings/upgrade", new { buildingType = "palace" });
        Assert.Equal(0, first.Code);
        Assert.NotNull(first.Data?.Queue);
        Assert.Equal("palace", first.Data.Queue.BuildingType);

        var (_, busy) = await api.Post<BuildingsOverviewDto>("/api/buildings/upgrade", new { buildingType = "palace" });
        Assert.Equal(ErrorCodes.BuildingQueueBusy, busy.Code);

        await _factory.ForceCompleteBuildingsAsync();
        var (_, done) = await api.Get<BuildingsOverviewDto>("/api/buildings");
        var palace = done.Data?.Buildings.Single(b => b.Type == "palace");
        Assert.Equal(1, palace?.Level);
        Assert.Null(done.Data?.Queue);
    }

    [SkippableFact]
    public async Task Recruit_RequiresBarracks_ThenSucceeds()
    {
        SkipIfUnavailable();
        var api = new ApiClient(_factory.CreateJsonClient());
        await api.RegisterCityAsync();

        var (_, blocked) = await api.Post<ArmyOverviewDto>("/api/army/recruit", new { troopType = "infantry", count = 5 });
        Assert.Equal(ErrorCodes.BarracksRequired, blocked.Code);

        await UpgradeAndFinish(api, "palace");
        await UpgradeAndFinish(api, "palace");
        await UpgradeAndFinish(api, "barracks");

        var (_, recruited) = await api.Post<ArmyOverviewDto>("/api/army/recruit", new { troopType = "infantry", count = 5 });
        Assert.Equal(0, recruited.Code);
        Assert.NotNull(recruited.Data?.RecruitQueue);
        Assert.Equal(0, recruited.Data?.Troops.Infantry);
        await _factory.ForceCompleteRecruitsAsync();
        var (_, ready) = await api.Get<ArmyOverviewDto>("/api/army");
        Assert.Equal(5, ready.Data?.Troops.Infantry);
        Assert.Null(ready.Data?.RecruitQueue);
        Assert.Contains(ready.Data!.TroopTypes, t => t.Type == "infantry" && t.RequireBarracksLevel == 1);
    }

    [SkippableFact]
    public async Task MarchOutpost_WritesReportAndMail()
    {
        SkipIfUnavailable();
        var api = new ApiClient(_factory.CreateJsonClient());
        var (_, x, y) = await api.RegisterCityAsync();
        await UpgradeAndFinish(api, "palace");
        await UpgradeAndFinish(api, "palace");
        await UpgradeAndFinish(api, "barracks");
        var (_, recruited) = await api.Post<ArmyOverviewDto>("/api/army/recruit", new { troopType = "infantry", count = 20 });
        Assert.Equal(0, recruited.Code);
        await _factory.ForceCompleteRecruitsAsync();

        var (ox, oy) = await _factory.PickEmptyCellAsync(x, y);
        var outpostId = await _factory.InsertOutpostAsync(ox, oy);
        var (_, marched) = await api.Post<ArmyOverviewDto>("/api/army/march", new
        {
            targetType = "outpost",
            targetId = outpostId,
            infantry = 20,
            archer = 0,
            cavalry = 0
        });
        Assert.Equal(0, marched.Code);
        Assert.Contains(marched.Data!.Marches, m => m.Status == MarchStatus.Marching);

        await _factory.ForceCompleteMarchesAsync();
        var (_, reports) = await api.Get<PagedResult<BattleReportDto>>("/api/reports?page=1&pageSize=20");
        Assert.Equal(0, reports.Code);
        Assert.True(reports.Data?.Total >= 1);
        var (_, mail) = await api.Get<MailListDto>("/api/mail");
        Assert.Equal(0, mail.Code);
        Assert.True(mail.Data?.UnreadCount >= 1);
        Assert.Contains(mail.Data!.Items, m => m.Type == MailType.Battle);

        var id = mail.Data.Items[0].Id;
        var (_, read) = await api.Post<object?>($"/api/mail/{id}/read");
        Assert.Equal(0, read.Code);
        var (_, mail2) = await api.Get<MailListDto>("/api/mail");
        Assert.Equal(0, mail2.Data!.UnreadCount);
    }

    [SkippableFact]
    public async Task World_And_Collect_AreReachable()
    {
        SkipIfUnavailable();
        var api = new ApiClient(_factory.CreateJsonClient());
        await api.RegisterCityAsync();
        var (_, world) = await api.Get<WorldDto>("/api/world");
        Assert.Equal(0, world.Code);
        Assert.Equal(40, world.Data?.Width);
        var (_, fields) = await api.Post<FieldsCollectDto>("/api/fields/collect", new { });
        Assert.Equal(0, fields.Code);
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
