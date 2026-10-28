using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using RemsToRems.Api.Filters;
using RemsToRems.Api.Models;
using RemsToRems.Application.Common.Exceptions;
using RemsToRems.Application.Features.PatientLookup;

namespace RemsToRems.Api.Controllers
{
    [ApiController]
    [Route("api/registry")]
    public sealed class PatientController : ControllerBase
    {
        private readonly IPatientLookupService _patientLookupService;
        private readonly UnexpectedErrorLogger _errorLogger;
        private readonly ILogger<PatientController> _logger;

        public PatientController(
            IPatientLookupService patientLookupService,
            UnexpectedErrorLogger errorLogger,
            ILogger<PatientController> logger)
        {
            _patientLookupService = patientLookupService;
            _errorLogger = errorLogger;
            _logger = logger;
        }

        /// <summary>
        /// Success returns the match response as-is (no wrapper), in the agreed exchange format.
        /// Every error is application/problem+json (ProblemResponses): invalid input -> 400 with one entry
        /// per field and its partner code, not found -> 404, anything else -> 500 (logged to [ErrorLog]).
        /// The body is read as plain JSON so a wrong value type is a field error, not a binding failure;
        /// a body that is empty or not JSON is answered by InvalidModelStateResponseFactory in Program.
        /// </summary>
        [HttpPost("match")]
        [Consumes("application/json")]
        public async Task<ActionResult<PatientLookupResponse>> Match(
            [FromBody] JsonElement body,
            CancellationToken cancellationToken)
        {
            try
            {
                var request = PatientLookupRequestReader.Read(body);
                var response = await _patientLookupService
                    .LookupAsync(request, cancellationToken)
                    .ConfigureAwait(false);

                return Ok(response);
            }
            catch (ValidationException ex)
            {
                // Bad input: every problem is listed. Not an application error, so not logged.
                return ProblemResponses.Validation(HttpContext, ex.Errors, ex.RequestId);
            }
            catch (NotFoundException ex)
            {
                _logger.LogWarning("Request referred to something that does not exist: {Message}", ex.Message);
                return ProblemResponses.NotFound(HttpContext, ex.Message);
            }
            catch (Exception ex)
            {
                // Database down, bad configuration, SQL error, ...: logged with the request, never shown to the caller.
                await _errorLogger.LogAsync(
                    HttpContext,
                    ControllerContext.ActionDescriptor.ControllerName,
                    ControllerContext.ActionDescriptor.ActionName,
                    new object?[] { body },
                    ex).ConfigureAwait(false);

                return ProblemResponses.Unexpected(HttpContext);
            }
        }
    }
}
