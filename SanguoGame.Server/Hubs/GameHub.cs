using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using FreeSql;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using SanguoGame.Infrastructure.Entities;
using SanguoGame.Server.Security;

namespace SanguoGame.Server.Hubs;

[Authorize]
public sealed class GameHub : Hub
{
    private readonly IFreeSql _orm;

    public GameHub(IFreeSql orm)
    {
        _orm = orm;
    }

    public override async Task OnConnectedAsync()
    {
        var value = Context.User?.FindFirstValue(JwtRegisteredClaimNames.Sub)
            ?? Context.User?.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? Context.User?.FindFirstValue("sub");
        if (long.TryParse(value, out var accountId))
        {
            var character = await _orm.Select<CharacterEntity>()
                .Where(c => c.AccountId == accountId)
                .FirstAsync();
            if (character is not null)
            {
                var city = await _orm.Select<CityEntity>()
                    .Where(c => c.CharacterId == character.Id)
                    .FirstAsync();
                if (city is not null)
                {
                    await Groups.AddToGroupAsync(Context.ConnectionId, $"city:{city.Id}");
                }
            }
        }

        await base.OnConnectedAsync();
    }
}
