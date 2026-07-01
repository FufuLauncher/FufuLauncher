/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/
using System.Text.Json.Serialization;
using Microsoft.UI.Xaml.Media.Imaging;

namespace FufuLauncher.Models
{
    public class PlayerRecordResponse
    {
        [JsonPropertyName("code")]
        public int Code
        {
            get; set;
        }

        [JsonPropertyName("result")]
        public PlayerRecordResult Result
        {
            get; set;
        }
    }

    public class PlayerRecordResult
    {
        [JsonPropertyName("role_data")]
        public List<RoleData> RoleData
        {
            get; set;
        }
    }
    
    public class RoleData
    {
        [JsonPropertyName("uid")]
        public string Uid
        {
            get; set;
        }

        [JsonPropertyName("role")]
        public string Name
        {
            get; set;
        }

        [JsonPropertyName("level")]
        public int Level
        {
            get; set;
        }
        [JsonIgnore]
        public string LevelDisplay => $"Lv.{Level}";
        [JsonPropertyName("element")]
        public int ElementType
        {
            get; set;
        }

        [JsonPropertyName("fetter")]
        public int Fetter
        {
            get; set;
        }
        
        [JsonPropertyName("role_img")]
        public string IconUrl
        {
            get; set;
        }

        [JsonIgnore]
        public BitmapImage? SafeIconImage
        {
            get
            {
                if (!Uri.TryCreate(IconUrl, UriKind.Absolute, out var uri))
                {
                    return null;
                }

                return new BitmapImage(uri);
            }
        }

        [JsonPropertyName("role_side_img")]
        public string PortraitUrl
        {
            get; set;
        }
        
        [JsonPropertyName("weapon")]
        public string WeaponName
        {
            get; set;
        }
        [JsonPropertyName("weapon_level")]
        public int WeaponLevel
        {
            get; set;
        }
        [JsonPropertyName("weapon_class")]
        public string WeaponRefinement
        {
            get; set;
        }
        
        [JsonPropertyName("hp")]
        public double Hp
        {
            get; set;
        }
        [JsonPropertyName("attack")]
        public double Attack
        {
            get; set;
        }
        [JsonPropertyName("defend")]
        public double Defend
        {
            get; set;
        }

        [JsonPropertyName("crit")]
        public string CritRate
        {
            get; set;
        }
        [JsonPropertyName("crit_dmg")]
        public string CritDmg
        {
            get; set;
        }
        [JsonPropertyName("recharge")]
        public string Recharge
        {
            get; set;
        }
        [JsonPropertyName("element_mastery")]
        public int ElementMastery
        {
            get; set;
        }
        
        [JsonPropertyName("fire_dmg")]
        public string FireDmg
        {
            get; set;
        }
        [JsonPropertyName("water_dmg")]
        public string WaterDmg
        {
            get; set;
        }
        [JsonPropertyName("wind_dmg")]
        public string WindDmg
        {
            get; set;
        }
        [JsonPropertyName("thunder_dmg")]
        public string ElectroDmg
        {
            get; set;
        }
        [JsonPropertyName("ice_dmg")]
        public string CryoDmg
        {
            get; set;
        }
        [JsonPropertyName("rock_dmg")]
        public string GeoDmg
        {
            get; set;
        }
        [JsonPropertyName("grass_dmg")]
        public string DendroDmg
        {
            get; set;
        }
        [JsonPropertyName("physical_dmg")]
        public string PhysicalDmg
        {
            get; set;
        }
        
        [JsonPropertyName("artifacts")]
        public string ArtifactsSetSummary
        {
            get; set;
        }
        
        [JsonPropertyName("artifacts_detail")]
        public List<ArtifactDetail> Artifacts
        {
            get; set;
        }
        
        [JsonPropertyName("ability1")]
        public int SkillA
        {
            get; set;
        }
        [JsonPropertyName("ability2")]
        public int SkillE
        {
            get; set;
        }
        [JsonPropertyName("ability3")]
        public int SkillQ
        {
            get; set;
        }
    }

    public class ArtifactDetail
    {
        [JsonPropertyName("artifacts_name")]
        public string Name
        {
            get; set;
        }

        [JsonPropertyName("artifacts_type")]
        public string Type
        {
            get; set;
        }

        [JsonPropertyName("level")]
        public int Level
        {
            get; set;
        }

        [JsonPropertyName("maintips")]
        public string MainStatName
        {
            get; set;
        }

        [JsonPropertyName("mainvalue")]
        public object MainStatValueRaw
        {
            get; set;
        }
        
        public string MainStatDisplay => $"{MainStatName}: {MainStatValueRaw}";
        
        [JsonPropertyName("tips1")]
        public string SubStat1
        {
            get; set;
        }
        [JsonPropertyName("tips2")]
        public string SubStat2
        {
            get; set;
        }
        [JsonPropertyName("tips3")]
        public string SubStat3
        {
            get; set;
        }
        [JsonPropertyName("tips4")]
        public string SubStat4
        {
            get; set;
        }
    }
    
    public class UserConfig
    {
        public string GameUid
        {
            get; set;
        }
        public string Nickname
        {
            get; set;
        }
    }
}
