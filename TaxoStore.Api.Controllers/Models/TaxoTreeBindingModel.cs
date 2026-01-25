using System.ComponentModel.DataAnnotations;
using TaxoStore.Core;

namespace TaxoStore.Api.Controllers.Models;

/// <summary>
/// Represents a data transfer object for binding tree information in create
/// or update operations.
/// </summary>
public class TaxoTreeBindingModel
{
    /// <summary>
    /// The tree's unique identifier (key). This is the external identifier
    /// used by consumers to reference the tree.
    /// </summary>
    [Required]
    [MaxLength(100)]
    public string Id { get; set; } = "";

    /// <summary>
    /// The tree's human-friendly display name.
    /// </summary>
    [Required]
    [MaxLength(500)]
    public string Name { get; set; } = "";

    /// <summary>
    /// An optional note about the tree.
    /// </summary>
    [MaxLength(5000)]
    public string? Note { get; set; }

    /// <summary>
    /// Creates a new Tree instance that contains the current object's Id,
    /// Name, and Note values.
    /// </summary>
    /// <returns>A Tree object initialized with the Id, Name, and Note
    /// properties of the current instance.</returns>
    public TaxoTree ToTree()
    {
        return new TaxoTree
        {
            Id = Id,
            Name = Name,
            Note = Note
        };
    }
}
