using FreeSql;
using SanguoGame.Core;
using SanguoGame.Infrastructure;
using SanguoGame.Infrastructure.Entities;
using SanguoGame.Server.Contracts;

namespace SanguoGame.Server.Services;

public sealed class CharacterService
{
    private readonly IFreeSql _orm;

    public CharacterService(IFreeSql orm)
    {
        _orm = orm;
    }

    public async Task<CharacterResponse> CreateAsync(long accountId, CreateCharacterRequest request, CancellationToken cancellationToken)
    {
        var name = request.Name.Trim();
        if (name.Length is < 2 or > 12)
        {
            throw new BizException(ErrorCodes.ValidationFailed, "角色名长度为 2～12 位");
        }

        if (await _orm.Select<CharacterEntity>().AnyAsync(c => c.AccountId == accountId, cancellationToken))
        {
            throw new BizException(ErrorCodes.CharacterExists, "该账号已有角色");
        }

        var entity = new CharacterEntity
        {
            AccountId = accountId,
            Name = name,
            CreatedAt = DateTime.UtcNow
        };

        try
        {
            entity.Id = await _orm.Insert(entity).ExecuteIdentityAsync(cancellationToken);
        }
        catch (Exception ex) when (DbErrors.IsUniqueViolation(ex))
        {
            if (await _orm.Select<CharacterEntity>().AnyAsync(c => c.AccountId == accountId, cancellationToken))
            {
                throw new BizException(ErrorCodes.CharacterExists, "该账号已有角色");
            }

            throw new BizException(ErrorCodes.CharacterNameTaken, "角色名已被占用");
        }

        return ToResponse(entity);
    }

    public async Task<CharacterResponse> GetMineAsync(long accountId, CancellationToken cancellationToken)
    {
        var entity = await _orm.Select<CharacterEntity>()
            .Where(c => c.AccountId == accountId)
            .FirstAsync(cancellationToken);
        if (entity is null)
        {
            throw new BizException(ErrorCodes.NotFound, "尚未创建角色");
        }

        return ToResponse(entity);
    }

    private static CharacterResponse ToResponse(CharacterEntity entity) =>
        new(entity.Id, entity.Name, entity.CreatedAt);
}
