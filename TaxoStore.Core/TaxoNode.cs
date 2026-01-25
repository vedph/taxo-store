using System.Text;

namespace TaxoStore.Core;

/// <summary>
/// A node in a <see cref="TaxoTree"/>.
/// </summary>
public class TaxoNode
{
    /// <summary>
    /// The node's ID. This is unique in the whole store.
    /// </summary>
    public int Id {  get; set; }

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
    /// Returns a string representing this node.
    /// </summary>
    /// <returns></returns>
    public override string ToString()
    {
        StringBuilder sb = new();

        sb.Append(TreeId).Append('#').Append(Id);
        if (ParentId != null)
            sb.Append('\u2190').Append(ParentId);
        sb.Append(' ').Append(Key);
        sb.Append(": ").Append(Label);

        if (!string.IsNullOrEmpty(Flags))
            sb.Append('[').Append(Flags).Append(']');

        return sb.ToString();
    }
}
