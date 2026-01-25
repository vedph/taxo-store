using Fusi.Tools.Data;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace TaxoStore.Core;

/// <summary>
/// A taxonomies store interface.
/// </summary>
public interface ITaxoStore
{
    /// <summary>
    /// Asynchronously performs any necessary initialization for the component.
    /// </summary>
    /// <returns>A task that represents the asynchronous initialization operation.
    /// </returns>
    public Task InitializeAsync();

    /// <summary>
    /// Gets the tree with the specified ID.
    /// </summary>
    /// <param name="id">The tree's ID (key).</param>
    /// <returns>Tree or null if not found.</returns>
    public Task<TaxoTree?> GetTreeAsync(string id);

    /// <summary>
    /// Adds or updates the specified tree.
    /// </summary>
    /// <param name="tree">The tree to add or update. If it is a new tree,
    /// its ID should be empty or the desired key; else it is the ID (key) of
    /// the tree to update, if it exists. If a tree with that ID no longer
    /// exists, the tree will be created with that ID.</param>
    /// <returns>The ID (key) of the tree which was added.</returns>
    public Task<string> AddTreeAsync(TaxoTree tree);

    /// <summary>
    /// Deletes the tree with the specified ID.
    /// </summary>
    /// <param name="id">The ID (key) of the tree to delete.</param>
    /// <returns>The ID (key) of the tree which was deleted, or null if it was
    /// not found.</returns>
    public Task<string?> DeleteTreeAsync(string id);

    /// <summary>
    /// Retrieves a paged list of trees that match the specified filter criteria.
    /// </summary>
    /// <param name="filter">An object containing the criteria used to filter
    /// the trees.</param>
    /// <returns>A task that represents the asynchronous operation. The task
    /// result contains a DataPage of Tree objects that satisfy the filter.
    /// If no trees match, the DataPage will be empty.</returns>
    public Task<DataPage<TaxoTree>> GetTreesAsync(TaxoTreeFilter filter);

    /// <summary>
    /// Retrieves the root node(s) of the specified tree, i.e. all the nodes
    /// belonging to that tree that have no parent.
    /// </summary>
    /// <param name="treeId">The ID (key) of the tree for which to retrieve the
    /// root node(s).</param>
    /// <param name="options">Paging options. When <see cref="PagingOptions.PageSize"/>
    /// is 0, paging is disabled and all root nodes are returned at once. This is
    /// useful when consumer code needs to display all root nodes as filters,
    /// typically in deeply nested hierarchies with a small number of roots.</param>
    /// <returns>A task that represents the asynchronous operation. The task
    /// result contains the requested page of <see cref="TaxoNode"/>'s. When page
    /// size is 0, the result contains all root nodes.</returns>
    public Task<DataPage<TaxoNode>> GetRootNodes(string treeId, PagingOptions options);

    /// <summary>
    /// Retrieves the node with the specified identifier.
    /// </summary>
    /// <param name="id">The ID of the node to retrieve. Must be greater than
    /// zero.</param>
    /// <returns>A task that represents the asynchronous operation. The task
    /// result contains the node with the specified identifier, or <c>null</c>
    /// if no such node exists.</returns>
    public Task<TaxoNode?> GetNodeAsync(int id);

    /// <summary>
    /// Retrieves the node associated with the specified key from the given tree.
    /// </summary>
    /// <param name="treeId">The ID (key) of the tree to search.</param>
    /// <param name="key">The key of the node to retrieve.</param>
    /// <returns>A task that represents the asynchronous operation. The task
    /// result contains the node associated with the specified key, or null
    /// if no such node exists.</returns>
    public Task<TaxoNode?> GetNodeFromKeyAsync(string treeId, string key);

    /// <summary>
    /// Retrieves a paged list of nodes that match the specified filter criteria.
    /// </summary>
    /// <param name="filter">The filter criteria used to select nodes.</param>
    /// <returns>A task that represents the asynchronous operation. The task
    /// result contains a DataPage of Node objects that match the filter.
    /// If no nodes match, the collection will be empty.</returns>
    public Task<DataPage<TaxoNode>> GetNodesAsync(TaxoNodeFilter filter);

    /// <summary>
    /// Adds a new node to the collection.
    /// </summary>
    /// <param name="node">The node to add. Cannot be null.</param>
    /// <returns>A task that represents the asynchronous operation. The task
    /// result contains the unique identifier assigned to the newly added node.
    /// </returns>
    public Task<int> AddNodeAsync(TaxoNode node);

