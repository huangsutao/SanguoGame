using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SanguoGame.Server.Contracts;
using SanguoGame.Server.Security;
using SanguoGame.Server.Services;

namespace SanguoGame.Server.Controllers;

[Authorize]
[ApiController]
[Route("api/alliances")]
public sealed class AlliancesController : ControllerBase
{
    private readonly AllianceService _alliances;

    public AlliancesController(AllianceService alliances)
    {
        _alliances = alliances;
    }

    [HttpPost]
    public async Task<ApiResult<AllianceDetailDto>> Create(
        [FromBody] CreateAllianceRequest request,
        CancellationToken cancellationToken) =>
        ApiResult.Ok(await _alliances.CreateAsync(User.GetAccountId(), request, cancellationToken));

    [HttpGet]
    public async Task<ApiResult<PagedResult<AllianceSummaryDto>>> List(
        [FromQuery] PagedQuery query,
        CancellationToken cancellationToken) =>
        ApiResult.Ok(await _alliances.ListAsync(query, cancellationToken));

    [HttpGet("me")]
    public async Task<ApiResult<AllianceDetailDto?>> Me(CancellationToken cancellationToken) =>
        ApiResult.Ok(await _alliances.GetMineAsync(User.GetAccountId(), cancellationToken));

    [HttpGet("pending")]
    public async Task<ApiResult<AlliancePendingDto>> Pending(CancellationToken cancellationToken) =>
        ApiResult.Ok(await _alliances.GetPendingAsync(User.GetAccountId(), cancellationToken));

    [HttpGet("{id:long}")]
    public async Task<ApiResult<AllianceDetailDto>> Get(long id, CancellationToken cancellationToken) =>
        ApiResult.Ok(await _alliances.GetAsync(User.GetAccountId(), id, cancellationToken));

    [HttpPost("{id:long}/apply")]
    public async Task<ApiResult<object?>> Apply(long id, CancellationToken cancellationToken)
    {
        await _alliances.ApplyAsync(User.GetAccountId(), id, cancellationToken);
        return ApiResult.Ok();
    }

    [HttpPost("invite")]
    public async Task<ApiResult<object?>> Invite(
        [FromBody] InviteAllianceRequest request,
        CancellationToken cancellationToken)
    {
        await _alliances.InviteAsync(User.GetAccountId(), request, cancellationToken);
        return ApiResult.Ok();
    }

    [HttpPost("invites/{id:long}/accept")]
    public async Task<ApiResult<object?>> AcceptInvite(long id, CancellationToken cancellationToken)
    {
        await _alliances.AcceptInviteAsync(User.GetAccountId(), id, cancellationToken);
        return ApiResult.Ok();
    }

    [HttpPost("invites/{id:long}/decline")]
    public async Task<ApiResult<object?>> DeclineInvite(long id, CancellationToken cancellationToken)
    {
        await _alliances.DeclineInviteAsync(User.GetAccountId(), id, cancellationToken);
        return ApiResult.Ok();
    }

    [HttpPost("applications/{id:long}/accept")]
    public async Task<ApiResult<object?>> AcceptApplication(long id, CancellationToken cancellationToken)
    {
        await _alliances.AcceptApplicationAsync(User.GetAccountId(), id, cancellationToken);
        return ApiResult.Ok();
    }

    [HttpPost("applications/{id:long}/reject")]
    public async Task<ApiResult<object?>> RejectApplication(long id, CancellationToken cancellationToken)
    {
        await _alliances.RejectApplicationAsync(User.GetAccountId(), id, cancellationToken);
        return ApiResult.Ok();
    }

    [HttpPost("leave")]
    public async Task<ApiResult<object?>> Leave(CancellationToken cancellationToken)
    {
        await _alliances.LeaveAsync(User.GetAccountId(), cancellationToken);
        return ApiResult.Ok();
    }

    [HttpPost("kick")]
    public async Task<ApiResult<object?>> Kick(
        [FromBody] KickAllianceRequest request,
        CancellationToken cancellationToken)
    {
        await _alliances.KickAsync(User.GetAccountId(), request, cancellationToken);
        return ApiResult.Ok();
    }

    [HttpPost("notice")]
    public async Task<ApiResult<object?>> Notice(
        [FromBody] UpdateAllianceNoticeRequest request,
        CancellationToken cancellationToken)
    {
        await _alliances.UpdateNoticeAsync(User.GetAccountId(), request, cancellationToken);
        return ApiResult.Ok();
    }

    [HttpPost("dissolve")]
    public async Task<ApiResult<object?>> Dissolve(CancellationToken cancellationToken)
    {
        await _alliances.DissolveAsync(User.GetAccountId(), cancellationToken);
        return ApiResult.Ok();
    }
}
