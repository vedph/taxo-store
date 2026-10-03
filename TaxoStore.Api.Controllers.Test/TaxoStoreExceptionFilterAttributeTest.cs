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

        Assert.Equal(NodeFlagMatchMode.Any, filter.FlagMatchMode);
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
            Flags = "ab",
            FlagMatchMode = NodeFlagMatchMode.None,
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
        Assert.Equal("ab", filter.Flags);
        Assert.Equal(NodeFlagMatchMode.None, filter.FlagMatchMode);
        Assert.False(filter.IsLeaf);
        Assert.True(filter.IsRoot);
        Assert.True(filter.MatchDescendants);
    }
}
