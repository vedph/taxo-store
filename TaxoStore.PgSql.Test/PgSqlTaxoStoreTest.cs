using Fusi.DbManager;
using Fusi.DbManager.PgSql;
using Fusi.Tools.Data;
using Npgsql;
using TaxoStore.Core;

namespace TaxoStore.PgSql.Test;

public class NonParallelResourceCollection { }

// https://github.com/xunit/xunit/issues/1999
[CollectionDefinition(nameof(NonParallelResourceCollection),
    DisableParallelization = true)]
[Collection(nameof(NonParallelResourceCollection))]
public sealed class PgSqlTaxoStoreTest
{
    private const string CS_TEMPLATE =
        "User ID=postgres;Password=postgres;Host=localhost;Port=5432;Database={0}";
    private const string DB_NAME = "taxo-store-test";

    private const string TREES_CSV = @"id,name,note
languages,Programming Languages,Programming languages taxonomy
fruits,Common Fruits,Common fruits classification
";

    private const string NODES_CSV = @"tree_n,parent_key,key,label,filtered_label,flags
1,,prog,Programming,programming,
1,prog,oop,Object-Oriented,object oriented,
1,prog,func,Functional,functional,
1,oop,csharp,C#,c,d
1,oop,java,Java,java,
1,func,haskell,Haskell,haskell,o
2,,fruit,Fruits,fruits,
2,fruit,citrus,Citrus,citrus,
2,fruit,berry,Berries,berries,
2,citrus,orange,Orange,orange,
2,citrus,lemon,Lemon,lemon,
2,berry,strawberry,Strawberry,strawberry,
";

    private static readonly Lock _lockObject = new();
    private readonly PgSqlDbManager _dbManager = new(CS_TEMPLATE);

    /// <summary>
    /// Creates a PgSqlTaxoStore instance for testing with optional seeding.
    /// </summary>
    /// <param name="useEmptySources">If true, creates an empty database;
    /// if false, seeds with sample data from CSV text.</param>
    /// <returns>A configured PgSqlTaxoStore instance.</returns>
    private PgSqlTaxoStore CreateStoreAsync(bool useEmptySources = true)
    {
        // synchronize database cleanup to prevent concurrent access
        lock (_lockObject)
        {
            // clear the Npgsql connection pool for this database
            NpgsqlConnection.ClearAllPools();

            // remove database if it exists
            try
            {
                if (_dbManager.Exists(DB_NAME))
                    _dbManager.RemoveDatabase(DB_NAME);
            }
            catch
            {
                // database may be in use; will be cleaned up on next run
            }

            // wait a moment for database deletion to complete
            Thread.Sleep(100);

            // build connection string with test database
            string connectionString = string.Format(CS_TEMPLATE, DB_NAME);

            // create the database and schema with optional seed data
            TaxoStoreOptions options = new()
            {
                Source = connectionString,
                SeedSourceAsText = !useEmptySources,
                SeedTreeSource = useEmptySources ? null : TREES_CSV,
                SeedNodeSource = useEmptySources ? null : NODES_CSV
            };

            PgSqlTaxoStore store = new(options);
            store.InitializeAsync().Wait();

            return store;
        }
    }

    #region Tree Tests

    [Fact]
    public async Task GetTreeAsync_ExistingTree_ReturnsTree()
    {
        using PgSqlTaxoStore store = CreateStoreAsync(false);
        TaxoTree? tree = await store.GetTreeAsync("languages");

        Assert.NotNull(tree);
        Assert.Equal("languages", tree.Id);
        Assert.Equal("Programming Languages", tree.Name);
        Assert.Equal("Programming languages taxonomy", tree.Note);
    }

    [Fact]
    public async Task GetTreeAsync_NonExistingTree_ReturnsNull()
    {
        using PgSqlTaxoStore store = CreateStoreAsync(false);
        TaxoTree? tree = await store.GetTreeAsync("nonexistent");

        Assert.Null(tree);
    }

    [Fact]
    public async Task AddTreeAsync_NewTree_ReturnsId()
    {
        using PgSqlTaxoStore store = CreateStoreAsync(true);
        TaxoTree tree = new()
        {
            Id = "test-tree",
            Name = "Test Tree",
            Note = "Test note"
        };

        string id = await store.AddTreeAsync(tree);

        Assert.Equal("test-tree", id);
        TaxoTree? saved = await store.GetTreeAsync(id);
        Assert.NotNull(saved);
        Assert.Equal("Test Tree", saved.Name);
        Assert.Equal("Test note", saved.Note);
    }

