using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using SanguoGame.Server.Contracts;
using Xunit;

namespace SanguoGame.Tests.Api;

public sealed class ApiClient
{
    private readonly HttpClient _http;
    private static readonly JsonSerializerOptions Json = GameApiFactory.Json;

    public ApiClient(HttpClient http)
    {
        _http = http;
    }

    public void UseToken(string accessToken) =>
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

    public async Task<(System.Net.HttpStatusCode Status, ApiResult<T> Body)> Send<T>(
        HttpMethod method,
        string url,
        object? body = null)
    {
        using var request = new HttpRequestMessage(method, url);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: Json);
        }

        using var response = await _http.SendAsync(request);
        var parsed = await response.Content.ReadFromJsonAsync<ApiResult<T>>(Json)
            ?? new ApiResult<T> { Code = -1, Message = "empty" };
        return (response.StatusCode, parsed);
    }

    public Task<(System.Net.HttpStatusCode Status, ApiResult<T> Body)> Get<T>(string url) =>
        Send<T>(HttpMethod.Get, url);

    public Task<(System.Net.HttpStatusCode Status, ApiResult<T> Body)> Post<T>(string url, object? body = null) =>
        Send<T>(HttpMethod.Post, url, body);

    public async Task<TokenResponse> RegisterAsync(string username, string password = "Passw0rd!")
    {
        var (_, body) = await Post<TokenResponse>("/api/auth/register", new { username, password });
        Assert.True(body.Code == 0, $"register failed code={body.Code} message={body.Message}");
        Assert.False(string.IsNullOrWhiteSpace(body.Data?.AccessToken));
        UseToken(body.Data!.AccessToken);
        return body.Data;
    }

    public async Task<(long CityId, int X, int Y)> RegisterCityAsync(string? prefix = null)
    {
        var player = await RegisterPlayerAsync(prefix);
        return (player.CityId, player.X, player.Y);
    }

    public async Task<TestPlayer> RegisterPlayerAsync(string? prefix = null)
    {
        var tag = (prefix ?? "p") + Guid.NewGuid().ToString("N")[..8];
        var username = "u" + tag;
        await RegisterAsync(username);
        var characterName = "角" + tag[..8];
        var (_, character) = await Post<CharacterResponse>("/api/characters", new { name = characterName });
        Assert.Equal(0, character.Code);
        Assert.NotNull(character.Data);
        var (_, city) = await Post<CityResponse>("/api/city/found");
        Assert.Equal(0, city.Code);
        Assert.NotNull(city.Data);
        return new TestPlayer(
            username,
            character.Data.Id,
            character.Data.Name,
            city.Data.Id,
            city.Data.X,
            city.Data.Y);
    }
}

public sealed record TestPlayer(
    string Username,
    long CharacterId,
    string CharacterName,
    long CityId,
    int X,
    int Y);
