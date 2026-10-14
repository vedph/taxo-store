namespace TaxoStore.Core;

/// <summary>
/// A mapping from a <see cref="TaxoNode"/> to an equivalent entity in an
/// external resource, like an authority list (e.g. VIAF, a gazetteer, etc.).
/// A mapping is identified by its node, scope and target, so that a node
/// cannot have more than one mapping with the same scope and target.
/// </summary>
public class TaxoNodeMapping
{
    /// <summary>
    /// The scope of the target, i.e. the identifier of the external resource
    /// the <see cref="Target"/> belongs to (e.g. <c>viaf</c>).
    /// </summary>
    public string Scope { get; set; } = "";

    /// <summary>
    /// The target ID, i.e. the identifier of the equivalent entity in the
    /// external resource defined by <see cref="Scope"/> (e.g. a VIAF ID).
    /// </summary>
    public string Target { get; set; } = "";

    /// <summary>
    /// An optional human-friendly label for the target.
    /// </summary>
    public string? Label { get; set; }

    /// <summary>
    /// The filtered version of <see cref="Label"/>, used for searching
    /// mappings (e.g. aliases of the mapped node) with the same criteria
    /// used for <see cref="TaxoNode.FilteredLabel"/>. When not set, stores
    /// fall back to <see cref="Label"/>.
    /// </summary>
    public string? FilteredLabel { get; set; }

    /// <summary>
    /// Returns a string representing this mapping.
    /// </summary>
    /// <returns>String.</returns>
    public override string ToString()
    {
        return string.IsNullOrEmpty(Label)
            ? $"{Scope}:{Target}"
            : $"{Scope}:{Target} ({Label})";
    }
}