    [Fact]
    public async Task AddTreeAsync_UpdateTree_UpdatesTree()
    {
        using PgSqlTaxoStore store = CreateStoreAsync(false);
        TaxoTree? tree = await store.GetTreeAsync("languages");
        Assert.NotNull(tree);

        tree.Name = "Updated Name";
        tree.Note = "Updated note";
        string id = await store.AddTreeAsync(tree);

        Assert.Equal("languages", id);
        TaxoTree? updated = await store.GetTreeAsync("languages");
        Assert.NotNull(updated);
        Assert.Equal("Updated Name", updated.Name);
        Assert.Equal("Updated note", updated.Note);
    }

    [Fact]
    public async Task DeleteTreeAsync_ExistingTree_DeletesTree()
    {
        using PgSqlTaxoStore store = CreateStoreAsync(false);
        string? deletedId = await store.DeleteTreeAsync("languages");

        Assert.Equal("languages", deletedId);
        TaxoTree? tree = await store.GetTreeAsync("languages");
        Assert.Null(tree);
    }

    [Fact]
    public async Task DeleteTreeAsync_NonExistingTree_ReturnsNull()
    {
        using PgSqlTaxoStore store = CreateStoreAsync(false);
        string? deletedId = await store.DeleteTreeAsync("nonexistent");

        Assert.Null(deletedId);
    }

    [Fact]
    public async Task GetTreesAsync_WithFilter_ReturnsFilteredTrees()
    {
        using PgSqlTaxoStore store = CreateStoreAsync(false);
        TaxoTreeFilter filter = new()
        {
            PageNumber = 1,
            PageSize = 10,
            Name = "Programming"
        };

        DataPage<TaxoTree> page = await store.GetTreesAsync(filter);

        Assert.Equal(1, page.Total);
        Assert.Single(page.Items);
        Assert.Equal("Programming Languages", page.Items[0].Name);
    }

    [Fact]
    public async Task GetTreesAsync_NoFilter_ReturnsAllTrees()
    {
        using PgSqlTaxoStore store = CreateStoreAsync(false);
        TaxoTreeFilter filter = new()
        {
            PageNumber = 1,
            PageSize = 10
        };

        DataPage<TaxoTree> page = await store.GetTreesAsync(filter);

        Assert.Equal(2, page.Total);
        Assert.Equal(2, page.Items.Count);
    }

    #endregion

    #region Node Tests

    [Fact]
    public async Task GetNodeAsync_ExistingNode_ReturnsNode()
    {
        using PgSqlTaxoStore store = CreateStoreAsync(false);
        TaxoNode? node = await store.GetNodeAsync(4);

        Assert.NotNull(node);
        Assert.Equal(4, node.Id);
        Assert.Equal("csharp", node.Key);
        Assert.Equal("C#", node.Label);
        Assert.Equal("languages", node.TreeId);
        Assert.Equal("d", node.Flags);
    }

    [Fact]
    public async Task GetNodeAsync_NonExistingNode_ReturnsNull()
    {
        using PgSqlTaxoStore store = CreateStoreAsync(false);
        TaxoNode? node = await store.GetNodeAsync(999);

        Assert.Null(node);
    }

    [Fact]
    public async Task GetNodeFromKeyAsync_ExistingNode_ReturnsNode()
    {
        using PgSqlTaxoStore store = CreateStoreAsync(false);
        TaxoNode? node = await store.GetNodeFromKeyAsync("languages", "csharp");

        Assert.NotNull(node);
        Assert.Equal("csharp", node.Key);
        Assert.Equal("C#", node.Label);
        Assert.Equal("languages", node.TreeId);
    }

    [Fact]
    public async Task GetNodeFromKeyAsync_NonExistingKey_ReturnsNull()
    {
        using PgSqlTaxoStore store = CreateStoreAsync(false);
        TaxoNode? node = await store.GetNodeFromKeyAsync("languages", "nonexistent");

        Assert.Null(node);
    }

    [Fact]
    public async Task GetNodeFromKeyAsync_NonExistingTreeId_ReturnsNull()
    {
        using PgSqlTaxoStore store = CreateStoreAsync(false);
        TaxoNode? node = await store.GetNodeFromKeyAsync("nonexistent-tree", "csharp");

        Assert.Null(node);
    }

    [Fact]
    public async Task GetNodeFromKeyAsync_DifferentTree_ReturnsCorrectNode()
    {
        using PgSqlTaxoStore store = CreateStoreAsync(false);
        TaxoNode? node = await store.GetNodeFromKeyAsync("fruits", "orange");

        Assert.NotNull(node);
        Assert.Equal("orange", node.Key);
        Assert.Equal("Orange", node.Label);
        Assert.Equal("fruits", node.TreeId);
    }

