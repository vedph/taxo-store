using System;
using System.ComponentModel.DataAnnotations;
using TaxoStore.Core;

namespace TaxoStore.Api.Controllers.Models;

/// <summary>
/// A DTO with the parameters used to filter and paginate tree data in a query
/// operation.
/// </summary>
/// <remarks>Use this model to specify filtering criteria and pagination
/// settings when requesting a list of trees. All properties must be set to
/// valid values to ensure correct filtering and paging behavior.</remarks>
public class TaxoTreeFilterBindingModel
{
    /// <summary>
    /// The page number for paginated results.
    /// </summary>
    [Required]
    [Range(1, int.MaxValue)]
    public int PageNumber { get; set; } = 1;

    /// <summary>
    /// The number of items per page for paginated results.
    /// </summary>
    [Required]
    [Range(1, 100)]
    public int PageSize { get; set; } = 20;

    /// <summary>
    /// Any part of the tree's name to match.
    /// </summary>
    [MaxLength(100)]
    public string? Name { get; set; }

    /// <summary>
    /// Converts this binding model to a TreeFilter instance.
    /// </summary>
    /// <returns>TreeFilter.</returns>
    public TaxoTreeFilter ToFilter()
    {
        return new TaxoTreeFilter
        {
            PageNumber = PageNumber,
            PageSize = PageSize,
            Name = Name,
        };
    }
}
