using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using TaxoStore.Api.Controllers.Models;
using TaxoStore.Core;

namespace TaxoStore.Api.Controllers.Test;

public sealed class TaxoStoreExceptionFilterAttributeTest
{
    private static ExceptionContext Handle(Exception exception)
    {
        ActionContext actionContext = new(new DefaultHttpContext(),
            new RouteData(), new ActionDescriptor());
        ExceptionContext context = new(actionContext, [])
        {
            Exception = exception
        };
        new TaxoStoreExceptionFilterAttribute().OnException(context);
        return context;
    }

    [Fact]
    public void OnException_Argument_Returns400()
    {
        ExceptionContext context = Handle(new ArgumentException("bad node"));

        Assert.True(context.ExceptionHandled);
        ObjectResult result = Assert.IsType<ObjectResult>(context.Result);
        Assert.Equal(400, result.StatusCode);
        Assert.Equal("bad node",
            Assert.IsType<ProblemDetails>(result.Value).Detail);
    }

    [Fact]
    public void OnException_ArgumentNull_Returns400()
    {
        ExceptionContext context = Handle(new ArgumentNullException("x"));

        Assert.Equal(400, Assert.IsType<ObjectResult>(context.Result).StatusCode);
    }

    [Fact]
    public void OnException_Conflict_Returns409()
    {
        ExceptionContext context = Handle(
            new TaxoStoreConflictException("duplicate"));

        Assert.True(context.ExceptionHandled);
        ObjectResult result = Assert.IsType<ObjectResult>(context.Result);
        Assert.Equal(409, result.StatusCode);
        Assert.Equal("Conflict",
            Assert.IsType<ProblemDetails>(result.Value).Title);
    }

    [Fact]
    public void OnException_Other_IsNotHandled()
    {
        ExceptionContext context = Handle(new InvalidOperationException());

        Assert.False(context.ExceptionHandled);
        Assert.Null(context.Result);
    }

    [Fact]
    public void FilterBindingModel_Defaults_MatchCoreFilter()
    {
        TaxoNodeFilter filter = new TaxoNodeFilterBindingModel().ToNodeFilter();

        Assert.Equal(NodeFeatureMatchMode.All, filter.FeatureMatchMode);
        Assert.Null(filter.Features);
        Assert.Equal(1, filter.PageNumber);
        Assert.Equal(20, filter.PageSize);
    }

    [Fact]
    public void FilterBindingModel_ToNodeFilter_CopiesAll()
    {
        TaxoNodeFilter filter = new TaxoNodeFilterBindingModel
        {
            PageNumber = 2,
            PageSize = 0,
            TreeId = "t",
            ParentId = 3,
            Key = "k",
            ParentKey = "pk",
            AncestorKey = "ak",
            FilteredLabel = "fl",
            Features = ["a", "b=1", "c~x=y", "", "=1"],
            FeatureMatchMode = NodeFeatureMatchMode.None,
            IsLeaf = false,
            IsRoot = true,
            MatchDescendants = true
        }.ToNodeFilter();

        Assert.Equal(2, filter.PageNumber);
        Assert.Equal(0, filter.PageSize);
        Assert.Equal("t", filter.TreeId);
        Assert.Equal(3, filter.ParentId);
        Assert.Equal("k", filter.Key);
        Assert.Equal("pk", filter.ParentKey);
        Assert.Equal("ak", filter.AncestorKey);
        Assert.Equal("fl", filter.FilteredLabel);
        // invalid features are dropped
        Assert.Equal(["a", "b=1", "c~x=y"],
            filter.Features!.Select(f => f.ToString()));
        Assert.Null(filter.Features![0].Value);
        Assert.Equal("1", filter.Features[1].Value);
        Assert.False(filter.Features[1].IsPartial);
        Assert.Equal("c", filter.Features[2].Name);
        Assert.Equal("x=y", filter.Features[2].Value);
        Assert.True(filter.Features[2].IsPartial);
        Assert.Equal(NodeFeatureMatchMode.None, filter.FeatureMatchMode);
        Assert.False(filter.IsLeaf);
        Assert.True(filter.IsRoot);
        Assert.True(filter.MatchDescendants);
    }
}
