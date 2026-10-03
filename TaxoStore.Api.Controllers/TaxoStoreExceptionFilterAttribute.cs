using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using System;
using TaxoStore.Core;

namespace TaxoStore.Api.Controllers;

/// <summary>
/// Exception filter mapping the exceptions thrown by an
/// <see cref="ITaxoStore"/> for invalid requests to the corresponding
/// HTTP problem responses: <see cref="ArgumentException"/> to 400 (bad
/// request), and <see cref="TaxoStoreConflictException"/> to 409 (conflict).
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class TaxoStoreExceptionFilterAttribute : ExceptionFilterAttribute
{
    /// <summary>
    /// Called when an action throws an exception.
    /// </summary>
    /// <param name="context">The context.</param>
    public override void OnException(ExceptionContext context)
    {
        int? status = context.Exception switch
        {
            TaxoStoreConflictException => StatusCodes.Status409Conflict,
            ArgumentException => StatusCodes.Status400BadRequest,
            _ => null
        };
        if (status == null) return;

        context.Result = new ObjectResult(new ProblemDetails
        {
            Status = status,
            Title = status == StatusCodes.Status409Conflict
                ? "Conflict" : "Bad Request",
            Detail = context.Exception.Message
        })
        {
            StatusCode = status
        };
        context.ExceptionHandled = true;
    }
}
