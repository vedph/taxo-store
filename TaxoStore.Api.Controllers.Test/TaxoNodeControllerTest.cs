using Fusi.DbManager.PgSql;
using Fusi.Tools.Data;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using TaxoStore.Api.Controllers.Models;
using TaxoStore.Core;
using TaxoStore.PgSql;

namespace TaxoStore.Api.Controllers.Test;

public class NonParallelControllerCollection { }

[CollectionDefinition(nameof(NonParallelControllerCollection),
    DisableParallelization = true)]
[Collection(nameof(NonParallelControllerCollection))]
public sealed class TaxoNodeControllerTest
{
    private const string CS_TEMPLATE =
        "User ID=postgres;Password=postgres;Host=localhost;Port=5432;Database={0}";
    // a database distinct from the store tests one, as test assemblies
    // can run in parallel
    private const string DB_NAME = "taxo-store-ctl-test";

    private const string TREES_CSV = @"id,name,note
languages,Programming Languages,
";

    // IDs: prog=1, oop=2, func=3, csharp=4, java=5, haskell=6
    private const string NODES_CSV = @"tree_n,parent_key,key,label,filtered_label,flags
1,,prog,Programming,programming,
1,prog,oop,Object-Oriented,object oriented,
1,prog,func,Functional,functional,
1,oop,csharp,C#,c,d
1,oop,java,Java,java,
1,func,haskell,Haskell,haskell,o
";

    private static readonly Lock _lockObject = new();

    internal static PgSqlTaxoStore CreateStore()
    {
        lock (_lockObject)
        {
            NpgsqlConnection.ClearAllPools();
            PgSqlDbManager dbManager = new(CS_TEMPLATE);
            if (dbManager.Exists(DB_NAME)) dbManager.RemoveDatabase(DB_NAME);

            PgSqlTaxoStore store = new(new TaxoStoreOptions
            {
                Source = string.Format(CS_TEMPLATE, DB_NAME),
                SeedSourceAsText = true,
                SeedTreeSource = TREES_CSV,
                SeedNodeSource = NODES_CSV
            });
            store.InitializeAsync().Wait();
            return store;
        }
    }

    private static T GetValue<T>(IActionResult result)
    {
        OkObjectResult ok = Assert.IsType<OkObjectResult>(result);
        return Assert.IsAssignableFrom<T>(ok.Value);
    }

    [Fact]
    public async Task GetNodeAsync_Existing_ReturnsPositionedNode()
    {
        using PgSqlTaxoStore store = CreateStore();
        TaxoNodeController controller = new(store);

        PositionedTaxoNodeModel node = GetValue<PositionedTaxoNodeModel>(
            await controller.GetNodeAsync(5));

        Assert.Equal("java", node.Key);
        Assert.Equal(2, node.ParentId);
        Assert.Equal(3, node.Y);
        Assert.Equal(2, node.X);
        Assert.False(node.HasChildren);
    }

    [Fact]
    public async Task GetNodeAsync_NotFound_Returns404()
    {
        using PgSqlTaxoStore store = CreateStore();
        TaxoNodeController controller = new(store);

        Assert.IsType<NotFoundResult>(await controller.GetNodeAsync(999));
    }

    [Fact]
    public async Task GetNodeFromKeyAsync_Existing_ReturnsPositionedNode()
    {
        using PgSqlTaxoStore store = CreateStore();
        TaxoNodeController controller = new(store);

        PositionedTaxoNodeModel node = GetValue<PositionedTaxoNodeModel>(
            await controller.GetNodeFromKeyAsync("languages", "oop"));

        Assert.Equal(2, node.Id);
        Assert.Equal(2, node.Y);
        Assert.Equal(2, node.X);
        Assert.True(node.HasChildren);
    }

    [Fact]
    public async Task GetNodeFromKeyAsync_NotFound_Returns404()
    {
        using PgSqlTaxoStore store = CreateStore();
        TaxoNodeController controller = new(store);

        Assert.IsType<NotFoundResult>(
            await controller.GetNodeFromKeyAsync("languages", "none"));
    }

