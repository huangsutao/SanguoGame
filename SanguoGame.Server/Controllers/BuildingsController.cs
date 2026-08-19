using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SanguoGame.Server.Contracts;
using SanguoGame.Server.Security;
using SanguoGame.Server.Services;

namespace SanguoGame.Server.Controllers;

[Authorize]
[ApiController]
[Route("api/buildings")]
public sealed class BuildingsController : ControllerBase
{
    private readonly BuildingService _buildings;

    public BuildingsController(BuildingService buildings)
    {
        _buildings = buildings;
    }

    [HttpGet]
    public async Task<ApiResult<BuildingsOverviewDto>> List(CancellationToken cancellationToken) =>
        ApiResult.Ok(await _buildings.GetOverviewAsync(User.GetAccountId(), cancellationToken));

    [HttpPost("upgrade")]
    public async Task<ApiResult<BuildingsOverviewDto>> Upgrade(
        [FromBody] UpgradeBuildingRequest request,
        CancellationToken cancellationToken) =>
        ApiResult.Ok(await _buildings.UpgradeAsync(User.GetAccountId(), request.BuildingType, cancellationToken));
}
