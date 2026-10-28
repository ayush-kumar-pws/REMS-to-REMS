using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using RemsToRems.Application.Common.Exceptions;
using RemsToRems.Api.Models;

namespace RemsToRems.Api.Filters
{
    /// <summary>
    /// Safety net for exceptions a controller action did not catch itself:
    /// ValidationException -> 400, NotFoundException -> 404, anything else -> 500, all as problem+json.
    /// Unexpected (500) errors are recorded by UnexpectedErrorLogger, the same as controller catch blocks.
    /// </summary>
    public sealed class ApiExceptionFilter : IAsyncActionFilter
    {
        private readonly UnexpectedErrorLogger _errorLogger;
        private readonly ILogger<ApiExceptionFilter> _logger;

        public ApiExceptionFilter(UnexpectedErrorLogger errorLogger, ILogger<ApiExceptionFilter> logger)
        {
            _errorLogger = errorLogger;
            _logger = logger;
        }

        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            var executed = await next();
            if (executed.Exception is null || executed.ExceptionHandled)
            {
                return;
            }

            var exception = executed.Exception;
            var httpContext = context.HttpContext;
            switch (exception)
            {
                case ValidationException validation:
                    executed.Result = ProblemResponses.Validation(httpContext, validation.Errors, validation.RequestId);
                    break;

                case NotFoundException notFound:
                    _logger.LogWarning("Request referred to something that does not exist: {Message}", notFound.Message);
                    executed.Result = ProblemResponses.NotFound(httpContext, notFound.Message);
                    break;

                default:
                    var descriptor = context.ActionDescriptor as ControllerActionDescriptor;
                    await _errorLogger.LogAsync(
                        context.HttpContext,
                        descriptor?.ControllerName ?? string.Empty,
                        descriptor?.ActionName ?? string.Empty,
                        context.ActionArguments.Values,
                        exception);
                    executed.Result = ProblemResponses.Unexpected(httpContext);
                    break;
            }

            executed.ExceptionHandled = true;
        }
    }
}
