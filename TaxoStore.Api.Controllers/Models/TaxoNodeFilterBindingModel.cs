using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using TaxoStore.Core;

namespace TaxoStore.Api.Controllers.Models;

/// <summary>
/// A DTO with the set of filter criteria used to query or paginate nodes in a
/// hierarchical data structure.
/// </summary>
/// <remarks>This model is used to bind incoming filter parameters for node
/// queries in API requests. It supports filtering by tree, parent, key, label,
/// features, mappings and leaf status, as well as pagination through page number and page
/// size. All properties are optional except for pagination parameters, which
/// are required and validated for range. The filter can be converted to a
/// domain-specific filter object using the ToNodeFilter method.</remarks>
public class TaxoNodeFilterBindingModel
{
    /// <summary>
    /// The page number for paginated results.
    /// </summary>
    [Required]
    [Range(1, int.MaxValue)]
    public int PageNumber { get; set; } = 1;

    /// <summary>
    /// The page size for paginated results. When 0, paging is disabled
    /// and all the matching nodes are returned.
    /// </summary>
    [Required]
    [Range(0, 100)]
    public int PageSize { get; set; } = 20;

    /// <summary>
    /// The node's tree ID (key).
    /// </summary>
    [MaxLength(100)]
    public string? TreeId { get; set; }

    /// <summary>
    /// The node's parent ID.
    /// </summary>
    public int? ParentId { get; set; }

    /// <summary>
    /// Any part of the node's key.
    /// </summary>
    [MaxLength(500)]
    public string? Key { get; set; }

    /// <summary>
    /// Any part of the node's parent's key.
    /// </summary>
    [MaxLength(500)]
    public string? ParentKey { get; set; }

    /// <summary>
    /// The exact key of an ancestor node, if any: when set, only descendants
    /// of the node(s) with this key are matched.
    /// </summary>
    [MaxLength(500)]
    public string? AncestorKey { get; set; }

    /// <summary>
    /// Any part of the node's filtered label. When
    /// <see cref="FilteredMappingLabel"/> is also set, the two criteria are
    /// combined with OR.
    /// </summary>
    [MaxLength(1000)]
    public string? FilteredLabel { get; set; }

    /// <summary>
    /// Any part of the filtered label of any of the node's mappings. When
    /// <see cref="FilteredLabel"/> is also set, a node matches if either its
    /// own filtered label or that of any of its mappings matches.
    /// </summary>
    [MaxLength(500)]
    public string? FilteredMappingLabel { get; set; }

    /// <summary>
    /// The features to match, each as <c>name</c> (any value),
    /// <c>name=value</c> (exact filtered value) or <c>name~value</c>
    /// (any part of the filtered value). In a query string, repeat the
    /// parameter for each feature (e.g. <c>features=a=1&amp;features=b</c>).
    /// Invalid entries (empty or without name) are ignored.
    /// </summary>
    [MaxLength(20)]
    public IList<string>? Features { get; set; }

    /// <summary>
    /// The mode used to combine the criteria in <see cref="Features"/>:
    /// all (default), any, or none of them must be matched.
    /// </summary>
    public NodeFeatureMatchMode FeatureMatchMode { get; set; } =
        NodeFeatureMatchMode.All;

    /// <summary>
    /// True to match only leaf nodes (=nodes with no children), false to match
    /// only non-leaf nodes, null to ignore this criterion.
    /// </summary>
    public bool? IsLeaf { get; set; }

    /// <summary>
    /// If true, include X (sibling position) and Y (depth) in the response.
    /// Default is false for better performance on bulk queries.
    /// </summary>
    public bool IncludePosition { get; set; }

    /// <summary>
    /// When true, restricts results to root nodes (parent_id IS NULL).
    /// </summary>
    public bool IsRoot { get; set; }

    /// <summary>
    /// When true and <see cref="FilteredLabel"/>, <see cref="FilteredMappingLabel"/>
    /// and/or <see cref="Features"/> are set, a node is included if it
    /// directly matches these criteria OR if any of its descendants matches
    /// all of them by itself.
    /// </summary>
    public bool MatchDescendants { get; set; }

    /// <summary>
    /// The exact scope of a mapping of the node: when set, only nodes having
    /// at least one mapping with this scope are matched.
    /// </summary>
    [MaxLength(100)]
    public string? MappingScope { get; set; }

    /// <summary>
    /// The exact target of a mapping of the node: when set, only nodes having
    /// at least one mapping with this target (and with
    /// <see cref="MappingScope"/>, when set) are matched.
    /// </summary>
    [MaxLength(500)]
    public string? MappingTarget { get; set; }

    /// <summary>
    /// If true, include the mappings of each node in the response.
    /// Default is false for better performance.
    /// </summary>
    public bool IncludeMappings { get; set; }

    /// <summary>
    /// If true, include the features of each node in the response.
    /// Default is false for better performance.
    /// </summary>
    public bool IncludeFeatures { get; set; }

    /// <summary>
    /// Creates a new <see cref="TaxoNodeFilter"/> instance populated with the
    /// current filter criteria.
    /// </summary>
    /// <returns>A <see cref="TaxoNodeFilter"/> object containing the values of
    /// the current filter properties.</returns>
    public TaxoNodeFilter ToNodeFilter()
    {
        return new TaxoNodeFilter
        {
            PageNumber = PageNumber,
            PageSize = PageSize,
            TreeId = TreeId,
            ParentId = ParentId,
            Key = Key,
            ParentKey = ParentKey,
            AncestorKey = AncestorKey,
            FilteredLabel = FilteredLabel,
            FilteredMappingLabel = FilteredMappingLabel,
            Features = Features == null ? null :
            [
                .. Features.Select(TaxoNodeFeatureFilter.Parse)
                    .Where(f => f != null)
                    .Select(f => f!)
            ],
            FeatureMatchMode = FeatureMatchMode,
            IsLeaf = IsLeaf,
            IsRoot = IsRoot,
            MatchDescendants = MatchDescendants,
            MappingScope = MappingScope,
            MappingTarget = MappingTarget
        };
    }
}
