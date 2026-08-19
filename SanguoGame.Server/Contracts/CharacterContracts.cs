using System.ComponentModel.DataAnnotations;

namespace SanguoGame.Server.Contracts;

public sealed class CreateCharacterRequest
{
    [Required]
    [MinLength(2)]
    [MaxLength(12)]
    public string Name { get; set; } = "";
}

public sealed record CharacterResponse(long Id, string Name, DateTime CreatedAt);
