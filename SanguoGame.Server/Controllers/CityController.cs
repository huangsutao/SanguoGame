using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SanguoGame.Server.Contracts;
using SanguoGame.Server.Security;
using SanguoGame.Server.Services;

namespace SanguoGame.Server.Controllers;

[Authorize]
[ApiController]
[Route("api/city")]
public sealed class CityController : ControllerBase
{
    private readonly CityService _cities;

    public CityController(CityService cities)
    {
        _cities = cities;
    }

    [HttpPost("found")]
    public async Task<ApiResult<CityResponse>> Found(CancellationToken cancellationToken) =>
        ApiResult.Ok(await _cities.FoundAsync(User.GetAccountId(), cancellationToken));

    [HttpGet("me")]
    public async Task<ApiResult<CityResponse>> Me(CancellationToken cancellationToken) =>
        ApiResult.Ok(await _cities.GetMineAsync(User.GetAccountId(), cancellationToken));
}
