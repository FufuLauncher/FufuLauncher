using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FufuLauncher.Data.Entities;

[Table("Settings")]
public class SettingEntity
{
    [Key]
    [Column("Key")]
    public string Key { get; set; } = string.Empty;

    [Column("Value")]
    public string? Value { get; set; }
}
