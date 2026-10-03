using Fusi.Tools.Data;
using Microsoft.AspNetCore.Mvc;
using TaxoStore.Api.Controllers.Models;
using TaxoStore.Core;
using TaxoStore.PgSql;

namespace TaxoStore.Api.Controllers.Test;

[Collection(nameof(NonParallelControllerCollection))]
public sealed class TaxoTreeControllerTest
{
    [Fact]
    public async Task GetTreeAsync_Existing_ReturnsTree()
    {
        using PgSqlTaxoStore store = TaxoNodeControllerTest.CreateStore();
        TaxoTreeController controller = new(store);

        OkObjectResult ok = Assert.IsType<OkObjectResult>(
            await controller.GetTreeAsync("languages"));

        Assert.Equal("languages", Assert.IsType<TaxoTree>(ok.Value).Id);
    }

    [Fact]
    public async Task GetTreeAsync_NotFound_Returns404()
    {
        using PgSqlTaxoStore store = TaxoNodeControllerTest.CreateStore();
        TaxoTreeController controller = new(store);

        Assert.IsType<NotFoundResult>(await controller.GetTreeAsync("none"));
    }

    [Fact]
    public async Task GetTreesAsync_ReturnsPage()
    {
        using PgSqlTaxoStore store = TaxoNodeControllerTest.CreateStore();
        TaxoTreeController controller = new(store);

        OkObjectResult ok = Assert.IsType<OkObjectResult>(
            await controller.GetTreesAsync(new TaxoTreeFilterBindingModel
            {
                Name = "program"
            }));

        DataPage<TaxoTree> page = Assert.IsType<DataPage<TaxoTree>>(ok.Value);
        Assert.Equal("languages", Assert.Single(page.Items).Id);
    }

    [Fact]
    public async Task AddTreeAsync_ReturnsCreatedWithId()
    {
        using PgSqlTaxoStore store = TaxoNodeControllerTest.CreateStore();
        TaxoTreeController controller = new(store);

        IActionResult result = await controller.AddTreeAsync(
            new TaxoTreeBindingModel { Id = "colors", Name = "Colors" });

        CreatedAtRouteResult created = Assert.IsType<CreatedAtRouteResult>(result);
        Assert.Equal("GetTree", created.RouteName);
        Assert.Equal("colors", created.Value);
        Assert.NotNull(await store.GetTreeAsync("colors"));
    }

    [Fact]
    public async Task DeleteTreeAsync_ReturnsIdOrNull()
    {
        using PgSqlTaxoStore store = TaxoNodeControllerTest.CreateStore();
        TaxoTreeController controller = new(store);

        Assert.Equal("languages", Assert.IsType<OkObjectResult>(
            await controller.DeleteTreeAsync("languages")).Value);
        Assert.Null(Assert.IsType<OkObjectResult>(
            await controller.DeleteTreeAsync("languages")).Value);
    }
}
