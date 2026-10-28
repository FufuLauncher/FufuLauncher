/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Globalization;
using System.Text;

namespace FufuLauncher.Services;

public enum PluginGameVersionSupport
{
    Unspecified,
    Supported,
    Unsupported,
    Malformed,
    Unknown
}

internal readonly struct PluginGameVersion
{
    private const int MaxPrecision = 4;

    private readonly int _first;
    private readonly int _second;
    private readonly int _third;
    private readonly int _fourth;

    private PluginGameVersion(int first, int second, int third, int fourth, int precision)
    {
        _first = first;
        _second = second;
        _third = third;
        _fourth = fourth;
        Precision = precision;
    }

    public int Precision
    {
        get;
    }

    public bool IsValid => Precision > 0;

    public static bool TryParse(string? text, out PluginGameVersion version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var value = text.Trim();
        if (value.Length > 1 && (value[0] is 'v' or 'V') && char.IsAsciiDigit(value[1]))
        {
            value = value[1..].TrimStart();
        }

        var segments = value.Split('.');
        if (segments.Length == 0 || segments.Length > MaxPrecision) return false;

        Span<int> parts = stackalloc int[MaxPrecision];
        var precision = 0;

        for (var index = 0; index < segments.Length; index++)
        {
            var segment = segments[index].Trim();
            if (segment.Length == 0) return false;

            if (IsWildcard(segment))
            {
                if (index != segments.Length - 1) return false;
                break;
            }

            var digits = CountLeadingDigits(segment);
            if (digits == 0) return false;
            if (digits != segment.Length && index != segments.Length - 1) return false;

            if (!int.TryParse(segment[..digits], NumberStyles.None, CultureInfo.InvariantCulture, out var part))
            {
                return false;
            }

            if (part > 9999) return false;
            parts[precision++] = part;
        }

        if (precision == 0) return false;

        version = new PluginGameVersion(parts[0],
            precision > 1 ? parts[1] : 0,
            precision > 2 ? parts[2] : 0,
            precision > 3 ? parts[3] : 0,
            precision);
        return true;
    }

    public static bool TryParseGameVersion(string? text, out PluginGameVersion version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        if (TryParse(text, out version)) return true;

        var value = text.Trim();
        for (var index = 0; index < value.Length; index++)
        {
            if (!char.IsAsciiDigit(value[index])) continue;
            if (TryParse(value[index..], out version)) return true;
        }

        return false;
    }

    public int ComponentAt(int index) => index switch
    {
        0 => _first,
        1 => _second,
        2 => _third,
        _ => _fourth
    };

    public int CompareTo(PluginGameVersion other)
    {
        if (!IsValid || !other.IsValid) return 0;

        var count = Math.Min(Precision, other.Precision);
        for (var index = 0; index < count; index++)
        {
            var difference = ComponentAt(index).CompareTo(other.ComponentAt(index));
            if (difference != 0) return difference;
        }

        return 0;
    }

    public override string ToString()
    {
        if (!IsValid) return string.Empty;

        var builder = new StringBuilder();
        for (var index = 0; index < Precision; index++)
        {
            if (index > 0) builder.Append('.');
            builder.Append(ComponentAt(index).ToString(CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }

    private static bool IsWildcard(string segment) => segment is "*" or "x" or "X";

    private static int CountLeadingDigits(string segment)
    {
        var count = 0;
        while (count < segment.Length && char.IsAsciiDigit(segment[count]))
        {
            count++;
        }

        return count;
    }
}

internal readonly struct PluginGameVersionCondition
{
    private readonly PluginGameVersion _lower;
    private readonly PluginGameVersion _upper;
    private readonly bool _hasLower;
    private readonly bool _hasUpper;
    private readonly bool _lowerInclusive;
    private readonly bool _upperInclusive;

    private PluginGameVersionCondition(PluginGameVersion lower, bool hasLower, bool lowerInclusive,
        PluginGameVersion upper, bool hasUpper, bool upperInclusive)
    {
        _lower = lower;
        _hasLower = hasLower;
        _lowerInclusive = lowerInclusive;
        _upper = upper;
        _hasUpper = hasUpper;
        _upperInclusive = upperInclusive;
    }

    public static PluginGameVersionCondition Any() => new(default, false, false, default, false, false);

    public static PluginGameVersionCondition Exact(PluginGameVersion version) =>
        new(version, true, true, version, true, true);

    public static PluginGameVersionCondition Between(PluginGameVersion lower, PluginGameVersion upper) =>
        new(lower, true, true, upper, true, true);

    public static PluginGameVersionCondition AtLeast(PluginGameVersion version) =>
        new(version, true, true, default, false, false);

    public static PluginGameVersionCondition GreaterThan(PluginGameVersion version) =>
        new(version, true, false, default, false, false);

    public static PluginGameVersionCondition AtMost(PluginGameVersion version) =>
        new(default, false, false, version, true, true);

    public static PluginGameVersionCondition LessThan(PluginGameVersion version) =>
        new(default, false, false, version, true, false);

    public bool IsSatisfiedBy(PluginGameVersion version)
    {
        if (!version.IsValid) return false;

        if (_hasLower)
        {
            var lower = version.CompareTo(_lower);
            if (lower < 0 || (lower == 0 && !_lowerInclusive)) return false;
        }

        if (_hasUpper)
        {
            var upper = version.CompareTo(_upper);
            if (upper > 0 || (upper == 0 && !_upperInclusive)) return false;
        }

        return true;
    }
}

internal sealed class PluginGameVersionRequirement
{
    private enum ComparisonKind
    {
        Exact,
        AtLeast,
        GreaterThan,
        AtMost,
        LessThan
    }

    private static readonly char[] OrSeparators = { ',', '，', '、', ';', '；', '|', '\n', '\r', '\t' };

    private static readonly char[] AndSeparators = { '&', '＆' };

    private static readonly string[] ExclusionPrefixes =
        { "!", "！", "exclude", "except", "not", "不支持", "排除", "除了", "不含" };

    private static readonly string[] AnyKeywords =
        { "*", "any", "all", "anything", "全部", "所有", "任意", "全版本", "通用" };

    private static readonly (string Token, ComparisonKind Kind)[] ComparisonPrefixes =
    {
        (">=", ComparisonKind.AtLeast),
        ("=>", ComparisonKind.AtLeast),
        ("≥", ComparisonKind.AtLeast),
        ("<=", ComparisonKind.AtMost),
        ("=<", ComparisonKind.AtMost),
        ("≤", ComparisonKind.AtMost),
        ("==", ComparisonKind.Exact),
        (">", ComparisonKind.GreaterThan),
        ("<", ComparisonKind.LessThan),
        ("＞", ComparisonKind.GreaterThan),
        ("＜", ComparisonKind.LessThan),
        ("=", ComparisonKind.Exact)
    };

    private static readonly string[] RangeSeparators =
        { "..", "~", "～", "-", "－", "–", "—", " to ", "到", "至" };

    private readonly PluginGameVersionCondition[][] _groups;
    private readonly PluginGameVersionCondition[] _exclusions;

    private PluginGameVersionRequirement(string raw, PluginGameVersionCondition[][] groups,
        PluginGameVersionCondition[] exclusions)
    {
        Raw = raw;
        _groups = groups;
        _exclusions = exclusions;
    }

    public string Raw
    {
        get;
    }

    public static bool TryParse(string? text, out PluginGameVersionRequirement? requirement,
        out string invalidSegment)
    {
        requirement = null;
        invalidSegment = string.Empty;

        if (string.IsNullOrWhiteSpace(text)) return false;

        var raw = text.Trim();
        var normalized = raw
            .Replace("或者", ",", StringComparison.Ordinal)
            .Replace("或", ",", StringComparison.Ordinal)
            .Replace("并且", "&", StringComparison.Ordinal)
            .Replace("且", "&", StringComparison.Ordinal)
            .Replace("排除", ",排除", StringComparison.Ordinal)
            .Replace("除了", ",除了", StringComparison.Ordinal)
            .Replace("不含", ",不含", StringComparison.Ordinal)
            .Replace("不支持", ",不支持", StringComparison.Ordinal)
            .Replace(" except ", ", except ", StringComparison.OrdinalIgnoreCase)
            .Replace(" exclude ", ", exclude ", StringComparison.OrdinalIgnoreCase)
            .Replace(" not ", ", not ", StringComparison.OrdinalIgnoreCase)
            .Replace("!", ",!", StringComparison.Ordinal)
            .Replace("！", ",！", StringComparison.Ordinal);

        var groups = new List<PluginGameVersionCondition[]>();
        var exclusions = new List<PluginGameVersionCondition>();
        var clauseCount = 0;

        foreach (var orPart in normalized.Split(OrSeparators, StringSplitOptions.RemoveEmptyEntries))
        {
            var conditions = new List<PluginGameVersionCondition>();

            foreach (var andPart in orPart.Split(AndSeparators, StringSplitOptions.RemoveEmptyEntries))
            {
                var segment = andPart.Trim();
                if (segment.Length == 0) continue;

                if (!TryParseCondition(segment, out var condition, out var excluded))
                {
                    invalidSegment = segment;
                    return false;
                }

                clauseCount++;
                if (excluded)
                {
                    exclusions.Add(condition);
                }
                else
                {
                    conditions.Add(condition);
                }
            }

            if (conditions.Count > 0)
            {
                groups.Add(conditions.ToArray());
            }
        }

        if (clauseCount == 0)
        {
            invalidSegment = raw;
            return false;
        }

        requirement = new PluginGameVersionRequirement(raw, groups.ToArray(), exclusions.ToArray());
        return true;
    }

    public bool IsSatisfiedBy(PluginGameVersion version)
    {
        if (!version.IsValid) return false;

        foreach (var exclusion in _exclusions)
        {
            if (exclusion.IsSatisfiedBy(version)) return false;
        }

        if (_groups.Length == 0) return true;

        foreach (var group in _groups)
        {
            var satisfied = true;
            foreach (var condition in group)
            {
                if (condition.IsSatisfiedBy(version)) continue;
                satisfied = false;
                break;
            }

            if (satisfied) return true;
        }

        return false;
    }

    public bool IsSatisfiedByGameVersion(string? gameVersion) =>
        PluginGameVersion.TryParse(gameVersion, out var version) && IsSatisfiedBy(version);

    private static bool TryParseCondition(string segment, out PluginGameVersionCondition condition,
        out bool excluded)
    {
        condition = default;

        var value = StripExclusionPrefix(segment, out excluded).Trim();
        if (value.Length == 0) return false;

        if (IsAnyKeyword(value))
        {
            condition = PluginGameVersionCondition.Any();
            return true;
        }

        if (TryParseComparison(value, out var kind, out var remainder))
        {
            if (!PluginGameVersion.TryParse(remainder, out var compared)) return false;
            condition = kind switch
            {
                ComparisonKind.AtLeast => PluginGameVersionCondition.AtLeast(compared),
                ComparisonKind.GreaterThan => PluginGameVersionCondition.GreaterThan(compared),
                ComparisonKind.AtMost => PluginGameVersionCondition.AtMost(compared),
                ComparisonKind.LessThan => PluginGameVersionCondition.LessThan(compared),
                _ => PluginGameVersionCondition.Exact(compared)
            };
            return true;
        }

        if (value.EndsWith('+'))
        {
            if (!PluginGameVersion.TryParse(value[..^1].Trim(), out var minimum)) return false;
            condition = PluginGameVersionCondition.AtLeast(minimum);
            return true;
        }

        var separatorIndex = FindRangeSeparator(value);
        if (separatorIndex.Index >= 0)
        {
            var lowerText = value[..separatorIndex.Index].Trim();
            var upperText = value[(separatorIndex.Index + separatorIndex.Length)..].Trim();

            if (lowerText.Length == 0 && upperText.Length == 0) return false;

            if (lowerText.Length == 0)
            {
                if (!PluginGameVersion.TryParse(upperText, out var onlyUpper)) return false;
                condition = PluginGameVersionCondition.AtMost(onlyUpper);
                return true;
            }

            if (upperText.Length == 0)
            {
                if (!PluginGameVersion.TryParse(lowerText, out var onlyLower)) return false;
                condition = PluginGameVersionCondition.AtLeast(onlyLower);
                return true;
            }

            if (!PluginGameVersion.TryParse(lowerText, out var lower)) return false;
            if (!PluginGameVersion.TryParse(upperText, out var upper)) return false;
            if (lower.CompareTo(upper) > 0) return false;

            condition = PluginGameVersionCondition.Between(lower, upper);
            return true;
        }

        if (!PluginGameVersion.TryParse(value, out var exact)) return false;
        condition = PluginGameVersionCondition.Exact(exact);
        return true;
    }

    private static string StripExclusionPrefix(string segment, out bool excluded)
    {
        excluded = false;

        foreach (var prefix in ExclusionPrefixes)
        {
            if (!segment.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
            excluded = true;
            return segment[prefix.Length..];
        }

        return segment;
    }

    private static bool IsAnyKeyword(string value) =>
        AnyKeywords.Any(keyword => string.Equals(keyword, value, StringComparison.OrdinalIgnoreCase));

    private static bool TryParseComparison(string text, out ComparisonKind kind, out string remainder)
    {
        kind = ComparisonKind.Exact;
        remainder = text;

        foreach (var (token, comparison) in ComparisonPrefixes)
        {
            if (!text.StartsWith(token, StringComparison.Ordinal)) continue;
            kind = comparison;
            remainder = text[token.Length..].Trim();
            return true;
        }

        return false;
    }

    private static (int Index, int Length) FindRangeSeparator(string text)
    {
        var index = -1;
        var length = 0;

        foreach (var token in RangeSeparators)
        {
            var found = text.IndexOf(token, StringComparison.OrdinalIgnoreCase);
            if (found < 0) continue;
            if (index >= 0 && found > index) continue;
            if (found == index && token.Length <= length) continue;

            index = found;
            length = token.Length;
        }

        return (index, length);
    }
}
