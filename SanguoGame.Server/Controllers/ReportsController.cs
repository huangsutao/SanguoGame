using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SanguoGame.Server.Contracts;
using SanguoGame.Server.Security;
using SanguoGame.Server.Services;

namespace SanguoGame.Server.Controllers;

[Authorize]
[ApiController]
[Route("api/reports")]
public sealed class ReportsController : ControllerBase
{
    private readonly MarchService _marches;

    public ReportsController(MarchService marches)
    {
        _marches = marches;
    }

    [HttpGet]
    public async Task<ApiResult<PagedResult<BattleReportDto>>> List(
        [FromQuery] PagedQuery query,
        CancellationToken cancellationToken) =>
        ApiResult.Ok(await _marches.ListReportsAsync(User.GetAccountId(), query, cancellationToken));
}
