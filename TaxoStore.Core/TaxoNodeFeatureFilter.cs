using System;

namespace TaxoStore.Core;

/// <summary>
/// A criterion for matching a <see cref="TaxoNodeFeature"/> of a node in
/// <see cref="TaxoNodeFilter.Features"/>. The feature name is always matched
/// exactly (case-sensitive), while its value is matched against the
/// feature's <see cref="TaxoNodeFeature.FilteredValue"/>.
/// </summary>
/// <remarks>This has a compact text representation, used e.g. in API query
/// strings (see <see cref="Parse(string?)"/>):
/// <list type="bullet">
/// <item><c>name</c>: any node having a feature with that name, whatever
/// its value;</item>
/// <item><c>name=value</c>: the feature filtered value is equal to
/// value;</item>
/// <item><c>name~value</c>: the feature filtered value contains value
/// (case-insensitive).</item>
/// </list>
/// </remarks>
public class TaxoNodeFeatureFilter
{
    /// <summary>
    /// The exact name of the feature.
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// The value of the feature to match, if any. This is compared with the
    /// feature's filtered value, so it should be filtered with the same
    /// criteria. When null, any feature with <see cref="Name"/> is matched,
    /// whatever its value; when empty, only features with an empty value
    /// are matched.
    /// </summary>
    public string? Value { get; set; }

    /// <summary>
    /// True to match any part of the feature's filtered value
    /// (case-insensitive), rather than the whole value. This is used only
    /// when <see cref="Value"/> is not empty.
    /// </summary>
    public bool IsPartial { get; set; }

    /// <summary>
    /// Parses the specified text into a feature filter. The text is
    /// <c>name</c>, <c>name=value</c> (exact value), or <c>name~value</c>
    /// (partial value), where the first <c>=</c> or <c>~</c> separates the
    /// name from the value.
    /// </summary>
    /// <param name="text">The text to parse.</param>
    /// <returns>The filter, or null if the text is empty or has no name.
    /// </returns>
    public static TaxoNodeFeatureFilter? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        int i = text.IndexOfAny(['=', '~']);
        if (i == -1) return new TaxoNodeFeatureFilter { Name = text.Trim() };
        if (i == 0) return null;

        string value = text[(i + 1)..];
        return new TaxoNodeFeatureFilter
        {
            Name = text[..i].Trim(),
            Value = value,
            IsPartial = text[i] == '~' && value.Length > 0
        };
    }

    /// <summary>
    /// Returns a string representing this filter, in the same format parsed
    /// by <see cref="Parse(string?)"/>.
    /// </summary>
    /// <returns>String.</returns>
    public override string ToString()
    {
        if (Value == null) return Name;
        return Name + (IsPartial && Value.Length > 0 ? '~' : '=') + Value;
    }
}