    [Fact]
    public async Task GetNodesAsync_WithPosition_ReturnsPositionedPage()
    {
        using PgSqlTaxoStore store = CreateStore();
        TaxoNodeController controller = new(store);

        DataPage<PositionedTaxoNodeModel> page =
            GetValue<DataPage<PositionedTaxoNodeModel>>(
                await controller.GetNodesAsync(new TaxoNodeFilterBindingModel
                {
                    TreeId = "languages",
                    ParentId = 2,
                    IncludePosition = true
                }));

        Assert.Equal(2, page.Total);
        Assert.Equal(["csharp", "java"], page.Items.Select(n => n.Key));
        Assert.Equal([1, 2], page.Items.Select(n => n.X));
        Assert.All(page.Items, n => Assert.Equal(3, n.Y));
    }

    [Fact]
    public async Task GetNodesAsync_WithoutPosition_ReturnsNodes()
    {
        using PgSqlTaxoStore store = CreateStore();
        TaxoNodeController controller = new(store);

        DataPage<TaxoNode> page = GetValue<DataPage<TaxoNode>>(
            await controller.GetNodesAsync(new TaxoNodeFilterBindingModel
            {
                AncestorKey = "prog",
                Flags = "do",
                FlagMatchMode = NodeFlagMatchMode.None
            }));

        Assert.Equal(["func", "java", "oop"], page.Items.Select(n => n.Key));
    }

    [Fact]
    public async Task GetRootNodesAsync_WithPosition_ReturnsRoots()
    {
        using PgSqlTaxoStore store = CreateStore();
        TaxoNodeController controller = new(store);

        DataPage<PositionedTaxoNodeModel> page =
            GetValue<DataPage<PositionedTaxoNodeModel>>(
                await controller.GetRootNodesAsync("languages",
                    new TaxoNodeFilterBindingModel { IncludePosition = true }));

        PositionedTaxoNodeModel root = Assert.Single(page.Items);
        Assert.Equal("prog", root.Key);
        Assert.Equal(1, root.Y);
        Assert.Equal(1, root.X);
        Assert.True(root.HasChildren);
    }

    [Fact]
    public async Task GetRootNodesAsync_WithoutPosition_ReturnsRoots()
    {
        using PgSqlTaxoStore store = CreateStore();
        TaxoNodeController controller = new(store);

        DataPage<TaxoNode> page = GetValue<DataPage<TaxoNode>>(
            await controller.GetRootNodesAsync("languages",
                new TaxoNodeFilterBindingModel { PageSize = 0 }));

        Assert.Equal("prog", Assert.Single(page.Items).Key);
    }

    [Fact]
    public async Task HierarchyEndpoints_WithPosition_ReturnPositionedNodes()
    {
        using PgSqlTaxoStore store = CreateStore();
        TaxoNodeController controller = new(store);

        IList<PositionedTaxoNodeModel> children =
            GetValue<IList<PositionedTaxoNodeModel>>(
                await controller.GetChildNodesAsync(1, true));
        Assert.Equal(["func", "oop"], children.Select(n => n.Key));
        Assert.Equal([1, 2], children.Select(n => n.X));

        IList<PositionedTaxoNodeModel> descendants =
            GetValue<IList<PositionedTaxoNodeModel>>(
                await controller.GetDescendantNodesAsync(1, true));
        Assert.Equal(["func", "haskell", "oop", "csharp", "java"],
            descendants.Select(n => n.Key));
        Assert.Equal([2, 3, 2, 3, 3], descendants.Select(n => n.Y));

        IList<PositionedTaxoNodeModel> ancestors =
            GetValue<IList<PositionedTaxoNodeModel>>(
                await controller.GetAncestorNodesAsync(4, true));
        Assert.Equal(["oop", "prog"], ancestors.Select(n => n.Key));
        Assert.All(ancestors, n => Assert.True(n.HasChildren));
    }

