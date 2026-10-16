using System.ComponentModel.DataAnnotations;
using TaxoStore.Core;

namespace TaxoStore.Api.Controllers.Models;

/// <summary>
/// Represents a data transfer object for a generic name=value feature of
/// a node.
/// </summary>
public class TaxoNodeFeatureBindingModel
{
    /// <summary>
    /// The feature's name. This cannot include <c>=</c> or <c>~</c>.
    /// </summary>
    [Required]
    [MaxLength(50)]
    [RegularExpression("^[^=~]+$",
        ErrorMessage = "Feature names cannot include = or ~")]
    public string Name { get; set; } = "";

    /// <summary>
    /// The feature's value. This can be empty.
    /// </summary>
    [MaxLength(500)]
    public string? Value { get; set; }

    /// <summary>
    /// The optional filtered version of <see cref="Value"/>, used for
    /// searching. When not set, the value is used.
    /// </summary>
    [MaxLength(500)]
    public string? FilteredValue { get; set; }

    /// <summary>
    /// Creates a new <see cref="TaxoNodeFeature"/> instance that represents
    /// the current object's state.
    /// </summary>
    /// <returns>The feature.</returns>
    public TaxoNodeFeature ToFeature()
    {
        return new TaxoNodeFeature
        {
            Name = Name,
            Value = Value ?? "",
            FilteredValue = FilteredValue
        };
    }
}
