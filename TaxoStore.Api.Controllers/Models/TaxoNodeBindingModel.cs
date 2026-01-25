using System;
using System.ComponentModel.DataAnnotations;
using TaxoStore.Core;

namespace TaxoStore.Api.Controllers.Models;

/// <summary>
/// Represents a data transfer object for a node within a hierarchical tree
/// structure, including identifiers, labels, and optional metadata.
/// </summary>
/// <remarks>This model is typically used for binding node data in API requests
/// or responses. Consumers should ensure that required fields are provided and
/// adhere to any specified length or range constraints.</remarks>
public class TaxoNodeBindingModel
{
    /// <summary>
    /// The node's ID. This is unique in the whole store.
    /// </summary>
    [Range(0, int.MaxValue)]
    public int Id { get; set; }

    /// <summary>
    /// The ID of the parent node, if any. Null if it has no parent.
    /// </summary>
    public int? ParentId { get; set; }

    /// <summary>
    /// The ID of the tree the node belongs to. This references the tree's
    /// unique string identifier (key).
    /// </summary>
    [Required]
    [MaxLength(100)]
    public string TreeId { get; set; } = "";

    /// <summary>
    /// The key identifying this node. This should be unique within the same
    /// tree and can thus be used as a human-friendly identifier for it in the
    /// context of a single set.
    /// </summary>
    [Required]
    [MaxLength(500)]
    public string Key { get; set; } = "";

    /// <summary>
    /// The node's label.
    /// </summary>
    [Required]
    [MaxLength(1000)]
    public string Label { get; set; } = "";

    /// <summary>
    /// The filtered version of <see cref="Label"/>. This should be set when
    /// setting label following some filtering procedure (e.g. remove diacritics,
    /// lowercase characters, etc.).
    /// </summary>
    [MaxLength(1000)]
    public string? FilteredLabel { get; set; }

    /// <summary>
    /// Flags attached to this node. Each character in the string is a flag.
    /// For instance, <c>o</c>=obsolete, <c>d</c>=draft, etc. The value of
    /// each character is defined by consumer code.
    /// </summary>
    [MaxLength(50)]
    public string? Flags { get; set; }

    /// <summary>
    /// An optional note attached to this node.
    /// </summary>
    [MaxLength(5000)]
    public string? Note { get; set; }

    /// <summary>
    /// Creates a new <see cref="TaxoNode"/> instance that represents the current
    /// object's state.
    /// </summary>
    /// <returns>A <see cref="TaxoNode"/> object populated with the current values
    /// of the relevant properties.</returns>
    public TaxoNode ToNode()
    {
        return new TaxoNode
        {
            Id = Id,
            ParentId = ParentId,
            TreeId = TreeId,
            Key = Key,
            Label = Label,
            FilteredLabel = FilteredLabel ?? Label,
            Flags = Flags,
            Note = Note
        };
    }
}
