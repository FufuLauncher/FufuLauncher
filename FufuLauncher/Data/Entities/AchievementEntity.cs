using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FufuLauncher.Data.Entities;

[Table("Achievements")]
public class AchievementEntity
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("Uid")]
    public int Uid { get; set; }

    [Column("Id")]
    public int Id { get; set; }

    [Column("Title")]
    public string? Title { get; set; }

    [Column("CategoryName")]
    public string? CategoryName { get; set; }

    [Column("RawJson")]
    public string? RawJson { get; set; }

    [Column("IsCompleted")]
    public int IsCompleted { get; set; }

    [Column("CurrentProgress")]
    public int CurrentProgress { get; set; }

    [Column("MaxProgress")]
    public int MaxProgress { get; set; }

    [Column("CompletionTimestamp")]
    public long CompletionTimestamp { get; set; }
}
