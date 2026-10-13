using System.ComponentModel.DataAnnotations;
using TaxoStore.Core;

namespace TaxoStore.Api.Controllers.Models;

/// <summary>
/// Represents a data transfer object for the mapping of a node to an
/// equivalent entity in an external resource.
/// </summary>
public class TaxoNodeMappingBindingModel
{
    /// <summary>
    /// The scope of the target, i.e. the identifier of the external resource
    /// the <see cref="Target"/> belongs to (e.g. <c>viaf</c>).
    /// </summary>
    [Required]
    [MaxLength(100)]
    public string Scope { get; set; } = "";

    /// <summary>
    /// The target ID, i.e. the identifier of the equivalent entity in the
    /// external resource defined by <see cref="Scope"/>.
    /// </summary>
    [Required]
    [MaxLength(500)]
    public string Target { get; set; } = "";

    /// <summary>
    /// An optional human-friendly label for the target.
    /// </summary>
    [MaxLength(500)]
    public string? Label { get; set; }

    /// <summary>
    /// Creates a new <see cref="TaxoNodeMapping"/> instance that represents
    /// the current object's state.
    /// </summary>
    /// <returns>The mapping.</returns>
    public TaxoNodeMapping ToMapping()
    {
        return new TaxoNodeMapping
        {
            Scope = Scope,
            Target = Target,
            Label = Label
        };
    }
}
