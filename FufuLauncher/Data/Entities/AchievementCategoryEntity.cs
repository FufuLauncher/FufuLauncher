using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FufuLauncher.Data.Entities;

[Table("Categories")]
public class AchievementCategoryEntity
{
    [Key]
    [Column("Name")]
    public string Name { get; set; } = string.Empty;

    [Column("IconUrl")]
    public string? IconUrl { get; set; }
}
