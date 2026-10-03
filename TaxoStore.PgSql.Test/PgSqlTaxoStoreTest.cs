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

    #region Node Query Tests
    // seed IDs: languages: prog=1, oop=2, func=3, csharp=4 (d), java=5,
    // haskell=6 (o); fruits: fruit=7, citrus=8, berry=9, orange=10,
    // lemon=11, strawberry=12

    private static TaxoNodeFilter AllNodes(Action<TaxoNodeFilter>? set = null)
    {
        TaxoNodeFilter filter = new() { PageNumber = 1, PageSize = 100 };
        set?.Invoke(filter);
        return filter;
    }

    private static string[] Keys(DataPage<TaxoNode> page) =>
        [.. page.Items.Select(n => n.Key)];

    [Fact]
    public async Task GetNodesAsync_AncestorKey_ReturnsDescendantsOnly()
    {
        using PgSqlTaxoStore store = CreateStoreAsync(false);

        DataPage<TaxoNode> page = await store.GetNodesAsync(
            AllNodes(f => f.AncestorKey = "oop"));

        Assert.Equal(2, page.Total);
        Assert.Equal(["csharp", "java"], Keys(page));
    }

    [Fact]
    public async Task GetNodesAsync_AncestorKeyRoot_ReturnsAllDescendants()
    {
        using PgSqlTaxoStore store = CreateStoreAsync(false);

        DataPage<TaxoNode> page = await store.GetNodesAsync(
            AllNodes(f => { f.AncestorKey = "prog"; f.TreeId = "languages"; }));

        Assert.Equal(5, page.Total);
        Assert.DoesNotContain("prog", Keys(page));
    }

    [Fact]
    public async Task GetNodesAsync_AncestorKeyIsExact_ReturnsNoneForPartialKey()
    {
        using PgSqlTaxoStore store = CreateStoreAsync(false);

        DataPage<TaxoNode> page = await store.GetNodesAsync(
            AllNodes(f => f.AncestorKey = "oo"));

        Assert.Equal(0, page.Total);
        Assert.Empty(page.Items);
    }

    [Fact]
    public async Task GetNodesAsync_AncestorKeyOtherTree_ReturnsNone()
    {
        using PgSqlTaxoStore store = CreateStoreAsync(false);

        DataPage<TaxoNode> page = await store.GetNodesAsync(
            AllNodes(f => { f.AncestorKey = "oop"; f.TreeId = "fruits"; }));

        Assert.Equal(0, page.Total);
    }

    [Fact]
    public async Task GetNodesAsync_AncestorKeyWithOtherFilters_CombinesThem()
    {
        using PgSqlTaxoStore store = CreateStoreAsync(false);

        DataPage<TaxoNode> page = await store.GetNodesAsync(AllNodes(f =>
        {
            f.AncestorKey = "prog";
            f.IsLeaf = true;
            f.Flags = "d";
        }));

        Assert.Equal(["csharp"], Keys(page));
        Assert.Equal(1, page.Total);
    }

    [Fact]
    public async Task GetNodesAsync_AncestorKeyAndMatchDescendants_CombinesThem()
    {
        using PgSqlTaxoStore store = CreateStoreAsync(false);

        // descendants of prog matching "java" directly or via descendants
        DataPage<TaxoNode> page = await store.GetNodesAsync(AllNodes(f =>
        {
            f.AncestorKey = "prog";
            f.FilteredLabel = "java";
            f.MatchDescendants = true;
        }));

        Assert.Equal(["java", "oop"], Keys(page));
    }

    [Fact]
    public async Task GetNodesAsync_MatchDescendants_ReturnsMatchingAncestors()
    {
        using PgSqlTaxoStore store = CreateStoreAsync(false);

        DataPage<TaxoNode> page = await store.GetNodesAsync(AllNodes(f =>
        {
            f.TreeId = "fruits";
            f.IsRoot = true;
            f.FilteredLabel = "lemon";
            f.MatchDescendants = true;
        }));

        Assert.Equal(["fruit"], Keys(page));
    }

    [Fact]
    public async Task GetNodesAsync_MatchDescendantsNoMatch_ReturnsNone()
    {
        using PgSqlTaxoStore store = CreateStoreAsync(false);

        DataPage<TaxoNode> page = await store.GetNodesAsync(AllNodes(f =>
        {
            f.TreeId = "fruits";
            f.IsRoot = true;
            f.FilteredLabel = "haskell";
            f.MatchDescendants = true;
        }));

        Assert.Empty(page.Items);
    }

    [Fact]
    public async Task GetNodesAsync_ParentKey_ReturnsChildren()
    {
        using PgSqlTaxoStore store = CreateStoreAsync(false);

        DataPage<TaxoNode> page = await store.GetNodesAsync(
            AllNodes(f => f.ParentKey = "citrus"));

        Assert.Equal(["lemon", "orange"], Keys(page));
    }

    [Fact]
    public async Task GetNodesAsync_IsRoot_ReturnsRoots()
    {
        using PgSqlTaxoStore store = CreateStoreAsync(false);

        DataPage<TaxoNode> page = await store.GetNodesAsync(
            AllNodes(f => f.IsRoot = true));

        Assert.Equal(["fruit", "prog"], Keys(page));
    }

    [Fact]
    public async Task GetNodesAsync_IsLeafFalse_ReturnsParents()
    {
        using PgSqlTaxoStore store = CreateStoreAsync(false);

        DataPage<TaxoNode> page = await store.GetNodesAsync(
            AllNodes(f => { f.TreeId = "languages"; f.IsLeaf = false; }));

        Assert.Equal(["func", "oop", "prog"], Keys(page));
    }

    [Theory]
    [InlineData(NodeFlagMatchMode.Any, "do", new[] { "csharp", "haskell" })]
    [InlineData(NodeFlagMatchMode.All, "d", new[] { "csharp" })]
    [InlineData(NodeFlagMatchMode.All, "do", new string[0])]
    [InlineData(NodeFlagMatchMode.None, "do",
        new[] { "func", "java", "oop", "prog" })]
    public async Task GetNodesAsync_Flags_MatchesByMode(NodeFlagMatchMode mode,
        string flags, string[] expected)
    {
        using PgSqlTaxoStore store = CreateStoreAsync(false);

        DataPage<TaxoNode> page = await store.GetNodesAsync(AllNodes(f =>
        {
            f.TreeId = "languages";
            f.Flags = flags;
            f.FlagMatchMode = mode;
        }));

        Assert.Equal(expected, Keys(page));
    }

    [Fact]
    public async Task GetNodesAsync_SpecialAndRepeatedFlags_Work()
    {
        using PgSqlTaxoStore store = CreateStoreAsync(false);
        await store.AddNodeAsync(new TaxoNode
        {
            TreeId = "languages", ParentId = 1, Key = "rust",
            Label = "Rust", FilteredLabel = "rust", Flags = "-%"
        });

        DataPage<TaxoNode> page = await store.GetNodesAsync(AllNodes(f =>
        {
            f.Flags = "%%-";
            f.FlagMatchMode = NodeFlagMatchMode.All;
        }));
        Assert.Equal(["rust"], Keys(page));

        // flags are not LIKE wildcards
        page = await store.GetNodesAsync(AllNodes(f => f.Flags = "_"));
        Assert.Empty(page.Items);
    }

    [Fact]
    public async Task GetNodesAsync_LikeWildcardsInText_AreLiteral()
    {
        using PgSqlTaxoStore store = CreateStoreAsync(false);
        await store.AddNodeAsync(new TaxoNode
        {
            TreeId = "languages", ParentId = 1, Key = "x_100%",
            Label = "100% x", FilteredLabel = "100% x"
        });

        Assert.Equal(["x_100%"], Keys(await store.GetNodesAsync(
            AllNodes(f => f.Key = "_100%"))));
        Assert.Equal(["x_100%"], Keys(await store.GetNodesAsync(
            AllNodes(f => f.FilteredLabel = "0% "))));
        Assert.Empty((await store.GetNodesAsync(
            AllNodes(f => f.Key = "c_ar"))).Items);
        Assert.Equal(["x_100%"], Keys(await store.GetNodesAsync(
            AllNodes(f => f.FilteredLabel = "%"))));
    }

    [Fact]
    public async Task GetNodesAsync_Paging_ReturnsPageWithTotal()
    {
        using PgSqlTaxoStore store = CreateStoreAsync(false);

        DataPage<TaxoNode> page = await store.GetNodesAsync(new TaxoNodeFilter
        {
            PageNumber = 2,
            PageSize = 5,
            TreeId = "fruits"
        });

        Assert.Equal(6, page.Total);
        Assert.Equal(2, page.PageNumber);
        Assert.Equal(5, page.PageSize);
        Assert.Equal(2, page.PageCount);
        Assert.Equal(["strawberry"], Keys(page));
    }

    [Fact]
    public async Task GetNodesAsync_PageBeyondEnd_ReturnsEmptyWithTotal()
    {
        using PgSqlTaxoStore store = CreateStoreAsync(false);

        DataPage<TaxoNode> page = await store.GetNodesAsync(new TaxoNodeFilter
        {
            PageNumber = 5,
            PageSize = 5,
            TreeId = "fruits"
        });

        Assert.Empty(page.Items);
        Assert.Equal(6, page.Total);
    }

    [Fact]
    public async Task GetNodesAsync_PageSizeZero_ReturnsAll()
    {
        using PgSqlTaxoStore store = CreateStoreAsync(false);

        DataPage<TaxoNode> page = await store.GetNodesAsync(new TaxoNodeFilter
        {
            PageNumber = 1,
            PageSize = 0
        });

        Assert.Equal(12, page.Total);
        Assert.Equal(12, page.Items.Count);
    }

    [Fact]
    public async Task GetNodesAsync_PageSizeZeroNoMatch_ReturnsEmpty()
    {
        using PgSqlTaxoStore store = CreateStoreAsync(false);

        DataPage<TaxoNode> page = await store.GetNodesAsync(new TaxoNodeFilter
        {
            PageNumber = 1,
            PageSize = 0,
            Key = "nothing-like-this"
        });

        Assert.Equal(0, page.Total);
        Assert.Empty(page.Items);
    }

    [Fact]
    public async Task GetRootNodes_Paging_ReturnsPage()
    {
        using PgSqlTaxoStore store = CreateStoreAsync(false);
        await store.AddNodeAsync(new TaxoNode
        {
            TreeId = "languages", Key = "a-root", Label = "A",
            FilteredLabel = "a"
        });

        DataPage<TaxoNode> page = await store.GetRootNodes("languages",
            new PagingOptions { PageNumber = 2, PageSize = 1 });

        Assert.Equal(2, page.Total);
        Assert.Equal(["prog"], Keys(page));
    }

    [Fact]
    public async Task GetTreesAsync_Paging_ReturnsPage()
    {
        using PgSqlTaxoStore store = CreateStoreAsync(false);

        DataPage<TaxoTree> page = await store.GetTreesAsync(new TaxoTreeFilter
        {
            PageNumber = 2,
            PageSize = 1
        });

        Assert.Equal(2, page.Total);
        Assert.Equal("languages", Assert.Single(page.Items).Id);
    }

    [Fact]
    public async Task GetTreesAsync_NameWithWildcard_IsLiteral()
    {
        using PgSqlTaxoStore store = CreateStoreAsync(false);

        DataPage<TaxoTree> page = await store.GetTreesAsync(new TaxoTreeFilter
        {
            PageNumber = 1,
            PageSize = 10,
            Name = "%"
        });

        Assert.Equal(0, page.Total);
    }
    #endregion

    #region Node Hierarchy Tests
    [Fact]
    public async Task GetDescendantNodesAsync_ReturnsPreOrder()
    {
        using PgSqlTaxoStore store = CreateStoreAsync(false);

        IList<TaxoNode> nodes = await store.GetDescendantNodesAsync(1);

        Assert.Equal(["func", "haskell", "oop", "csharp", "java"],
            nodes.Select(n => n.Key));
    }

    [Fact]
    public async Task GetDescendantNodesAsync_Leaf_ReturnsEmpty()
    {
        using PgSqlTaxoStore store = CreateStoreAsync(false);

        Assert.Empty(await store.GetDescendantNodesAsync(4));
    }

    [Fact]
    public async Task GetNodePositionsAsync_ReturnsPositions()
    {
        using PgSqlTaxoStore store = CreateStoreAsync(false);

        IDictionary<int, TaxoNodePosition> positions =
            await store.GetNodePositionsAsync([1, 3, 2, 5, 7, 11, 999, 5]);

        Assert.Equal(6, positions.Count);
        Assert.Equal(new TaxoNodePosition(1, 1, 1, true), positions[1]);
        // func before oop
        Assert.Equal(new TaxoNodePosition(3, 2, 1, true), positions[3]);
        Assert.Equal(new TaxoNodePosition(2, 2, 2, true), positions[2]);
        // java after csharp
        Assert.Equal(new TaxoNodePosition(5, 3, 2, false), positions[5]);
        Assert.Equal(new TaxoNodePosition(7, 1, 1, true), positions[7]);
        // lemon before orange
        Assert.Equal(new TaxoNodePosition(11, 3, 1, false), positions[11]);
        Assert.False(positions.ContainsKey(999));
    }

    [Fact]
    public async Task GetNodePositionsAsync_Empty_ReturnsEmpty()
    {
        using PgSqlTaxoStore store = CreateStoreAsync(false);

        Assert.Empty(await store.GetNodePositionsAsync([]));
    }

    [Fact]
    public async Task GetNodePathAsync_SiblingPages_AreCorrect()
    {
        using PgSqlTaxoStore store = CreateStoreAsync(false);

        IList<TaxoNodePathStep> path = await store.GetNodePathAsync(10, 1);

        // fruit (only root), citrus (after berry), orange (after lemon)
        Assert.Equal([new(7, 1), new(8, 2), new(10, 2)], path);
    }
    #endregion

    #region Node Write Tests
    [Fact]
    public async Task AddNodeAsync_Update_UpdatesNode()
    {
        using PgSqlTaxoStore store = CreateStoreAsync(false);
        TaxoNode node = (await store.GetNodeAsync(5))!;
        node.Label = "Java!";
        node.Flags = "x";

        int id = await store.AddNodeAsync(node);

        Assert.Equal(5, id);
        TaxoNode saved = (await store.GetNodeAsync(5))!;
        Assert.Equal("Java!", saved.Label);
        Assert.Equal("x", saved.Flags);
        Assert.Equal(2, saved.ParentId);
    }

    [Fact]
    public async Task AddNodeAsync_ExplicitNewId_AdvancesSequence()
    {
        using PgSqlTaxoStore store = CreateStoreAsync(false);

        int id = await store.AddNodeAsync(new TaxoNode
        {
            Id = 50, TreeId = "languages", ParentId = 1, Key = "go",
            Label = "Go", FilteredLabel = "go"
        });
        Assert.Equal(50, id);

        // a new node must not collide with the explicit ID
        int next = await store.AddNodeAsync(new TaxoNode
        {
            TreeId = "languages", ParentId = 1, Key = "rust",
            Label = "Rust", FilteredLabel = "rust"
        });
        Assert.True(next > 50);
    }

    [Fact]
    public async Task AddNodeAsync_DuplicateKey_ThrowsConflict()
    {
        using PgSqlTaxoStore store = CreateStoreAsync(false);

        await Assert.ThrowsAsync<TaxoStoreConflictException>(() =>
            store.AddNodeAsync(new TaxoNode
            {
                TreeId = "languages", ParentId = 1, Key = "java",
                Label = "Java 2", FilteredLabel = "java 2"
            }));
    }

    [Fact]
    public async Task AddNodeAsync_SameKeyInOtherTree_Succeeds()
    {
        using PgSqlTaxoStore store = CreateStoreAsync(false);

        int id = await store.AddNodeAsync(new TaxoNode
        {
            TreeId = "fruits", ParentId = 7, Key = "java",
            Label = "Java coffee", FilteredLabel = "java coffee"
        });

        Assert.True(id > 0);
    }

    [Fact]
    public async Task AddNodeAsync_MissingParent_ThrowsArgument()
    {
        using PgSqlTaxoStore store = CreateStoreAsync(false);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            store.AddNodeAsync(new TaxoNode
            {
                TreeId = "languages", ParentId = 999, Key = "x",
                Label = "X", FilteredLabel = "x"
            }));
    }

    [Fact]
    public async Task AddNodeAsync_MissingTree_ThrowsArgument()
    {
        using PgSqlTaxoStore store = CreateStoreAsync(false);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            store.AddNodeAsync(new TaxoNode
            {
                TreeId = "none", Key = "x", Label = "X", FilteredLabel = "x"
            }));
    }

    [Theory]
    [InlineData("", "k")]
    [InlineData("languages", "")]
    [InlineData("languages", " ")]
    public async Task AddNodeAsync_MissingTreeOrKey_ThrowsArgument(
        string treeId, string key)
    {
        using PgSqlTaxoStore store = CreateStoreAsync(false);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            store.AddNodeAsync(new TaxoNode
            {
                TreeId = treeId, Key = key, Label = "X", FilteredLabel = "x"
            }));
    }

    [Fact]
    public async Task AddNodeAsync_ParentInOtherTree_ThrowsArgumentAndRollsBack()
    {
        using PgSqlTaxoStore store = CreateStoreAsync(false);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            store.AddNodeAsync(new TaxoNode
            {
                TreeId = "languages", ParentId = 7, Key = "kotlin",
                Label = "Kotlin", FilteredLabel = "kotlin"
            }));

        Assert.Null(await store.GetNodeFromKeyAsync("languages", "kotlin"));
    }

    [Fact]
    public async Task AddNodeAsync_MoveToOtherTreeWithChildren_ThrowsArgument()
    {
        using PgSqlTaxoStore store = CreateStoreAsync(false);
        TaxoNode oop = (await store.GetNodeAsync(2))!;
        oop.TreeId = "fruits";
        oop.ParentId = 7;

        await Assert.ThrowsAsync<ArgumentException>(() =>
            store.AddNodeAsync(oop));

        Assert.Equal("languages", (await store.GetNodeAsync(2))!.TreeId);
    }

    [Fact]
    public async Task AddNodeAsync_OwnParent_ThrowsArgument()
    {
        using PgSqlTaxoStore store = CreateStoreAsync(false);
        TaxoNode node = (await store.GetNodeAsync(2))!;
        node.ParentId = 2;

        await Assert.ThrowsAsync<ArgumentException>(() =>
            store.AddNodeAsync(node));
    }

    [Fact]
    public async Task AddNodeAsync_DescendantAsParent_ThrowsArgumentAndRollsBack()
    {
        using PgSqlTaxoStore store = CreateStoreAsync(false);
        // prog under its grandchild csharp
        TaxoNode prog = (await store.GetNodeAsync(1))!;
        prog.ParentId = 4;

        await Assert.ThrowsAsync<ArgumentException>(() =>
            store.AddNodeAsync(prog));

        Assert.Null((await store.GetNodeAsync(1))!.ParentId);
        Assert.Equal(5, (await store.GetDescendantNodesAsync(1)).Count);
    }

    [Fact]
    public async Task AddNodeAsync_MoveNode_UpdatesHierarchy()
    {
        using PgSqlTaxoStore store = CreateStoreAsync(false);
        // move java under func
        TaxoNode java = (await store.GetNodeAsync(5))!;
        java.ParentId = 3;

        await store.AddNodeAsync(java);

        Assert.Equal(["haskell", "java"],
            (await store.GetChildNodesAsync(3)).Select(n => n.Key));
    }

    [Fact]
    public async Task AddNodesAsync_InvalidNode_RollsBackAll()
    {
        using PgSqlTaxoStore store = CreateStoreAsync(false);

        await Assert.ThrowsAsync<TaxoStoreConflictException>(() =>
            store.AddNodesAsync([
                new TaxoNode
                {
                    TreeId = "languages", ParentId = 1, Key = "go",
                    Label = "Go", FilteredLabel = "go"
                },
                new TaxoNode
                {
                    TreeId = "languages", ParentId = 1, Key = "java",
                    Label = "Java", FilteredLabel = "java"
                }
            ]));

        Assert.Null(await store.GetNodeFromKeyAsync("languages", "go"));
    }

    [Fact]
    public async Task AddNodesAsync_Empty_ReturnsEmpty()
    {
        using PgSqlTaxoStore store = CreateStoreAsync(false);

        Assert.Empty(await store.AddNodesAsync([]));
    }

    [Fact]
    public async Task AddNodesAsync_ManyNodes_ReturnsIdsInOrder()
    {
        using PgSqlTaxoStore store = CreateStoreAsync(false);
        List<TaxoNode> nodes = [.. Enumerable.Range(1, 1200).Select(i =>
            new TaxoNode
            {
                TreeId = "fruits", ParentId = 9, Key = $"b{i:0000}",
                Label = $"B{i}", FilteredLabel = $"b{i}"
            })];

        IList<int> ids = await store.AddNodesAsync(nodes);

        Assert.Equal(1200, ids.Count);
        Assert.Equal(ids.Count, ids.Distinct().Count());
        Assert.Equal("b0001", (await store.GetNodeAsync(ids[0]))!.Key);
        Assert.Equal("b1200", (await store.GetNodeAsync(ids[^1]))!.Key);
    }

    [Fact]
    public async Task AddNodeAsync_EmptyFilteredLabel_UsesLabel()
    {
        using PgSqlTaxoStore store = CreateStoreAsync(false);

        int id = await store.AddNodeAsync(new TaxoNode
        {
            TreeId = "languages", ParentId = 1, Key = "go", Label = "Go"
        });

        Assert.Equal("Go", (await store.GetNodeAsync(id))!.FilteredLabel);
    }

    [Fact]
    public async Task AddTreeAsync_MissingId_ThrowsArgument()
    {
        using PgSqlTaxoStore store = CreateStoreAsync(true);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            store.AddTreeAsync(new TaxoTree { Id = "", Name = "X" }));
    }

    [Fact]
    public async Task DeleteTreeAsync_DeletesItsNodes()
    {
        using PgSqlTaxoStore store = CreateStoreAsync(false);

        await store.DeleteTreeAsync("fruits");

        Assert.Null(await store.GetNodeAsync(7));
        Assert.NotNull(await store.GetNodeAsync(1));
    }

    [Fact]
    public async Task ClearAsync_RestartsNodeIds()
    {
        using PgSqlTaxoStore store = CreateStoreAsync(false);

        await store.ClearAsync();
        await store.AddTreeAsync(new TaxoTree { Id = "t", Name = "T" });
        int id = await store.AddNodeAsync(new TaxoNode
        {
            TreeId = "t", Key = "a", Label = "A", FilteredLabel = "a"
        });

        Assert.Equal(1, id);
    }
    #endregion
}
