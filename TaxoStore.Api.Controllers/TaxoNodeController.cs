using Fusi.Tools.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TaxoStore.Api.Controllers.Models;
using TaxoStore.Core;

namespace TaxoStore.Api.Controllers;

/// <summary>
/// Provides API endpoints for managing and querying nodes within a tree
/// structure.
/// </summary>
/// <remarks>All endpoints require authentication. This controller supports
/// operations such as retrieving, adding, deleting, and querying nodes,
/// including hierarchical queries for children, ancestors, and descendants.
/// Responses are returned in JSON format.</remarks>
/// <param name="store">The tree store service used to access and manipulate
/// node data.</param>
[Authorize]
[ApiController]
[Route("api/taxostore/nodes")]
public sealed class TaxoNodeController(ITaxoStore store) : ControllerBase
{
    private readonly ITaxoStore _store = store;

    #region Position Helpers
    /// <summary>
    /// Computes the Y (depth) value for a node.
    /// Y is 1-based: root nodes have Y=1, their children Y=2, etc.
    /// </summary>
    private async Task<int> ComputeYAsync(int nodeId)
    {
        IList<TaxoNode> ancestors = await _store.GetAncestorNodesAsync(nodeId);
        return ancestors.Count + 1;
    }

    /// <summary>
    /// Computes the X (sibling position) value for a node.
    /// X is 1-based: first sibling (by key order) has X=1, etc.
    /// </summary>
    private async Task<int> ComputeXAsync(TaxoNode node)
    {
        IList<TaxoNode> siblings;
        if (node.ParentId == null)
        {
            // Root node - get all root nodes for this tree
            DataPage<TaxoNode> roots = await _store.GetRootNodes(node.TreeId,
                new PagingOptions { PageNumber = 1, PageSize = 0 });
            siblings = [.. roots.Items];
        }
        else
        {
            siblings = await _store.GetChildNodesAsync(node.ParentId.Value);
        }

        // Siblings are already sorted by key from the store
        int position = 1;
        foreach (TaxoNode sibling in siblings)
        {
            if (sibling.Id == node.Id) return position;
            position++;
        }
        return 1; // Fallback (should not happen)
    }

    /// <summary>
    /// Converts a Node to a PositionedNodeModel with computed X/Y/HasChildren.
    /// </summary>
    private async Task<PositionedTaxoNodeModel> ToPositionedModelAsync(TaxoNode node)
    {
        return new PositionedTaxoNodeModel
        {
            Id = node.Id,
            ParentId = node.ParentId,
            TreeId = node.TreeId,
            Key = node.Key,
            Label = node.Label,
            FilteredLabel = node.FilteredLabel,
            Flags = node.Flags,
            Note = node.Note,
            Y = await ComputeYAsync(node.Id),
            X = await ComputeXAsync(node),
            HasChildren = await _store.NodeHasChildrenAsync(node.Id)
        };
    }

    /// <summary>
    /// Converts a list of nodes to positioned models.
    /// Uses caching to optimize sibling lookups.
    /// </summary>
    private async Task<IList<PositionedTaxoNodeModel>> ToPositionedModelsAsync(
        IList<TaxoNode> nodes)
    {
        if (nodes.Count == 0) return [];

        List<PositionedTaxoNodeModel> result = new(nodes.Count);

        // Cache sibling lists to avoid repeated queries for nodes sharing the same parent
        Dictionary<(string TreeId, int? ParentId), IList<TaxoNode>> siblingCache = [];

        foreach (TaxoNode node in nodes)
        {
            // Get Y (depth)
            int y = await ComputeYAsync(node.Id);

            // Get X (sibling position) with caching
            var cacheKey = (node.TreeId, node.ParentId);
            if (!siblingCache.TryGetValue(cacheKey, out IList<TaxoNode>? siblings))
            {
                if (node.ParentId == null)
                {
                    DataPage<TaxoNode> roots = await _store.GetRootNodes(node.TreeId,
                        new PagingOptions { PageNumber = 1, PageSize = 0 });
                    siblings = [.. roots.Items];
                }
                else
                {
                    siblings = await _store.GetChildNodesAsync(node.ParentId.Value);
                }
                siblingCache[cacheKey] = siblings;
            }

            int x = 1;
            foreach (TaxoNode sibling in siblings)
            {
                if (sibling.Id == node.Id) break;
                x++;
            }

            result.Add(new PositionedTaxoNodeModel
            {
                Id = node.Id,
                ParentId = node.ParentId,
                TreeId = node.TreeId,
                Key = node.Key,
                Label = node.Label,
                FilteredLabel = node.FilteredLabel,
                Flags = node.Flags,
                Note = node.Note,
                Y = y,
                X = x,
                HasChildren = await _store.NodeHasChildrenAsync(node.Id)
            });
        }

        return result;
    }
    #endregion

