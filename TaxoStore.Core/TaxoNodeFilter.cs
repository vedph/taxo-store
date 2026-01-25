using Fusi.Tools.Data;
using System.Text;

namespace TaxoStore.Core;

/// <summary>
/// A filter for <see cref="TaxoNode"/>'s.
/// </summary>
public class TaxoNodeFilter : PagingOptions, IPagingOptions
{
    /// <summary>
    /// The node's tree ID.
    /// </summary>
    public string? TreeId { get; set; }

    /// <summary>
    /// The node's parent ID.
    /// </summary>
    public int? ParentId { get; set; }

    /// <summary>
    /// Any part of the node's key.
    /// </summary>
    public string? Key {  get; set; }

    /// <summary>
    /// Any part of the node's parent's key.
    /// </summary>
    public string? ParentKey { get; set; }

    /// <summary>
    /// The key of the ancestor node, if any.
    /// </summary>
    public string? AncestorKey { get; set; }

    /// <summary>
    /// Any part of the node's filtered label.
    /// </summary>
    public string? FilteredLabel { get; set; }

    /// <summary>
    /// The flags to match.
    /// </summary>
    public string? Flags { get; set; }

    /// <summary>
    /// The mode used to match node flags against <see cref="Flags"/>.
    /// </summary>
    /// <remarks>The selected match mode determines how node flags are compared
    /// when performing flag-based operations.</remarks>
    public NodeFlagMatchMode FlagMatchMode { get; set; } = NodeFlagMatchMode.Any;

    /// <summary>
    /// True to match only leaf nodes (=nodes with no children), false to match
    /// only non-leaf nodes, null to ignore this criterion.
    /// </summary>
    public bool? IsLeaf { get; set; }

    /// <summary>
    /// Returns a string representing this object.
    /// </summary>
    /// <returns>String.</returns>
    public override string ToString()
    {
        StringBuilder sb = new();

        if (TreeId != null) sb.Append("TreeId=").Append(TreeId).Append(';');

        if (ParentId != null) sb.Append("ParentId=").Append(ParentId).Append(';');

        if (!string.IsNullOrEmpty(Key))
            sb.Append("Key=").Append(Key).Append(';');

        if (!string.IsNullOrEmpty(ParentKey))
            sb.Append("ParentKey=").Append(ParentKey).Append(';');

        if (!string.IsNullOrEmpty(AncestorKey))
            sb.Append("AncestorKey=").Append(AncestorKey).Append(';');

        if (!string.IsNullOrEmpty(FilteredLabel))
            sb.Append("FilteredLabel=").Append(FilteredLabel).Append(';');

        if (!string.IsNullOrEmpty(Flags))
            sb.Append("Flags=").Append(Flags)
              .Append('(').Append(FlagMatchMode).Append(");");

        if (IsLeaf != null)
            sb.Append("IsLeaf=").Append(IsLeaf).Append(';');

        return sb.ToString();
    }
}

/// <summary>
/// Specifies the modes used to determine how node flags are matched.
/// </summary>
/// <remarks>Use this enumeration to control whether no flags, all specified
/// flags, or any specified flag must be present when evaluating node flags.
/// The selected mode affects how flag checks are performed in APIs that support
/// flag-based filtering or selection.</remarks>
public enum NodeFlagMatchMode
{
    /// <summary>
    /// No flag matching.
    /// </summary>
    None,
    /// <summary>
    /// All specified flags must be present.
    /// </summary>
    All,
    /// <summary>
    /// At least one of the specified flags must be present.
    /// </summary>
    Any
}