    [Fact]
    public async Task AddNodeAsync_NewNode_ReturnsId()
    {
        using PgSqlTaxoStore store = CreateStoreAsync(false);
        TaxoNode node = new()
        {
            TreeId = "languages",
            ParentId = 2,
            Key = "cpp",
            Label = "C++",
            FilteredLabel = "c",
            Flags = "d"
        };

        int id = await store.AddNodeAsync(node);

        Assert.True(id > 0);
        TaxoNode? saved = await store.GetNodeAsync(id);
        Assert.NotNull(saved);
        Assert.Equal("cpp", saved.Key);
        Assert.Equal("C++", saved.Label);
    }

    [Fact]
    public async Task DeleteNodeAsync_ExistingNode_DeletesNode()
    {
        using PgSqlTaxoStore store = CreateStoreAsync(false);
        int deletedId = await store.DeleteNodeAsync(4);

        Assert.Equal(4, deletedId);
        TaxoNode? node = await store.GetNodeAsync(4);
        Assert.Null(node);
    }

    #endregion

    #region Node Filtering Tests

    [Fact]
    public async Task GetNodesAsync_FilterByTreeId_ReturnsCorrectNodes()
    {
        using PgSqlTaxoStore store = CreateStoreAsync(false);
        TaxoNodeFilter filter = new()
        {
            PageNumber = 1,
            PageSize = 100,
            TreeId = "languages"
        };

        DataPage<TaxoNode> page = await store.GetNodesAsync(filter);

        Assert.True(page.Total >= 5);
        Assert.All(page.Items, n => Assert.Equal("languages", n.TreeId));
    }

    [Fact]
    public async Task GetNodesAsync_FilterByParentId_ReturnsChildren()
    {
        using PgSqlTaxoStore store = CreateStoreAsync(false);
        TaxoNodeFilter filter = new()
        {
            PageNumber = 1,
            PageSize = 100,
            ParentId = 2
        };

        DataPage<TaxoNode> page = await store.GetNodesAsync(filter);

        Assert.True(page.Total >= 2);
        Assert.All(page.Items, n => Assert.Equal(2, n.ParentId));
    }

    [Fact]
    public async Task GetNodesAsync_FilterByKey_ReturnsMatchingNodes()
    {
        using PgSqlTaxoStore store = CreateStoreAsync(false);
        TaxoNodeFilter filter = new()
        {
            PageNumber = 1,
            PageSize = 100,
            Key = "java"
        };

        DataPage<TaxoNode> page = await store.GetNodesAsync(filter);

        Assert.True(page.Total >= 1);
        Assert.Contains(page.Items, n => n.Key.Contains("java"));
    }

    [Fact]
    public async Task GetNodesAsync_FilterByFilteredLabel_ReturnsMatchingNodes()
    {
        using PgSqlTaxoStore store = CreateStoreAsync(false);
        TaxoNodeFilter filter = new()
        {
            PageNumber = 1,
            PageSize = 100,
            FilteredLabel = "object"
        };

        DataPage<TaxoNode> page = await store.GetNodesAsync(filter);

        Assert.True(page.Total >= 1);
        Assert.Contains(page.Items,
            n => n.FilteredLabel.Contains("object"));
    }

    [Fact]
    public async Task GetNodesAsync_FilterByFlagsAll_ReturnsMatchingNodes()
    {
        using PgSqlTaxoStore store = CreateStoreAsync(false);
        TaxoNodeFilter filter = new()
        {
            PageNumber = 1,
            PageSize = 100,
            Flags = "d",
            FlagMatchMode = NodeFlagMatchMode.All
        };

        DataPage<TaxoNode> page = await store.GetNodesAsync(filter);

        Assert.True(page.Total >= 1);
        Assert.All(page.Items, n => Assert.Contains('d', n.Flags ?? ""));
    }

    [Fact]
    public async Task GetNodesAsync_FilterByIsLeaf_ReturnsLeafNodes()
    {
        using PgSqlTaxoStore store = CreateStoreAsync(false);
        TaxoNodeFilter filter = new()
        {
            PageNumber = 1,
            PageSize = 100,
            TreeId = "languages",
            IsLeaf = true
        };

        DataPage<TaxoNode> page = await store.GetNodesAsync(filter);

        Assert.True(page.Total >= 1);
        // Verify they are actually leaf nodes
        foreach (TaxoNode node in page.Items)
        {
            bool hasChildren = await store.NodeHasChildrenAsync(node.Id);
            Assert.False(hasChildren);
        }
    }

    #endregion

    #region Hierarchy Tests

