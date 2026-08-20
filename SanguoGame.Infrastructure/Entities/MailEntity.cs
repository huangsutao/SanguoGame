using FreeSql.DataAnnotations;
using SanguoGame.Core.Social;

namespace SanguoGame.Infrastructure.Entities;

[Table(Name = "sg_mail")]
[Index("ix_mail_recipient_id", "RecipientCharacterId,Id")]
public sealed class MailEntity
{
    [Column(Name = "id", IsIdentity = true, IsPrimary = true)]
    public long Id { get; set; }

    [Column(Name = "recipient_character_id", IsNullable = false)]
    public long RecipientCharacterId { get; set; }

    [Column(Name = "type", IsNullable = false)]
    public MailType Type { get; set; }

    [Column(Name = "title", StringLength = 64, IsNullable = false)]
    public string Title { get; set; } = "";

    [Column(Name = "body", StringLength = 2000, IsNullable = false)]
    public string Body { get; set; } = "";

    [Column(Name = "related_type", StringLength = 32)]
    public string? RelatedType { get; set; }

    [Column(Name = "related_id")]
    public long? RelatedId { get; set; }

    [Column(Name = "is_read", IsNullable = false)]
    public bool IsRead { get; set; }

    [Column(Name = "created_at", IsNullable = false)]
    public DateTime CreatedAt { get; set; }
}
