using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FufuLauncher.Data.Entities;

[Table("Metadata")]
public class MetadataEntity
{
    [Key]
    [Column("Name")]
    public string Name { get; set; } = string.Empty;

    [Column("ImgSrc")]
    public string? ImgSrc { get; set; }

    [Column("ElementSrc")]
    public string? ElementSrc { get; set; }

    [Column("Type")]
    public string? Type { get; set; }

    [Column("Rank")]
    public string? Rank { get; set; }

    [Column("ItemId")]
    public string? ItemId { get; set; }
}
