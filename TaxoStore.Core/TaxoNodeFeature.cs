namespace TaxoStore.Core;

/// <summary>
/// A generic feature of a <see cref="TaxoNode"/>, i.e. a name=value pair
/// used to attach any attribute to it (e.g. <c>status</c>=<c>draft</c>).
/// A node can have any number of features, also with the same name (e.g.
/// <c>son</c>=<c>Mark</c> and <c>son</c>=<c>Jane</c> for the two sons of a
/// person). A feature with an empty value can be used as a boolean marker
/// (e.g. <c>obsolete</c>), whose presence is all what matters.
/// </summary>
public class TaxoNodeFeature
{
    /// <summary>
    /// The feature's name (max 50 characters). This cannot be empty, nor
    /// include <c>=</c> or <c>~</c>, which are reserved for the syntax of
    /// feature filters (see <see cref="TaxoNodeFeatureFilter"/>).
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// The feature's value (max 500 characters). This can be empty.
    /// </summary>
    public string Value { get; set; } = "";

    /// <summary>
    /// The filtered ("index") version of <see cref="Value"/>, used for
    /// searching features by value with the same criteria used for
    /// <see cref="TaxoNode.FilteredLabel"/>, except that digits are kept.
    /// When not set, stores fall back to <see cref="Value"/>.
    /// </summary>
    public string? FilteredValue { get; set; }

    /// <summary>
    /// Returns a string representing this feature.
    /// </summary>
    /// <returns>String.</returns>
    public override string ToString()
    {
        return $"{Name}={Value}";
    }
}