    /// <summary>
    /// Retrieves the node with the specified identifier.
    /// </summary>
    /// <param name="id">The unique identifier of the node to retrieve.</param>
    /// <returns>An <see cref="OkObjectResult"/> containing the node with
    /// computed position (X/Y) if found; otherwise, a <see cref="NotFoundResult"/>
    /// if no node with the specified identifier exists.</returns>
    [HttpGet("{id:int}", Name = "GetNode")]
    [Produces("application/json")]
    [ProducesResponseType(200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> GetNodeAsync([FromRoute] int id)
    {
        TaxoNode? node = await _store.GetNodeAsync(id);
        if (node is null) return NotFound();

        PositionedTaxoNodeModel model = await ToPositionedModelAsync(node);
        return Ok(model);
    }

    /// <summary>
    /// Retrieves the node with the specified tree identifier and key.
    /// </summary>
    /// <param name="treeId">The unique identifier of the tree containing the node.</param>
    /// <param name="key">The key of the node to retrieve within the specified tree.</param>
    /// <returns>An <see cref="OkObjectResult"/> containing the node with
    /// computed position (X/Y) if found; otherwise, a <see cref="NotFoundResult"/>
    /// if no node with the specified tree identifier and key exists.</returns>
    [HttpGet("tree/{treeId}/key/{key}")]
    [Produces("application/json")]
    [ProducesResponseType(200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> GetNodeFromKeyAsync(
        [FromRoute] string treeId,
        [FromRoute] string key)
    {
        TaxoNode? node = await _store.GetNodeFromKeyAsync(treeId, key);
        if (node is null) return NotFound();

        PositionedTaxoNodeModel model = await ToPositionedModelAsync(node);
        return Ok(model);
    }

    /// <summary>
    /// Retrieves a paginated list of nodes that match the specified filter
    /// criteria.
    /// </summary>
    /// <param name="model">The filter parameters to apply to the node query.
    /// The filter is provided as query string values. Set IncludePosition=true
    /// to include X/Y position data in the response.</param>
    /// <returns>An <see cref="IActionResult"/> containing a JSON-formatted
    /// paginated list of nodes that satisfy the filter conditions.</returns>
    [HttpGet]
    [Produces("application/json")]
    [ProducesResponseType(200)]
    public async Task<IActionResult> GetNodesAsync(
        [FromQuery] TaxoNodeFilterBindingModel model)
    {
        TaxoNodeFilter filter = model.ToNodeFilter();
        DataPage<TaxoNode> result = await _store.GetNodesAsync(filter);

        if (model.IncludePosition)
        {
            IList<PositionedTaxoNodeModel> positioned =
                await ToPositionedModelsAsync([.. result.Items]);
            return Ok(new DataPage<PositionedTaxoNodeModel>(
                result.PageNumber,
                result.PageSize,
                result.Total,
                positioned));
        }

        return Ok(result);
    }

    /// <summary>
    /// Retrieves a paged list of root nodes for the specified tree.
    /// </summary>
    /// <param name="treeId">The unique identifier (key) of the tree for which to
    /// retrieve root nodes.</param>
    /// <param name="model">An object containing filter and paging options to
    /// apply to the root node query. May include page number, page size, and
    /// additional filter criteria. Set IncludePosition=true to include X/Y
    /// position data.</param>
    /// <returns>An <see cref="IActionResult"/> containing a paged collection
    /// of root nodes in JSON format with HTTP status code 200.</returns>
    [HttpGet("roots/{treeId}")]
    [Produces("application/json")]
    [ProducesResponseType(200)]
    public async Task<IActionResult> GetRootNodesAsync(
        [FromRoute] string treeId,
        [FromQuery] TaxoNodeFilterBindingModel model)
    {
        PagingOptions options = new()
        {
            PageNumber = model.PageNumber,
            PageSize = model.PageSize
        };
        DataPage<TaxoNode> result = await _store.GetRootNodes(treeId, options);

        if (model.IncludePosition)
        {
            IList<PositionedTaxoNodeModel> positioned =
                await ToPositionedModelsAsync([.. result.Items]);
            return Ok(new DataPage<PositionedTaxoNodeModel>(
                result.PageNumber,
                result.PageSize,
                result.Total,
                positioned));
        }

        return Ok(result);
    }

    /// <summary>
    /// Creates a new node in the data store using the specified node binding
    /// model.
    /// </summary>
    /// <remarks>The response does not include a response body. The location
    /// header can be used to retrieve the created node resource.</remarks>
    /// <param name="model">The node data to create, provided in the request
    /// body. Must not be null.</param>
    /// <returns>A 201 Created response with a location header referencing
    /// the newly created node.</returns>
    [HttpPost]
    [Produces("application/json")]
    [ProducesResponseType(201)]
    public async Task<IActionResult> AddNodeAsync(
        [FromBody] TaxoNodeBindingModel model)
    {
        TaxoNode node = model.ToNode();

        int id = await _store.AddNodeAsync(node);
        return CreatedAtRoute("GetNode", new { id }, null);
    }

    /// <summary>
    /// Adds a batch of nodes to the data store and returns their assigned
    /// identifiers.
    /// </summary>
    /// <remarks>This operation processes all provided nodes in a single batch.
    /// The response will include the identifiers assigned to each node
    /// in the order they were processed.</remarks>
    /// <param name="models">A collection of node binding models containing
    /// the data for each node to add. Cannot be null.</param>
    /// <returns>An <see cref="IActionResult"/> containing a JSON array of
    /// integers representing the identifiers of the newly added nodes.</returns>
    [HttpPost("batch")]
    [Produces("application/json")]
    [ProducesResponseType(200)]
    public async Task<IActionResult> AddNodesAsync(
        [FromBody] IList<TaxoNodeBindingModel> models)
    {
        IEnumerable<TaxoNode> nodes = models.Select(m => m.ToNode());

        IList<int> ids = await _store.AddNodesAsync(nodes);
        return Ok(ids);
    }

    /// <summary>
    /// Deletes the node with the specified identifier.
    /// </summary>
    /// <param name="id">The unique identifier of the node to delete.</param>
    /// <returns>An <see cref="IActionResult"/> that contains the identifier
    /// of the deleted node if the operation is successful.</returns>
    [HttpDelete("{id:int}")]
    [Produces("application/json")]
    [ProducesResponseType(200)]
    public async Task<IActionResult> DeleteNodeAsync([FromRoute] int id)
    {
        int deletedId = await _store.DeleteNodeAsync(id);
        return Ok(deletedId);
    }

    /// <summary>
    /// Determines whether the node with the specified identifier has any child
    /// nodes.
    /// </summary>
    /// <remarks>The response is returned with HTTP status code 200 (OK) and
    /// a JSON-encoded Boolean value. If the specified node does not exist,
    /// the result will be <see langword="false"/>.</remarks>
    /// <param name="id">The unique identifier of the node to check for child
    /// nodes.</param>
    /// <returns>An <see cref="IActionResult"/> that contains a Boolean value
    /// in the response body: <see langword="true"/> if the node has one or
    /// more children; otherwise, <see langword="false"/>.</returns>
    [HttpGet("{id:int}/haschildren")]
    [Produces("application/json")]
    [ProducesResponseType(200)]
    public async Task<IActionResult> NodeHasChildrenAsync([FromRoute] int id)
    {
        bool has = await _store.NodeHasChildrenAsync(id);
        return Ok(has);
    }

    /// <summary>
    /// Retrieves the collection of child nodes for the specified parent node.
    /// </summary>
    /// <param name="id">The unique identifier of the parent node whose child
    /// nodes are to be retrieved.</param>
    /// <param name="includePosition">If true, include X (sibling position) and
    /// Y (depth) in the response. Default is false for better performance.</param>
    /// <returns>An <see cref="IActionResult"/> containing a JSON array of child
    /// nodes with HTTP status code 200 if successful.
    /// The array is empty if the parent node has no children.</returns>
    [HttpGet("{id:int}/children")]
    [Produces("application/json")]
    [ProducesResponseType(200)]
    public async Task<IActionResult> GetChildNodesAsync(
        [FromRoute] int id,
        [FromQuery] bool includePosition = false)
    {
        IList<TaxoNode> nodes = await _store.GetChildNodesAsync(id);

        if (includePosition)
        {
            IList<PositionedTaxoNodeModel> models = await ToPositionedModelsAsync(nodes);
            return Ok(models);
        }

        return Ok(nodes);
    }

    /// <summary>
    /// Retrieves all descendant nodes of the specified node.
    /// </summary>
    /// <remarks>Use this method to obtain all nodes that are descendants of
    /// the specified node in the hierarchy. The response will be an empty
    /// array if the node has no descendants.</remarks>
    /// <param name="id">The unique identifier of the node for which to retrieve
    /// descendant nodes.</param>
    /// <param name="includePosition">If true, include X (sibling position) and
    /// Y (depth) in the response. Default is false for better performance.</param>
    /// <returns>An <see cref="IActionResult"/> containing a JSON array of
    /// descendant nodes with HTTP status code 200.</returns>
    [HttpGet("{id:int}/descendants")]
    [Produces("application/json")]
    [ProducesResponseType(200)]
    public async Task<IActionResult> GetDescendantNodesAsync(
        [FromRoute] int id,
        [FromQuery] bool includePosition = false)
    {
        IList<TaxoNode> nodes = await _store.GetDescendantNodesAsync(id);

        if (includePosition)
        {
            IList<PositionedTaxoNodeModel> models = await ToPositionedModelsAsync(nodes);
            return Ok(models);
        }

        return Ok(nodes);
    }

    /// <summary>
    /// Retrieves the list of ancestor nodes for the specified node identifier.
    /// </summary>
    /// <param name="id">The unique identifier of the node for which to retrieve
    /// ancestor nodes.</param>
    /// <param name="includePosition">If true, include X (sibling position) and
    /// Y (depth) in the response. Default is false for better performance.</param>
    /// <returns>An <see cref="IActionResult"/> containing a JSON array of
    /// ancestor nodes. Returns an empty array if the node has no ancestors.</returns>
    [HttpGet("{id:int}/ancestors")]
    [Produces("application/json")]
    [ProducesResponseType(200)]
    public async Task<IActionResult> GetAncestorNodesAsync(
        [FromRoute] int id,
        [FromQuery] bool includePosition = false)
    {
        IList<TaxoNode> nodes = await _store.GetAncestorNodesAsync(id);

        if (includePosition)
        {
            IList<PositionedTaxoNodeModel> models = await ToPositionedModelsAsync(nodes);
            return Ok(models);
        }

        return Ok(nodes);
    }

    /// <summary>
    /// Retrieves the path from the root node to the specified target node,
    /// including the page number for each step.
    /// </summary>
    /// <remarks>
    /// <para>This endpoint supports tree visualization by returning the exact
    /// page numbers needed to expand and display the path to a target node.
    /// Each step indicates which page of siblings contains that node when
    /// children are ordered alphabetically by key.</para>
    /// <para>This is useful after an edit operation when the client needs to
    /// reload data while keeping focus on the affected node. The returned
    /// path allows the client to expand each level of the tree and navigate
    /// to the correct page at each level.</para>
    /// </remarks>
    /// <param name="id">The unique identifier of the target node.</param>
    /// <param name="pageSize">The page size used to calculate page numbers.
    /// Must be greater than 0. Default is 20.</param>
    /// <returns>An <see cref="IActionResult"/> containing a JSON array of
    /// <see cref="TaxoNodePathStep"/> objects ordered from root to target.
    /// Returns an empty array if the node does not exist.</returns>
    [HttpGet("{id:int}/path")]
    [Produces("application/json")]
    [ProducesResponseType(200)]
    [ProducesResponseType(400)]
    public async Task<IActionResult> GetNodePathAsync(
        [FromRoute] int id,
        [FromQuery] int pageSize = 20)
    {
        if (pageSize <= 0)
            return BadRequest("Page size must be greater than 0");

        IList<TaxoNodePathStep> path = await _store.GetNodePathAsync(id, pageSize);
        return Ok(path);
    }

    /// <summary>
    /// Deletes all items from the store asynchronously.
    /// </summary>
    /// <remarks>Use this method to remove all data from the underlying store.
    /// This operation is irreversible and will result in the loss of all stored
    /// items.</remarks>
    /// <returns>An <see cref="IActionResult"/> that represents the result of
    /// the operation. Returns an HTTP 200 response if the store was cleared
    /// successfully.</returns>
    [HttpDelete]
    [Produces("application/json")]
    [ProducesResponseType(200)]
    public async Task<IActionResult> ClearAsync()
    {
        await _store.ClearAsync();
        return Ok();
    }
}
