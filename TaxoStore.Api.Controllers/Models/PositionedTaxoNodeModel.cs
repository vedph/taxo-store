namespace TaxoStore.Api.Controllers.Models;

/// <summary>
/// A node with computed position properties for tree visualization.
/// </summary>
/// <remarks>This model extends the base node data with X (sibling position)
/// and Y (depth level) properties that are computed at runtime from the
/// tree structure, not stored in the database.</remarks>
public class PositionedTaxoNodeModel
{
    /// <summary>
    /// The node's ID. This is unique in the whole store.
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// The ID of the parent node, if any. Null if it has no parent.
    /// </summary>
    public int? ParentId { get; set; }

    /// <summary>
    /// The ID of the tree the node belongs to. This references the tree's
    /// unique string identifier (key).
    /// </summary>
    public string TreeId { get; set; } = "";

    /// <summary>
    /// The key identifying this node. This should be unique within the same
    /// tree and can thus be used as a human-friendly identifier for it in the
    /// context of a single set.
    /// </summary>
    public string Key { get; set; } = "";

    /// <summary>
    /// The node's label.
    /// </summary>
    public string Label { get; set; } = "";

    /// <summary>
    /// The filtered version of <see cref="Label"/>. This should be set when
    /// setting label following some filtering procedure (e.g. remove diacritics,
    /// lowercase characters, etc.).
    /// </summary>
    public string FilteredLabel { get; set; } = "";

    /// <summary>
    /// Flags attached to this node. Each character in the string is a flag.
    /// For instance, <c>o</c>=obsolete, <c>d</c>=draft, etc. The value of
    /// each character is defined by consumer code.
    /// </summary>
    public string? Flags { get; set; }

    /// <summary>
    /// An optional note attached to this node.
    /// </summary>
    public string? Note { get; set; }

    /// <summary>
    /// The depth level (1-based). Root nodes have Y=1, their children Y=2, etc.
    /// </summary>
    public int Y { get; set; }

    /// <summary>
    /// The sibling position (1-based), ordered by key. The first sibling
    /// (alphabetically by key) has X=1, the second X=2, etc.
    /// </summary>
    public int X { get; set; }

    /// <summary>
    /// Indicates whether the node has any children.
    /// </summary>
    public bool HasChildren { get; set; }
}
