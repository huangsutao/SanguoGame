using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SanguoGame.Server.Contracts;
using SanguoGame.Server.Security;
using SanguoGame.Server.Services;

namespace SanguoGame.Server.Controllers;

[Authorize]
[ApiController]
[Route("api/rankings")]
public sealed class RankingsController : ControllerBase
{
    private readonly RankingService _rankings;

    public RankingsController(RankingService rankings)
    {
        _rankings = rankings;
    }

    [HttpGet]
    public async Task<ApiResult<RankingDto>> Get(
        [FromQuery] string type = "power",
        CancellationToken cancellationToken = default) =>
        ApiResult.Ok(await _rankings.GetAsync(User.GetAccountId(), type, cancellationToken));
}
