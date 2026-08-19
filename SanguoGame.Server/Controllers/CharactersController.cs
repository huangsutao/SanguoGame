using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SanguoGame.Server.Contracts;
using SanguoGame.Server.Security;
using SanguoGame.Server.Services;

namespace SanguoGame.Server.Controllers;

[Authorize]
[ApiController]
[Route("api/characters")]
public sealed class CharactersController : ControllerBase
{
    private readonly CharacterService _characters;

    public CharactersController(CharacterService characters)
    {
        _characters = characters;
    }

    [HttpPost]
    public async Task<ApiResult<CharacterResponse>> Create(
        [FromBody] CreateCharacterRequest request,
        CancellationToken cancellationToken) =>
        ApiResult.Ok(await _characters.CreateAsync(User.GetAccountId(), request, cancellationToken));

    [HttpGet("me")]
    public async Task<ApiResult<CharacterResponse>> Me(CancellationToken cancellationToken) =>
        ApiResult.Ok(await _characters.GetMineAsync(User.GetAccountId(), cancellationToken));
}