    [Fact]
    public async Task HierarchyEndpoints_WithoutPosition_ReturnNodes()
    {
        using PgSqlTaxoStore store = CreateStore();
        TaxoNodeController controller = new(store);

        Assert.Equal(2, GetValue<IList<TaxoNode>>(
            await controller.GetChildNodesAsync(1)).Count);
        Assert.Equal(5, GetValue<IList<TaxoNode>>(
            await controller.GetDescendantNodesAsync(1)).Count);
        Assert.Equal(2, GetValue<IList<TaxoNode>>(
            await controller.GetAncestorNodesAsync(4)).Count);
        Assert.True(GetValue<bool>(await controller.NodeHasChildrenAsync(1)));
    }

    [Fact]
    public async Task AddNodeAsync_New_ReturnsCreatedWithId()
    {
        using PgSqlTaxoStore store = CreateStore();
        TaxoNodeController controller = new(store);

        IActionResult result = await controller.AddNodeAsync(
            new TaxoNodeBindingModel
            {
                TreeId = "languages",
                ParentId = 3,
                Key = "lisp",
                Label = "Lisp"
            });

        CreatedAtRouteResult created = Assert.IsType<CreatedAtRouteResult>(result);
        Assert.Equal("GetTaxoNode", created.RouteName);
        int id = Assert.IsType<int>(created.Value);
        Assert.Equal(id, created.RouteValues!["id"]);
        TaxoNode saved = (await store.GetNodeAsync(id))!;
        Assert.Equal("lisp", saved.Key);
        // filtered label defaults to label
        Assert.Equal("Lisp", saved.FilteredLabel);
    }

    [Fact]
    public async Task AddNodeAsync_Update_ReturnsCreatedWithSameId()
    {
        using PgSqlTaxoStore store = CreateStore();
        TaxoNodeController controller = new(store);

        IActionResult result = await controller.AddNodeAsync(
            new TaxoNodeBindingModel
            {
                Id = 5,
                TreeId = "languages",
                ParentId = 2,
                Key = "java",
                Label = "Java 21"
            });

        Assert.Equal(5, Assert.IsType<CreatedAtRouteResult>(result).Value);
        Assert.Equal("Java 21", (await store.GetNodeAsync(5))!.Label);
    }

    [Fact]
    public async Task AddNodesAsync_ReturnsIds()
    {
        using PgSqlTaxoStore store = CreateStore();
        TaxoNodeController controller = new(store);

        IList<int> ids = GetValue<IList<int>>(await controller.AddNodesAsync([
            new TaxoNodeBindingModel
            {
                TreeId = "languages", ParentId = 3, Key = "lisp", Label = "Lisp"
            },
            new TaxoNodeBindingModel
            {
                TreeId = "languages", ParentId = 3, Key = "ml", Label = "ML"
            }
        ]));

        Assert.Equal(2, ids.Count);
    }

    [Fact]
    public async Task DeleteNodeAsync_ReturnsId()
    {
        using PgSqlTaxoStore store = CreateStore();
        TaxoNodeController controller = new(store);

        Assert.Equal(6, GetValue<int>(await controller.DeleteNodeAsync(6)));
        Assert.Equal(0, GetValue<int>(await controller.DeleteNodeAsync(6)));
    }

    [Fact]
    public async Task GetNodePathAsync_ReturnsPath()
    {
        using PgSqlTaxoStore store = CreateStore();
        TaxoNodeController controller = new(store);

        IList<TaxoNodePathStep> path = GetValue<IList<TaxoNodePathStep>>(
            await controller.GetNodePathAsync(5, 1));

        Assert.Equal([new(1, 1), new(2, 2), new(5, 2)], path);
    }

    [Fact]
    public async Task GetNodePathAsync_InvalidPageSize_ReturnsBadRequest()
    {
        using PgSqlTaxoStore store = CreateStore();
        TaxoNodeController controller = new(store);

        Assert.IsType<BadRequestObjectResult>(
            await controller.GetNodePathAsync(5, 0));
    }
}
