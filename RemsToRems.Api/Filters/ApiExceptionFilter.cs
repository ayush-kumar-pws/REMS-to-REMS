using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using RemsToRems.Application.Abstractions.Auditing;
using RemsToRems.Application.Abstractions.Database;
using RemsToRems.Application.Common.Exceptions;
using RemsToRems.Api.Models;

namespace RemsToRems.Api.Filters
{
    /// <summary>
    /// Turns exceptions from controller actions into ApiResponse errors:
    /// ValidationException -> 400, NotFoundException -> 404, anything else -> 500.
    /// Unexpected (500) errors are also written to [ErrorLog] in the request's REMS database,
    /// replacing SurveyApi's per-action catch blocks.
    /// </summary>
    public sealed class ApiExceptionFilter : IAsyncActionFilter
    {
        // No authentication yet; SurveyApi records this as the system user.
        private const long AnonymousUserId = 1;

        private readonly IAuditTrailService _auditTrailService;
        private readonly ILogger<ApiExceptionFilter> _logger;

        public ApiExceptionFilter(IAuditTrailService auditTrailService, ILogger<ApiExceptionFilter> logger)
        {
            _auditTrailService = auditTrailService;
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
            switch (exception)
            {
                case ValidationException validation:
                    executed.Result = Error(StatusCodes.Status400BadRequest, validation.Message);
                    break;

                case NotFoundException notFound:
                    _logger.LogWarning("Request referred to something that does not exist: {Message}", notFound.Message);
                    executed.Result = Error(StatusCodes.Status404NotFound, notFound.Message);
                    break;

                default:
                    await LogUnexpectedErrorAsync(context, exception);
                    executed.Result = Error(StatusCodes.Status500InternalServerError, "An unexpected error occurred.");
                    break;
            }

            executed.ExceptionHandled = true;
        }

        private async Task LogUnexpectedErrorAsync(ActionExecutingContext context, Exception exception)
        {
            var httpContext = context.HttpContext;
            var descriptor = context.ActionDescriptor as ControllerActionDescriptor;
            var arguments = context.ActionArguments.Values
                .Where(argument => argument is not CancellationToken)
                .ToList();

            _logger.LogError(
                exception,
                "Unhandled exception in {ControllerName}.{ActionName}.",
                descriptor?.ControllerName,
                descriptor?.ActionName);

            var errorLog = new ErrorLog
            {
                OriginUrl = $"{httpContext.Request.Path}{httpContext.Request.QueryString}",
                ControllerName = descriptor?.ControllerName ?? string.Empty,
                MethodName = descriptor?.ActionName ?? string.Empty,
                RequestJson = Serialize(arguments),
                ErrorJson = Serialize(Describe(exception))
            };

            var databaseContext = arguments
                .OfType<IHasDatabaseContext>()
                .FirstOrDefault()
                ?.ToDatabaseContext();

            // Use CancellationToken.None: the error should be recorded even if the client disconnected.
            await _auditTrailService.InsertErrorLogAsync(
                databaseContext,
                errorLog,
                AnonymousUserId,
                CancellationToken.None);
        }

        private static ObjectResult Error(int statusCode, string message)
        {
            return new ObjectResult(ApiResponse<object>.Error(message)) { StatusCode = statusCode };
        }

        // System.Text.Json cannot serialize Exception directly (TargetSite), so log the useful parts.
        private static object Describe(Exception exception)
        {
            return new
            {
                Type = exception.GetType().FullName,
                exception.Message,
                exception.StackTrace,
                InnerException = exception.InnerException is null ? null : Describe(exception.InnerException)
            };
        }

        private static string Serialize(object value)
        {
            try
            {
                return JsonSerializer.Serialize(value);
            }
            catch (Exception ex) when (ex is NotSupportedException or JsonException)
            {
                return string.Empty;
            }
        }
    }
}
