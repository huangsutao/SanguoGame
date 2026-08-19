using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SanguoGame.Server.Contracts;
using SanguoGame.Server.Security;
using SanguoGame.Server.Services;

namespace SanguoGame.Server.Controllers;

[Authorize]
[ApiController]
[Route("api/fields")]
public sealed class FieldsController : ControllerBase
{
    private readonly FieldService _fields;

    public FieldsController(FieldService fields)
    {
        _fields = fields;
    }

    [HttpGet]
    public async Task<ApiResult<FieldsOverviewDto>> List(CancellationToken cancellationToken) =>
        ApiResult.Ok(await _fields.GetOverviewAsync(User.GetAccountId(), cancellationToken));

    [HttpPost("upgrade")]
    public async Task<ApiResult<FieldsOverviewDto>> Upgrade(
        [FromBody] UpgradeFieldRequest request,
        CancellationToken cancellationToken) =>
        ApiResult.Ok(await _fields.UpgradeAsync(User.GetAccountId(), request.FieldType, cancellationToken));

    [HttpPost("collect")]
    public async Task<ApiResult<FieldsCollectDto>> Collect(
        [FromBody] CollectFieldsRequest? request,
        CancellationToken cancellationToken)
    {
        var result = await _fields.CollectAsync(User.GetAccountId(), request?.FieldType, cancellationToken);
        return ApiResult.Ok(result.Data, result.Message);
    }
}
