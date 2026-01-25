using Fusi.Tools.Data;

namespace TaxoStore.Core;

/// <summary>
/// A filter for <see cref="TaxoTree"/>'s.
/// </summary>
public class TaxoTreeFilter : PagingOptions, IPagingOptions
{
    /// <summary>
    /// Any part of the tree's name to match.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Returns a string representation of this object.
    /// </summary>
    /// <returns>String.</returns>
    public override string ToString()
    {
        return string.IsNullOrEmpty(nameof(Name))
            ? base.ToString(): $"Name={Name}";
    }
}
