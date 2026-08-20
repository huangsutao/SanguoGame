using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SanguoGame.Server.Contracts;
using SanguoGame.Server.Security;
using SanguoGame.Server.Services;

namespace SanguoGame.Server.Controllers;

[Authorize]
[ApiController]
[Route("api/daily")]
public sealed class DailyController : ControllerBase
{
    private readonly DailyService _daily;

    public DailyController(DailyService daily)
    {
        _daily = daily;
    }

    [HttpGet]
    public async Task<ApiResult<DailyOverviewDto>> Get(CancellationToken cancellationToken) =>
        ApiResult.Ok(await _daily.GetOverviewAsync(User.GetAccountId(), cancellationToken));

    [HttpPost("claim")]
    public async Task<ApiResult<DailyOverviewDto>> Claim(
        [FromBody] ClaimDailyRequest request,
        CancellationToken cancellationToken) =>
        ApiResult.Ok(await _daily.ClaimAsync(User.GetAccountId(), request.MissionType, cancellationToken));
}
