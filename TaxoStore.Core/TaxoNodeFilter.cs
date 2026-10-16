using Fusi.Tools.Data;
using System.Collections.Generic;
using System.Text;

namespace TaxoStore.Core;

/// <summary>
/// A filter for <see cref="TaxoNode"/>'s. When <see cref="PagingOptions.PageSize"/>
/// is 0, paging is disabled and all the matching nodes are returned.
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
    /// The exact key of an ancestor node, if any. When set, only descendants
    /// (at any depth) of the node(s) with this key are matched; the ancestor
    /// node itself is not included. If <see cref="TreeId"/> is set, the
    /// ancestor is searched only in that tree.
    /// </summary>
    public string? AncestorKey { get; set; }

    /// <summary>
    /// Any part of the node's filtered label. When
    /// <see cref="FilteredMappingLabel"/> is also set, a node matches if
    /// either its filtered label matches this, or the filtered label of any
    /// of its mappings matches <see cref="FilteredMappingLabel"/>.
    /// </summary>
    public string? FilteredLabel { get; set; }

    /// <summary>
    /// Any part of the filtered label of any of the node's mappings. This
    /// is typically used to find nodes also via their aliases stored as
    /// mappings. When <see cref="FilteredLabel"/> is also set, the two
    /// criteria are combined with OR rather than AND: a node matches if
    /// either its own filtered label or the filtered label of any of its
    /// mappings matches. Usually, both are set to the same value.
    /// </summary>
    public string? FilteredMappingLabel { get; set; }

    /// <summary>
    /// The features to match. Each criterion is matched by any feature of
    /// the node having its name and, when specified, its (exact or partial)
    /// value. Criteria are combined according to
    /// <see cref="FeatureMatchMode"/>.
    /// </summary>
    public IList<TaxoNodeFeatureFilter>? Features { get; set; }

    /// <summary>
    /// The mode used to combine the criteria in <see cref="Features"/>.
    /// The default is <see cref="NodeFeatureMatchMode.All"/>.
    /// </summary>
    public NodeFeatureMatchMode FeatureMatchMode { get; set; } =
        NodeFeatureMatchMode.All;

    /// <summary>
    /// True to match only leaf nodes (=nodes with no children), false to match
    /// only non-leaf nodes, null to ignore this criterion.
    /// </summary>
    public bool? IsLeaf { get; set; }

    /// <summary>
    /// When true, restricts results to root nodes (parent_id IS NULL).
    /// This allows to query root nodes with full filter support.
    /// </summary>
    public bool IsRoot { get; set; }

    /// <summary>
    /// When true and <see cref="FilteredLabel"/>, <see cref="FilteredMappingLabel"/>
    /// and/or <see cref="Features"/> are set, a node is included if it
    /// directly matches these criteria OR if any of its descendants matches
    /// them. A matching node must match all these criteria by itself (e.g.
    /// a descendant matching the label and another one matching the
    /// features do not make their ancestor match). All the other criteria
    /// still apply to the included nodes themselves. Implemented via a
    /// reverse-recursive CTE that walks from matching nodes up to their
    /// ancestors. This can be combined with <see cref="AncestorKey"/>.
    /// </summary>
    public bool MatchDescendants { get; set; }

    /// <summary>
    /// The exact scope of a mapping of the node. When set, only nodes having
    /// at least one mapping with this scope are matched. When combined with
    /// <see cref="MappingTarget"/>, both must be matched by the same mapping.
    /// </summary>
    public string? MappingScope { get; set; }

    /// <summary>
    /// The exact target of a mapping of the node. When set, only nodes having
    /// at least one mapping with this target are matched. This is usually
    /// combined with <see cref="MappingScope"/>, to find the node(s) mapped
    /// to a specific entity of an external resource.
    /// </summary>
    public string? MappingTarget { get; set; }

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

        if (!string.IsNullOrEmpty(FilteredMappingLabel))
        {
            sb.Append("FilteredMappingLabel=").Append(FilteredMappingLabel)
              .Append(';');
        }

        if (Features?.Count > 0)
        {
            sb.Append("Features=").AppendJoin(", ", Features)
              .Append('(').Append(FeatureMatchMode).Append(");");
        }

        if (IsLeaf != null)
            sb.Append("IsLeaf=").Append(IsLeaf).Append(';');

        if (IsRoot) sb.Append("IsRoot;");

        if (MatchDescendants) sb.Append("MatchDescendants;");

        if (!string.IsNullOrEmpty(MappingScope))
            sb.Append("MappingScope=").Append(MappingScope).Append(';');

        if (!string.IsNullOrEmpty(MappingTarget))
            sb.Append("MappingTarget=").Append(MappingTarget).Append(';');

        return sb.ToString();
    }
}

/// <summary>
/// Specifies the modes used to combine the criteria of a features filter
/// (<see cref="TaxoNodeFilter.Features"/>).
/// </summary>
public enum NodeFeatureMatchMode
{
    /// <summary>
    /// None of the criteria must be matched by any feature of the node.
    /// </summary>
    None,
    /// <summary>
    /// Each criterion must be matched by a feature of the node (not
    /// necessarily the same one).
    /// </summary>
    All,
    /// <summary>
    /// At least one of the criteria must be matched by a feature of the node.
    /// </summary>
    Any
}
