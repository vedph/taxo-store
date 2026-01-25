using System;
using System.ComponentModel.DataAnnotations;
using TaxoStore.Core;

namespace TaxoStore.Api.Controllers.Models;

/// <summary>
/// A DTO with the set of filter criteria used to query or paginate nodes in a
/// hierarchical data structure.
/// </summary>
/// <remarks>This model is used to bind incoming filter parameters for node
/// queries in API requests. It supports filtering by tree, parent, key, label,
/// flags, and leaf status, as well as pagination through page number and page
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
    /// The page size for paginated results.
    /// </summary>
    [Required]
    [Range(0, 100)]
    public int PageSize { get; set; } = 20;

    /// <summary>
    /// The node's tree ID (key).
    /// </summary>
    public string? TreeId { get; set; }

    /// <summary>
    /// The node's parent ID.
    /// </summary>
    public int? ParentId { get; set; }

    /// <summary>
    /// Any part of the node's key.
    /// </summary>
    public string? Key { get; set; }

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
    public NodeFlagMatchMode FlagMatchMode { get; set; }

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
            Flags = Flags,
            FlagMatchMode = FlagMatchMode,
            IsLeaf = IsLeaf
        };
    }
}
