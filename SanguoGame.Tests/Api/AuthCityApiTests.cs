using System.Net;
using SanguoGame.Core;
using SanguoGame.Server.Contracts;
using Xunit;

namespace SanguoGame.Tests.Api;

[Collection("api")]
public sealed class AuthCityApiTests
{
    private readonly GameApiFactory _factory;

    public AuthCityApiTests(GameApiFactory factory)
    {
        _factory = factory;
    }

    [SkippableFact]
    public async Task Ping_ReturnsServerTime()
    {
        SkipIfUnavailable();
        var api = new ApiClient(_factory.CreateJsonClient());
        var (status, body) = await api.Get<PingResponse>("/api/system/ping");
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(0, body.Code);
        Assert.NotNull(body.Data);
        Assert.False(string.IsNullOrWhiteSpace(body.TraceId));
    }

    [SkippableFact]
    public async Task CityMe_WithoutToken_Is40100()
    {
        SkipIfUnavailable();
        var api = new ApiClient(_factory.CreateJsonClient());
        var (status, body) = await api.Get<CityResponse>("/api/city/me");
        Assert.Equal(HttpStatusCode.Unauthorized, status);
        Assert.Equal(ErrorCodes.Unauthorized, body.Code);
    }

    [SkippableFact]
    public async Task Register_Login_Session_And_DuplicateUsername()
    {
        SkipIfUnavailable();
        var api = new ApiClient(_factory.CreateJsonClient());
        var username = "u" + Guid.NewGuid().ToString("N")[..10];
        var tokens = await api.RegisterAsync(username);
        var (ok, session) = await api.Get<SessionResponse>("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, ok);
        Assert.Equal(0, session.Code);
        Assert.Equal(username, session.Data?.Username);
        Assert.Null(session.Data?.Character);

        var other = new ApiClient(_factory.CreateJsonClient());
        var (dupStatus, dup) = await other.Post<TokenResponse>("/api/auth/register", new { username, password = "Passw0rd!" });
        Assert.Equal(HttpStatusCode.OK, dupStatus);
        Assert.Equal(ErrorCodes.UsernameTaken, dup.Code);

        var login = new ApiClient(_factory.CreateJsonClient());
        var (loginStatus, loginBody) = await login.Post<TokenResponse>("/api/auth/login", new { username, password = "Passw0rd!" });
        Assert.Equal(0, loginBody.Code);
        Assert.NotEqual(tokens.RefreshToken, loginBody.Data?.RefreshToken);
        Assert.Equal(HttpStatusCode.OK, loginStatus);
    }

    [SkippableFact]
    public async Task Register_InvalidUsername_Is40001()
    {
        SkipIfUnavailable();
        var api = new ApiClient(_factory.CreateJsonClient());
        var (_, body) = await api.Post<TokenResponse>("/api/auth/register", new { username = "ab", password = "Passw0rd!" });
        Assert.Equal(ErrorCodes.ValidationFailed, body.Code);
    }

    [SkippableFact]
    public async Task Character_And_City_HappyPath_And_Conflicts()
    {
        SkipIfUnavailable();
        var api = new ApiClient(_factory.CreateJsonClient());
        var tag = Guid.NewGuid().ToString("N")[..8];
        await api.RegisterAsync("u" + tag);

        var (_, noCity) = await api.Get<CityResponse>("/api/city/me");
        Assert.Equal(ErrorCodes.NotFound, noCity.Code);

        var (_, created) = await api.Post<CharacterResponse>("/api/characters", new { name = "角" + tag });
        Assert.Equal(0, created.Code);
        var (_, again) = await api.Post<CharacterResponse>("/api/characters", new { name = "另" + tag });
        Assert.Equal(ErrorCodes.CharacterExists, again.Code);

        var (_, city) = await api.Post<CityResponse>("/api/city/found");
        Assert.Equal(0, city.Code);
        Assert.InRange(city.Data!.X, 0, 39);
        var (_, cityAgain) = await api.Post<CityResponse>("/api/city/found");
        Assert.Equal(ErrorCodes.CityExists, cityAgain.Code);

        var (_, me) = await api.Get<CityResponse>("/api/city/me");
        Assert.Equal(city.Data.Id, me.Data?.Id);
    }

    [SkippableFact]
    public async Task Refresh_Rotates_And_ReuseIsRejected()
    {
        SkipIfUnavailable();
        var api = new ApiClient(_factory.CreateJsonClient());
        var tokens = await api.RegisterAsync("u" + Guid.NewGuid().ToString("N")[..10]);
        var (_, refreshed) = await api.Post<TokenResponse>("/api/auth/refresh", new { refreshToken = tokens.RefreshToken });
        Assert.Equal(0, refreshed.Code);
        var (_, reuse) = await api.Post<TokenResponse>("/api/auth/refresh", new { refreshToken = tokens.RefreshToken });
        Assert.Equal(ErrorCodes.Unauthorized, reuse.Code);
        var (_, stillValid) = await api.Post<TokenResponse>(
            "/api/auth/refresh",
            new { refreshToken = refreshed.Data!.RefreshToken });
        Assert.Equal(0, stillValid.Code);
        Assert.False(string.IsNullOrWhiteSpace(stillValid.Data?.AccessToken));
    }

    [SkippableFact]
    public async Task Refresh_ConcurrentReuse_DoesNotRevokeNewSession()
    {
        SkipIfUnavailable();
        var api = new ApiClient(_factory.CreateJsonClient());
        var tokens = await api.RegisterAsync("u" + Guid.NewGuid().ToString("N")[..10]);
        var first = api.Post<TokenResponse>("/api/auth/refresh", new { refreshToken = tokens.RefreshToken });
        var second = api.Post<TokenResponse>("/api/auth/refresh", new { refreshToken = tokens.RefreshToken });
        await Task.WhenAll(first, second);

        var bodies = new[] { first.Result.Body, second.Result.Body };
        Assert.Contains(bodies, body => body.Code == 0);
        var winner = bodies.First(body => body.Code == 0);
        var (_, again) = await api.Post<TokenResponse>(
            "/api/auth/refresh",
            new { refreshToken = winner.Data!.RefreshToken });
        Assert.Equal(0, again.Code);
    }

    private void SkipIfUnavailable()
    {
        Skip.If(!_factory.Available, _factory.UnavailableReason ?? "需要 PostgreSQL 或 Docker");
    }
}
