using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FufuLauncher.Data.Entities;

[Table("GachaLogs")]
public class GachaLogEntity
{
    [Column("Id")]
    public string Id { get; set; } = string.Empty;

    [Column("Uid")]
    public string Uid { get; set; } = string.Empty;

    [Column("GachaType")]
    public string GachaType { get; set; } = string.Empty;

    [Column("ItemId")]
    public string? ItemId { get; set; }

    [Column("Count")]
    public string? Count { get; set; }

    [Column("Time")]
    public string? Time { get; set; }

    [Column("Name")]
    public string? Name { get; set; }

    [Column("Lang")]
    public string? Lang { get; set; }

    [Column("ItemType")]
    public string? ItemType { get; set; }

    [Column("RankType")]
    public string? RankType { get; set; }
}
