using FreeSql.DataAnnotations;
using SanguoGame.Core.World;

namespace SanguoGame.Infrastructure.Entities;

[Table(Name = "sg_outpost")]
[Index("uk_outpost_xy", "X,Y", true)]
public sealed class OutpostEntity
{
    [Column(Name = "id", IsIdentity = true, IsPrimary = true)]
    public long Id { get; set; }

    [Column(Name = "type", StringLength = 32, IsNullable = false)]
    public string Type { get; set; } = "";

    [Column(Name = "name", StringLength = 32, IsNullable = false)]
    public string Name { get; set; } = "";

    [Column(Name = "x", IsNullable = false)]
    public int X { get; set; }

    [Column(Name = "y", IsNullable = false)]
    public int Y { get; set; }

    [Column(Name = "garrison", IsNullable = false)]
    public int Garrison { get; set; }

    [Column(Name = "recover_at")]
    public DateTime? RecoverAt { get; set; }

    [Column(Name = "kind", IsNullable = false)]
    public OutpostKind Kind { get; set; }

    [Column(Name = "expires_at")]
    public DateTime? ExpiresAt { get; set; }
}
