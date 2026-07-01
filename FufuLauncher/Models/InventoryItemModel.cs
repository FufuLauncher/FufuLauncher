/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/
using System.Text.Json.Serialization;

namespace FufuLauncher.Models
{
    public class InventoryItemModel
    {
        public int Id
        {
            get; set;
        }
        public string Name { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public int OwnedCount
        {
            get; set;
        }
        public string? IconUrl { get; set; } = string.Empty;
        
        public int TargetCount
        {
            get; set;
        }

        [JsonIgnore]
        public string DisplayCount => OwnedCount >= 10000 ? $"{OwnedCount / 10000.0:F1}w" : OwnedCount.ToString("N0");
        
        [JsonIgnore]
        public string ProgressText => TargetCount > 0 ? $"{OwnedCount} / {TargetCount}" : OwnedCount.ToString();
        
        [JsonIgnore]
        public bool IsTargetMet => TargetCount > 0 && OwnedCount >= TargetCount;
    }

    public class InventoryData
    {
        public List<InventoryItemModel> Items { get; set; } = new();
        public long LastUpdateTime
        {
            get; set;
        }
    }
}
