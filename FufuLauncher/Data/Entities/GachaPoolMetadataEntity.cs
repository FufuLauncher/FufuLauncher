using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FufuLauncher.Data.Entities;

[Table("GachaPoolMetadata")]
public class GachaPoolMetadataEntity
{
    [Column("Version")]
    public string Version { get; set; } = string.Empty;

    [Column("PoolType")]
    public string PoolType { get; set; } = string.Empty;

    [Column("StartTime")]
    public string StartTime { get; set; } = string.Empty;

    [Column("EndTime")]
    public string EndTime { get; set; } = string.Empty;

    [Column("UpItems")]
    public string UpItems { get; set; } = string.Empty;

    [Column("UpItemNames")]
    public string UpItemNames { get; set; } = "[]";
}
