using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SanguoGame.Server.Contracts;
using SanguoGame.Server.Security;
using SanguoGame.Server.Services;

namespace SanguoGame.Server.Controllers;

[Authorize]
[ApiController]
[Route("api/army")]
public sealed class ArmyController : ControllerBase
{
    private readonly ArmyService _army;
    private readonly MarchService _marches;

    public ArmyController(ArmyService army, MarchService marches)
    {
        _army = army;
        _marches = marches;
    }

    [HttpGet]
    public async Task<ApiResult<ArmyOverviewDto>> Get(CancellationToken cancellationToken) =>
        ApiResult.Ok(await _army.GetOverviewAsync(User.GetAccountId(), cancellationToken));

    [HttpPost("recruit")]
    public async Task<ApiResult<ArmyOverviewDto>> Recruit(
        [FromBody] RecruitRequest request,
        CancellationToken cancellationToken) =>
        ApiResult.Ok(await _army.RecruitAsync(User.GetAccountId(), request.TroopType, request.Count, cancellationToken));

    [HttpPost("march")]
    public async Task<ApiResult<ArmyOverviewDto>> March(
        [FromBody] MarchRequest request,
        CancellationToken cancellationToken) =>
        ApiResult.Ok(await _marches.StartAsync(User.GetAccountId(), request, cancellationToken));
}
