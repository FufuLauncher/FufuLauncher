/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

namespace FufuLauncher.Services;

internal sealed class PluginConfigSection
{
    public PluginConfigSection(string name) => Name = name;

    public string Name
    {
        get;
    }

    public List<string> Lines { get; } = [];

    public List<string> KeyOrder { get; } = [];

    public Dictionary<string, string> KeyLines { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, string> Values { get; } = new(StringComparer.OrdinalIgnoreCase);
}

internal sealed class PluginConfigDocument
{
    public List<string> PrefixLines { get; } = [];

    public List<PluginConfigSection> Sections { get; } = [];

    public static PluginConfigDocument Parse(IReadOnlyList<string> lines)
    {
        var document = new PluginConfigDocument();
        PluginConfigSection? current = null;

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (TryParseSectionName(trimmed, out var sectionName))
            {
                current = new PluginConfigSection(sectionName);
                current.Lines.Add(line);
                document.Sections.Add(current);
                continue;
            }

            if (current == null)
            {
                document.PrefixLines.Add(line);
                continue;
            }

            current.Lines.Add(line);
            if (!TryParseKey(trimmed, out var key, out var value))
            {
                continue;
            }

            current.Values[key] = value;
            if (current.KeyLines.TryAdd(key, line))
            {
                current.KeyOrder.Add(key);
            }
        }

        return document;
    }

    public PluginConfigSection? Find(string sectionName) =>
        Sections.FirstOrDefault(section => string.Equals(section.Name, sectionName, StringComparison.OrdinalIgnoreCase));

    public string? GetValue(string sectionName, string key)
    {
        var section = Find(sectionName);
        return section != null && section.Values.TryGetValue(key, out var value) ? value : null;
    }

    public static bool TryParseSectionName(string trimmedLine, out string name)
    {
        name = string.Empty;
        if (trimmedLine.Length < 3 || !trimmedLine.StartsWith('[') || !trimmedLine.EndsWith(']'))
        {
            return false;
        }

        name = trimmedLine[1..^1].Trim();
        return name.Length > 0;
    }

    public static bool TryParseKey(string trimmedLine, out string key, out string value)
    {
        key = string.Empty;
        value = string.Empty;

        if (trimmedLine.Length == 0 || trimmedLine[0] is ';' or '#' or '[')
        {
            return false;
        }

        var separator = trimmedLine.IndexOf('=');
        if (separator <= 0)
        {
            return false;
        }

        key = trimmedLine[..separator].Trim();
        if (key.Length == 0)
        {
            return false;
        }

        value = trimmedLine[(separator + 1)..].Trim();
        return true;
    }
}

internal sealed class PluginConfigRepairSummary
{
    public int AddedSections
    {
        get;
        set;
    }

    public int RemovedSections
    {
        get;
        set;
    }

    public int AddedKeys
    {
        get;
        set;
    }

    public int RemovedKeys
    {
        get;
        set;
    }

    public bool HasChanges => AddedSections + RemovedSections + AddedKeys + RemovedKeys > 0;

    public int AddedTotal => AddedSections + AddedKeys;

    public int RemovedTotal => RemovedSections + RemovedKeys;
}

internal static class PluginConfigRepair
{
    public static List<string> Repair(PluginConfigBaseline baseline, IReadOnlyList<string> liveLines,
        out PluginConfigRepairSummary summary)
    {
        summary = new PluginConfigRepairSummary();

        var baselineDocument = PluginConfigDocument.Parse(PluginConfigBaseline.SplitLines(baseline.Content));
        var liveDocument = PluginConfigDocument.Parse(liveLines);

        var baselineSections = new Dictionary<string, PluginConfigSection>(StringComparer.OrdinalIgnoreCase);
        foreach (var section in baselineDocument.Sections)
        {
            baselineSections.TryAdd(section.Name, section);
        }

        var output = new List<string>(liveLines.Count + 16);
        output.AddRange(liveDocument.PrefixLines);

        foreach (var section in liveDocument.Sections)
        {
            if (!baselineSections.TryGetValue(section.Name, out var baselineSection))
            {
                summary.RemovedSections++;
                continue;
            }

            var kept = new List<string>(section.Lines.Count);
            var present = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            kept.Add(section.Lines[0]);

            for (var index = 1; index < section.Lines.Count; index++)
            {
                var line = section.Lines[index];
                if (PluginConfigDocument.TryParseKey(line.Trim(), out var key, out _))
                {
                    if (!baselineSection.Values.ContainsKey(key) || !present.Add(key))
                    {
                        summary.RemovedKeys++;
                        continue;
                    }
                }

                kept.Add(line);
            }

            var insertAt = kept.Count;
            while (insertAt > 1 && string.IsNullOrWhiteSpace(kept[insertAt - 1]))
            {
                insertAt--;
            }

            var additions = new List<string>();
            foreach (var key in baselineSection.KeyOrder)
            {
                if (present.Contains(key))
                {
                    continue;
                }

                additions.Add(baselineSection.KeyLines[key]);
                summary.AddedKeys++;
            }

            if (additions.Count > 0)
            {
                kept.InsertRange(insertAt, additions);
            }

            output.AddRange(kept);
        }

        var liveSectionNames = new HashSet<string>(liveDocument.Sections.Select(section => section.Name),
            StringComparer.OrdinalIgnoreCase);

        foreach (var section in baselineDocument.Sections)
        {
            if (liveSectionNames.Contains(section.Name))
            {
                continue;
            }

            if (output.Count > 0 && !string.IsNullOrWhiteSpace(output[^1]))
            {
                output.Add(string.Empty);
            }

            output.AddRange(section.Lines);
            summary.AddedSections++;
        }

        return output;
    }
}
