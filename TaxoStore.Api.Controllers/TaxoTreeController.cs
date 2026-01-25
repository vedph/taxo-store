using Fusi.Tools.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;
using TaxoStore.Api.Controllers.Models;
using TaxoStore.Core;

namespace TaxoStore.Api.Controllers;

/// <summary>
/// Tree API controller.
/// </summary>
/// <param name="store"></param>
[Authorize]
[ApiController]
[Route("api/taxostore/trees")]
public sealed class TaxoTreeController(ITaxoStore store) : ControllerBase
{
    private readonly ITaxoStore _store = store;

    /// <summary>
    /// Retrieves the tree with the specified identifier.
    /// </summary>
    /// <param name="id">The unique identifier (key) of the tree to retrieve.</param>
    /// <returns>An <see cref="OkObjectResult"/> containing the tree if found;
    /// otherwise, a <see cref="NotFoundResult"/> if no tree with the specified
    /// identifier exists.</returns>
    [HttpGet("{id}", Name = "GetTree")]
    [Produces("application/json")]
    [ProducesResponseType(200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> GetTreeAsync([FromRoute] string id)
    {
        TaxoTree? tree = await _store.GetTreeAsync(id);
        if (tree is null) return NotFound();
        return Ok(tree);
    }

    /// <summary>
    /// Retrieves a paginated list of trees that match the specified filter
    /// criteria.
    /// </summary>
    /// <param name="model">The filter parameters to apply to the tree query.
    /// Must not be null.</param>
    /// <returns>An <see cref="IActionResult"/> containing a JSON-formatted
    /// paginated list of trees that satisfy the filter conditions. Returns an
    /// empty list if no trees match the criteria.</returns>
    [HttpGet]
    [Produces("application/json")]
    [ProducesResponseType(200)]
    public async Task<IActionResult> GetTreesAsync(
        [FromQuery] TaxoTreeFilterBindingModel model)
    {
        TaxoTreeFilter filter = model.ToFilter();
        DataPage<TaxoTree> result = await _store.GetTreesAsync(filter)
            ;
        return Ok(result);
    }

    /// <summary>
    /// Creates a new tree entry using the specified data and returns a response
    /// indicating the result.
    /// </summary>
    /// <param name="model">The data used to create the new tree. Must not be
    /// null.</param>
    /// <returns>A 201 Created response with a location header referencing the
    /// newly created tree resource.</returns>
    [HttpPost]
    [Produces("application/json")]
    [ProducesResponseType(201)]
    public async Task<IActionResult> AddTreeAsync(
        [FromBody] TaxoTreeBindingModel model)
    {
        TaxoTree tree = model.ToTree();
        string id = await _store.AddTreeAsync(tree);
        return CreatedAtRoute("GetTree", new { id }, null);
    }

    /// <summary>
    /// Deletes the tree with the specified identifier.
    /// </summary>
    /// <param name="id">The unique identifier (key) of the tree to delete.</param>
    /// <returns>An <see cref="IActionResult"/> that represents the result of
    /// the delete operation. Returns a 200 OK response with the identifier of
    /// the deleted tree, or null if not found.</returns>
    [HttpDelete("{id}")]
    [Produces("application/json")]
    [ProducesResponseType(200)]
    public async Task<IActionResult> DeleteTreeAsync([FromRoute] string id)
    {
        string? deletedId = await _store.DeleteTreeAsync(id);
        return Ok(deletedId);
    }
}