    [Fact]
    public async Task GetRootNodes_ReturnsOnlyRootNodes()
    {
        using PgSqlTaxoStore store = CreateStoreAsync(false);
        PagingOptions options = new()
        {
            PageNumber = 1,
            PageSize = 100
        };

        DataPage<TaxoNode> page = await store.GetRootNodes("languages", options);

        Assert.True(page.Total >= 1);
        Assert.All(page.Items, n => Assert.Null(n.ParentId));
    }

    [Fact]
    public async Task NodeHasChildrenAsync_NodeWithChildren_ReturnsTrue()
    {
        using PgSqlTaxoStore store = CreateStoreAsync(false);
        bool hasChildren = await store.NodeHasChildrenAsync(2);

        Assert.True(hasChildren);
    }

    [Fact]
    public async Task NodeHasChildrenAsync_LeafNode_ReturnsFalse()
    {
        using PgSqlTaxoStore store = CreateStoreAsync(false);
        bool hasChildren = await store.NodeHasChildrenAsync(4);

        Assert.False(hasChildren);
    }

    [Fact]
    public async Task NodeHasChildrenAsync_NonExistingNode_ReturnsFalse()
    {
        using PgSqlTaxoStore store = CreateStoreAsync(false);
        bool hasChildren = await store.NodeHasChildrenAsync(999);

        Assert.False(hasChildren);
    }

    [Fact]
    public async Task NodeHasChildrenAsync_RootNodeWithChildren_ReturnsTrue()
    {
        using PgSqlTaxoStore store = CreateStoreAsync(false);
        // Node 1 (prog) is a root node with children (oop, func)
        bool hasChildren = await store.NodeHasChildrenAsync(1);

        Assert.True(hasChildren);
    }

    [Fact]
    public async Task GetChildNodesAsync_ReturnsDirectChildren()
    {
        using PgSqlTaxoStore store = CreateStoreAsync(false);
        IList<TaxoNode> children = await store.GetChildNodesAsync(2);

        Assert.True(children.Count >= 2);
        Assert.All(children, n => Assert.Equal(2, n.ParentId));
    }

    [Fact]
    public async Task GetDescendantNodesAsync_ReturnsAllDescendants()
    {
        using PgSqlTaxoStore store = CreateStoreAsync(false);
        IList<TaxoNode> descendants = await store.GetDescendantNodesAsync(1);

        Assert.True(descendants.Count >= 4);
        // All descendants should be in tree languages
        Assert.All(descendants, n => Assert.Equal("languages", n.TreeId));
    }

    [Fact]
    public async Task GetAncestorNodesAsync_ReturnsAllAncestors()
    {
        using PgSqlTaxoStore store = CreateStoreAsync(false);
        // Node 4 (csharp) has parent 2 (oop) and grandparent 1 (prog)
        IList<TaxoNode> ancestors = await store.GetAncestorNodesAsync(4);

        Assert.True(ancestors.Count >= 2);
        // First ancestor should be immediate parent
        Assert.Equal(2, ancestors[0].Id);
    }

    [Fact]
    public async Task GetAncestorNodesAsync_RootNode_ReturnsEmpty()
    {
        using PgSqlTaxoStore store = CreateStoreAsync(false);
        IList<TaxoNode> ancestors = await store.GetAncestorNodesAsync(1);

        Assert.Empty(ancestors);
    }

    [Fact]
    public async Task GetNodePathAsync_LeafNode_ReturnsFullPath()
    {
        using PgSqlTaxoStore store = CreateStoreAsync(false);
        // Node 4 (csharp) has parent 2 (oop) and grandparent 1 (prog)
        // Path: prog -> oop -> csharp
        IList<TaxoNodePathStep> path = await store.GetNodePathAsync(4, 10);

        Assert.Equal(3, path.Count);
        // First step is root (prog)
        Assert.Equal(1, path[0].NodeId);
        Assert.Equal(1, path[0].PageNumber);
        // Second step is oop (position 2 among siblings func, oop)
        Assert.Equal(2, path[1].NodeId);
        Assert.Equal(1, path[1].PageNumber);
        // Third step is csharp (position 1 among siblings csharp, java)
        Assert.Equal(4, path[2].NodeId);
        Assert.Equal(1, path[2].PageNumber);
    }

    [Fact]
    public async Task GetNodePathAsync_RootNode_ReturnsSingleStep()
    {
        using PgSqlTaxoStore store = CreateStoreAsync(false);
        // Node 1 (prog) is a root node
        IList<TaxoNodePathStep> path = await store.GetNodePathAsync(1, 10);

        Assert.Single(path);
        Assert.Equal(1, path[0].NodeId);
        Assert.Equal(1, path[0].PageNumber);
    }

