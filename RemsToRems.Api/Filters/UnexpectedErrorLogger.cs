using System.Text.Json;
using Microsoft.Extensions.Options;
using RemsToRems.Application.Abstractions.Auditing;
using RemsToRems.Application.Abstractions.Database;

namespace RemsToRems.Api.Filters
{
    /// <summary>
    /// Records an unexpected (500) error: the application log, plus a row in [ErrorLog] in the configured
    /// REMS database. Used by controller catch blocks and by ApiExceptionFilter, so both log the same way.
    /// Writing the error log never throws; a failure there is only logged.
    /// </summary>
    public sealed class UnexpectedErrorLogger
    {
        // No authentication yet; SurveyApi records this as the system user.
        private const long AnonymousUserId = 1;

        private readonly IAuditTrailService _auditTrailService;
        private readonly ILogger<UnexpectedErrorLogger> _logger;
        private readonly DatabaseContext _databaseContext;

        public UnexpectedErrorLogger(
            IAuditTrailService auditTrailService,
            ILogger<UnexpectedErrorLogger> logger,
            IOptions<DatabaseContextOptions> databaseContext)
        {
            _auditTrailService = auditTrailService;
            _logger = logger;
            _databaseContext = databaseContext.Value.ToDatabaseContext();
        }

        public async Task LogAsync(
            HttpContext httpContext,
            string controllerName,
            string actionName,
            IEnumerable<object?> requestArguments,
            Exception exception)
        {
            _logger.LogError(exception, "Unhandled exception in {ControllerName}.{ActionName}.", controllerName, actionName);

            var errorLog = new ErrorLog
            {
                OriginUrl = $"{httpContext.Request.Path}{httpContext.Request.QueryString}",
                ControllerName = controllerName,
                MethodName = actionName,
                RequestJson = Serialize(requestArguments.Where(argument => argument is not CancellationToken).ToList()),
                ErrorJson = Serialize(Describe(exception))
            };

            // Use CancellationToken.None: the error should be recorded even if the client disconnected.
            await _auditTrailService.InsertErrorLogAsync(
                _databaseContext,
                errorLog,
                AnonymousUserId,
                CancellationToken.None).ConfigureAwait(false);
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
