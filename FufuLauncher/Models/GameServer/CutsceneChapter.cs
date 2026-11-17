/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Text.RegularExpressions;
using FufuLauncher.Helpers;

namespace FufuLauncher.Models.GameServer;

internal enum CutsceneCategory { Archon, Story, Event, Tribal, World, Other }

internal sealed partial record CutsceneChapter(CutsceneCategory Category, string TitleKey, string QuestId = "",
    IReadOnlyDictionary<string, string>? Titles = null, Version? ReleaseVersion = null, int QuestChapterId = 0)
{
    public static CutsceneChapter FromFileName(string name)
    {
        string file = Path.GetFileNameWithoutExtension(name).ToUpperInvariant();
        if (file.StartsWith("VIDEO_REUNION_", StringComparison.Ordinal))
            return new(CutsceneCategory.Other, "Cutscene_Reunion");

        Match quest = QuestCode().Match(file);
        string kind = quest.Groups[1].Value;
        string number = quest.Groups[2].Value;
        string region = file switch
        {
            _ when file.Contains("MENGDE") || file.Contains("MDAQ") || file.StartsWith("CS_102")
                || file.Contains("WDLQ") || file.Contains("VENTI") => "Mondstadt",
            _ when file.Contains("LIYUE") || file.Contains("LYAQ") || file.StartsWith("CS_LY_")
                || file.Contains("XIAO") || file.Contains("SHENHE") || file.Contains("YUNJIN") => "Liyue",
            _ when file.Contains("INAZUMA") || file.Contains("DAOQI") || file.Contains("SHOUGUN")
                || file.Contains("WANYE") || file.Contains("_SHG_") || file.Contains("_AYAKA_") => "Inazuma",
            _ when file.Contains("SUMERU") || file.StartsWith("CS_XM_") => "Sumeru",
            _ when file.Contains("FONTAINE") || file.StartsWith("CS_FD_") => "Fontaine",
            _ when file.Contains("NATLAN") => "Natlan",
            _ when file.Contains("NODKRAI") || file.StartsWith("CS_NK_") => "NodKrai",
            _ when file.StartsWith("CS_ZD_") => "Snezhnaya",
            _ => string.Empty
        };
        CutsceneCategory category = kind switch
        {
            "AQ" => CutsceneCategory.Archon,
            "LQ" when region == "Natlan" && number.StartsWith("15", StringComparison.Ordinal) => CutsceneCategory.Tribal,
            "LQ" => CutsceneCategory.Story,
            "EQ" => CutsceneCategory.Event,
            "WQ" => CutsceneCategory.World,
            _ when file.StartsWith("CS_102") || file.StartsWith("CS_200") || file.StartsWith("CS_201")
                || file.StartsWith("CS_XM_") => CutsceneCategory.Archon,
            _ => CutsceneCategory.Other
        };
        if (region.Length == 0 && (category is CutsceneCategory.Story or CutsceneCategory.Archon))
        {
            region = number switch
            {
                _ when number.StartsWith("10") => "Mondstadt",
                _ when number.StartsWith("11") => "Liyue",
                _ when number.StartsWith("12") => "Inazuma",
                _ => string.Empty
            };
        }

        // Chapter groups use the region. Where the quest code is consistent, split by quest family.
        string questId = category is CutsceneCategory.Story or CutsceneCategory.Tribal or CutsceneCategory.World
            && number.Length >= 5 ? kind + number[..5] : string.Empty;
        string titleKey = region.Length == 0 ? $"Cutscene_Category_{category}" : $"Cutscene_Region_{region}";
        return new(category, titleKey, questId);
    }

    public string Title => Titles is not null ? CutsceneNames.Localize(Titles)
        : QuestId.Length == 0 ? TitleKey.GetLocalized() : $"{TitleKey.GetLocalized()} · {QuestId}";
    public string Label => $"Cutscene_Category_{Category}".GetLocalized();

    public static CutsceneChapter FromVoiceGroup(string groupId, IReadOnlyList<string> videos,
        IReadOnlyList<CutsceneVoiceQuestRef> quests)
    {
        if (videos.Count > 0)
        {
            CutsceneChapter fromVideo = FromFileName(videos[0]);
            if (fromVideo.QuestId.Length > 0 || fromVideo.TitleKey != "Cutscene_Category_Other")
                return fromVideo with { QuestId = string.IsNullOrEmpty(fromVideo.QuestId) ? $"#{groupId}" : fromVideo.QuestId };
        }

        int chapterId = quests.Select(q => q.ChapterId).FirstOrDefault(id => id > 0);
        CutsceneCategory category = groupId switch
        {
            _ when groupId.StartsWith("1", StringComparison.Ordinal) => CutsceneCategory.Archon,
            _ when groupId.StartsWith("2", StringComparison.Ordinal) => CutsceneCategory.Story,
            _ when groupId.StartsWith("4", StringComparison.Ordinal) => CutsceneCategory.Event,
            _ when groupId.StartsWith("5", StringComparison.Ordinal) => CutsceneCategory.World,
            _ => CutsceneCategory.Other
        };
        string suffix = chapterId > 0 ? $"#{chapterId}" : $"#{groupId}";
        return new CutsceneChapter(category, $"Cutscene_Category_{category}", suffix, QuestChapterId: chapterId);
    }

    [GeneratedRegex("(AQ|LQ|EQ|WQ)_?(\\d+)", RegexOptions.CultureInvariant)]
    private static partial Regex QuestCode();
}
