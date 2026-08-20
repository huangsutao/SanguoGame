using SanguoGame.Core;
using SanguoGame.Core.Market;
using SanguoGame.Core.Social;
using SanguoGame.Server.Contracts;
using Xunit;

namespace SanguoGame.Tests.Api;

[Collection("api")]
public sealed class MarketApiTests
{
    private readonly GameApiFactory _factory;

    public MarketApiTests(GameApiFactory factory)
    {
        _factory = factory;
    }

    [SkippableFact]
    public async Task Trade_DeductsNow_CreditsAfterArrival()
    {
        SkipIfUnavailable();
        var api = new ApiClient(_factory.CreateJsonClient());
        var (cityId, x, y) = await api.RegisterCityAsync();
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
        Assert.Equal(1000, traded.Data?.Resources.Grain);
        Assert.Equal(2000, traded.Data?.Resources.Wood);
        var transport = Assert.Single(traded.Data!.Transports, t => t.FromCityId == cityId);
        Assert.Equal(TransportKind.Market, transport.Kind);
        Assert.Equal(1000, transport.Cargo.Grain);
        Assert.Equal(900, transport.Credit.Wood);
        Assert.True(transport.Mine);
        Assert.Equal(cityId, transport.FromCityId);

        var (_, same) = await api.Post<MarketsOverviewDto>("/api/markets/trade", new
        {
            marketId,
            fromResource = "grain",
            toResource = "grain",
            amount = 1000
        });
        Assert.Equal(ErrorCodes.InvalidTrade, same.Code);

        var (_, tiny) = await api.Post<MarketsOverviewDto>("/api/markets/trade", new
        {
            marketId,
            fromResource = "grain",
            toResource = "wood",
            amount = 99
        });
        Assert.Equal(ErrorCodes.InvalidTrade, tiny.Code);

        await _factory.ForceCompleteTransportsAsync();
        var (_, done) = await api.Get<MarketsOverviewDto>("/api/markets");
        Assert.Equal(1000, done.Data?.Resources.Grain);
        Assert.Equal(2900, done.Data?.Resources.Wood);
        Assert.Empty(done.Data!.Transports);

        var (_, mail) = await api.Get<MailListDto>("/api/mail");
        Assert.Contains(mail.Data!.Items, m => m.Type == MailType.System && m.RelatedType == "transport");
    }

    [SkippableFact]
    public async Task Aid_RequiresAlliance_AndDeliversAfterLeave()
    {
        SkipIfUnavailable();
        var leader = new ApiClient(_factory.CreateJsonClient());
        var lead = await leader.RegisterPlayerAsync("m1");
        var tag = Guid.NewGuid().ToString("N")[..6];
        var (_, created) = await leader.Post<AllianceDetailDto>("/api/alliances", new { name = "运" + tag });
        Assert.Equal(0, created.Code);
        Assert.Contains(created.Data!.Members, m => m.CharacterId == lead.CharacterId && m.CityId == lead.CityId);

        var member = new ApiClient(_factory.CreateJsonClient());
        var guest = await member.RegisterPlayerAsync("m2");

        var (_, unaided) = await member.Post<MarketsOverviewDto>("/api/markets/aid", new
        {
            targetCityId = lead.CityId,
            grain = 200,
            wood = 0,
            iron = 0,
            copper = 0
        });
        Assert.Equal(ErrorCodes.NotInAlliance, unaided.Code);

        var (_, apply) = await member.Post<object?>($"/api/alliances/{created.Data.Id}/apply");
        Assert.Equal(0, apply.Code);
        var (_, pending) = await leader.Get<AlliancePendingDto>("/api/alliances/pending");
        var application = Assert.Single(pending.Data!.Applications);
        var (_, accepted) = await leader.Post<object?>($"/api/alliances/applications/{application.Id}/accept");
        Assert.Equal(0, accepted.Code);

        var (_, self) = await leader.Post<MarketsOverviewDto>("/api/markets/aid", new
        {
            targetCityId = lead.CityId,
            grain = 200,
            wood = 0,
            iron = 0,
            copper = 0
        });
        Assert.Equal(ErrorCodes.CannotAidSelf, self.Code);

        var outsider = new ApiClient(_factory.CreateJsonClient());
        var other = await outsider.RegisterPlayerAsync("m3");
        var (_, blocked) = await outsider.Post<MarketsOverviewDto>("/api/markets/aid", new
        {
            targetCityId = lead.CityId,
            grain = 200,
            wood = 0,
            iron = 0,
            copper = 0
        });
        Assert.Equal(ErrorCodes.NotInAlliance, blocked.Code);

        var (_, strangerAlliance) = await outsider.Post<AllianceDetailDto>("/api/alliances", new { name = "外" + tag });
        Assert.Equal(0, strangerAlliance.Code);
        var (_, notAllied) = await outsider.Post<MarketsOverviewDto>("/api/markets/aid", new
        {
            targetCityId = lead.CityId,
            grain = 200,
            wood = 0,
            iron = 0,
            copper = 0
        });
        Assert.Equal(ErrorCodes.NotAlliedTransport, notAllied.Code);

        var (_, sent) = await leader.Post<MarketsOverviewDto>("/api/markets/aid", new
        {
            targetCityId = guest.CityId,
            grain = 200,
            wood = 0,
            iron = 0,
            copper = 0
        });
        Assert.Equal(0, sent.Code);
        Assert.Equal(1800, sent.Data?.Resources.Grain);
        var cart = Assert.Single(sent.Data!.Transports, t => t.Kind == TransportKind.Aid);
        Assert.Equal(guest.CityId, cart.ToCityId);
        Assert.Equal(200, cart.Cargo.Grain);

        var (_, leave) = await leader.Post<object?>("/api/alliances/leave");
        Assert.Equal(0, leave.Code);

        await _factory.ForceCompleteTransportsAsync();
        var (_, received) = await member.Get<ArmyOverviewDto>("/api/army");
        Assert.Equal(2200, received.Data?.Resources.Grain);
        var (_, sender) = await leader.Get<ArmyOverviewDto>("/api/army");
        Assert.Equal(1800, sender.Data?.Resources.Grain);
    }

    private void SkipIfUnavailable() =>
        Skip.If(!_factory.Available, _factory.UnavailableReason ?? "需要 PostgreSQL 或 Docker");
}