    /// <summary>
    /// Deletes the node with the specified identifier.
    /// </summary>
    /// <param name="id">The unique identifier of the node to delete.
    /// Must be greater than zero.</param>
    /// <returns>A task that represents the asynchronous operation. The task
    /// result contains the ID of the deleted node, or 0 if the node was not
    /// found.</returns>
    public Task<int> DeleteNodeAsync(int id);

    /// <summary>
    /// Adds the specified collection of nodes to the data store and returns
    /// the identifiers of the added nodes, one per node, in the order they
    /// were received.
    /// </summary>
    /// <param name="nodes">The collection of <see cref="TaxoNode"/> objects to add.
    /// Each node must contain valid data required for insertion. If it has ID
    /// equal to 0, it will be added as a new node. Else, the existing node
    /// with that ID will be updated, or added if not found.</param>
    /// <returns>A task that represents the asynchronous operation. The task
    /// result contains a list of integers representing the identifiers assigned
    /// to the nodes (whether they were created or just updated), in the order
    /// they were processed.</returns>
    public Task<IList<int>> AddNodesAsync(IEnumerable<TaxoNode> nodes);

    /// <summary>
    /// Determines whether the node with the specified identifier has any child nodes.
    /// </summary>
    /// <param name="id">The ID of the node to check for child nodes.</param>
    /// <returns>A task that represents the asynchronous operation. The task
    /// result is true if the node has one or more child nodes; otherwise,
    /// false.</returns>
    public Task<bool> NodeHasChildrenAsync(int id);

    /// <summary>
    /// Retrieves the collection of child nodes for the specified parent node.
    /// </summary>
    /// <param name="parentId">The identifier of the parent node whose child
    /// nodes are to be retrieved. Must be a valid node identifier.</param>
    /// <returns>A task that represents the asynchronous operation. The task
    /// result contains a list of child nodes for the specified parent, sorted
    /// by their key.
    /// The list is empty if the parent node has no children.</returns>
    public Task<IList<TaxoNode>> GetChildNodesAsync(int parentId);

    /// <summary>
    /// Retrieves all descendant nodes of the specified parent node.
    /// </summary>
    /// <param name="parentId">The identifier of the parent node whose
    /// descendant nodes are to be retrieved.</param>
    /// <returns>A task that represents the asynchronous operation. The task
    /// result contains a list of descendant nodes, in the nodes traversal order,
    /// sorting siblings by their key. The list is empty if the parent node
    /// has no descendants.</returns>
    public Task<IList<TaxoNode>> GetDescendantNodesAsync(int parentId);

    /// <summary>
    /// Retrieves all ancestor nodes of the specified node, ordered from the
    /// immediate parent up to the root.
    /// </summary>
    /// <param name="nodeId">The identifier of the node for which to retrieve
    /// ancestor nodes. Must correspond to an existing node.</param>
    /// <returns>A task that represents the asynchronous operation. The task
    /// result contains a list of ancestor nodes, ordered from the immediate
    /// parent to the root. The list is empty if the node has no ancestors.
    /// </returns>
    public Task<IList<TaxoNode>> GetAncestorNodesAsync(int nodeId);

    /// <summary>
    /// Retrieves the path from the root node to the specified target node,
    /// including the page number for each step based on the specified page size.
    /// </summary>
    /// <remarks>
    /// <para>This method is designed to support tree visualization by providing
    /// the exact page numbers needed to display the path to a target node.
    /// Each step in the returned path indicates which page of siblings contains
    /// that node when children are ordered alphabetically by key.</para>
    /// <para>The method assumes no filtering is applied - all siblings are
    /// considered when calculating page numbers.</para>
    /// </remarks>
    /// <param name="nodeId">The identifier of the target node.</param>
    /// <param name="pageSize">The page size used to calculate page numbers.
    /// Must be greater than 0.</param>
    /// <returns>A task that represents the asynchronous operation. The task
    /// result contains a list of <see cref="TaxoNodePathStep"/> records ordered
    /// from the root node to the target node. Each step contains the node ID
    /// and its 1-based page number among its siblings. Returns an empty list
    /// if the node does not exist.</returns>
    public Task<IList<TaxoNodePathStep>> GetNodePathAsync(int nodeId, int pageSize);

    /// <summary>
    /// Asynchronously removes all data from the store.
    /// </summary>
    /// <returns>A task that represents the asynchronous clear operation.
    /// </returns>
    public Task ClearAsync();
}