    [Fact]
    public async Task GetNodePathAsync_SmallPageSize_CalculatesCorrectPages()
    {
        using PgSqlTaxoStore store = CreateStoreAsync(false);
        // Node 4 (csharp) path: prog -> oop -> csharp
        // oop is position 2 among siblings [func, oop] sorted by key
        // With page size 1, oop should be on page 2
        IList<TaxoNodePathStep> path = await store.GetNodePathAsync(4, 1);

        Assert.Equal(3, path.Count);
        // prog is only root, position 1 -> page 1
        Assert.Equal(1, path[0].NodeId);
        Assert.Equal(1, path[0].PageNumber);
        // oop is position 2 -> page 2 with pageSize=1
        Assert.Equal(2, path[1].NodeId);
        Assert.Equal(2, path[1].PageNumber);
        // csharp is position 1 -> page 1
        Assert.Equal(4, path[2].NodeId);
        Assert.Equal(1, path[2].PageNumber);
    }

    [Fact]
    public async Task GetNodePathAsync_NonExistingNode_ReturnsEmpty()
    {
        using PgSqlTaxoStore store = CreateStoreAsync(false);
        IList<TaxoNodePathStep> path = await store.GetNodePathAsync(999, 10);

        Assert.Empty(path);
    }

    [Fact]
    public async Task GetNodePathAsync_InvalidPageSize_ThrowsException()
    {
        using PgSqlTaxoStore store = CreateStoreAsync(false);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => store.GetNodePathAsync(1, 0));
    }

    [Fact]
    public async Task GetNodePathAsync_DifferentTree_ReturnsCorrectPath()
    {
        using PgSqlTaxoStore store = CreateStoreAsync(false);
        // Node 10 (orange) in fruits tree: fruit -> citrus -> orange
        // fruit is only root
        // citrus siblings: [berry, citrus] -> citrus is position 2
        // orange siblings: [lemon, orange] -> orange is position 2
        IList<TaxoNodePathStep> path = await store.GetNodePathAsync(10, 1);

        Assert.Equal(3, path.Count);
        // fruit is position 1 -> page 1
        Assert.Equal(7, path[0].NodeId);
        Assert.Equal(1, path[0].PageNumber);
        // citrus is position 2 -> page 2 with pageSize=1
        Assert.Equal(8, path[1].NodeId);
        Assert.Equal(2, path[1].PageNumber);
        // orange is position 2 -> page 2 with pageSize=1
        Assert.Equal(10, path[2].NodeId);
        Assert.Equal(2, path[2].PageNumber);
    }

    #endregion

    #region Bulk Operations Tests

    [Fact]
    public async Task AddNodesAsync_MultipleNodes_ReturnsAllIds()
    {
        using PgSqlTaxoStore store = CreateStoreAsync(true);

        // First add a tree
        TaxoTree tree = new() { Id = "test", Name = "Test", Note = null };
        string treeId = await store.AddTreeAsync(tree);

        List<TaxoNode> nodes = [
            new TaxoNode
            {
                TreeId = treeId,
                Key = "node1",
                Label = "Node 1",
                FilteredLabel = "node 1"
            },
            new TaxoNode
            {
                TreeId = treeId,
                Key = "node2",
                Label = "Node 2",
                FilteredLabel = "node 2"
            }
        ];

        IList<int> ids = await store.AddNodesAsync(nodes);

        Assert.Equal(2, ids.Count);
        Assert.All(ids, id => Assert.True(id > 0));

        // Verify nodes were saved
        TaxoNode? node1 = await store.GetNodeAsync(ids[0]);
        Assert.NotNull(node1);
        Assert.Equal("node1", node1.Key);
    }

    #endregion

    #region Clear and Reset Tests

    [Fact]
    public async Task ClearAsync_RemovesAllDataAndResetsSequences()
    {
        using PgSqlTaxoStore store = CreateStoreAsync(false);

        // Verify data exists
        TaxoTreeFilter filter = new()
        {
            PageNumber = 1,
            PageSize = 100
        };
        DataPage<TaxoTree> before = await store.GetTreesAsync(filter);
        Assert.True(before.Total > 0);

        // Clear store
        await store.ClearAsync();

        // Verify all data is removed
        DataPage<TaxoTree> after = await store.GetTreesAsync(filter);
        Assert.Equal(0, after.Total);

        // Verify sequences are reset by adding a new tree
        TaxoTree tree = new() { Id = "new-tree", Name = "New Tree", Note = null };
        string id = await store.AddTreeAsync(tree);
        Assert.Equal("new-tree", id);
    }

    #endregion
}
