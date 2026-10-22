using Microsoft.AspNetCore.Mvc;
using RemsToRems.Application.Features.PatientLookup;
using RemsToRems.Api.Models;

namespace RemsToRems.Api.Controllers
{
    [ApiController]
    [Route("api/patient")]
    public sealed class PatientController : ControllerBase
    {
        private readonly IPatientLookupService _patientLookupService;

        public PatientController(IPatientLookupService patientLookupService)
        {
            _patientLookupService = patientLookupService;
        }

        // Validation and error responses are handled by PatientLookupService and ApiExceptionFilter.
        [HttpPost("lookup")]
        public async Task<ActionResult<ApiResponse<PatientLookupResponse>>> Lookup(
            [FromBody] PatientLookupRequest request,
            CancellationToken cancellationToken)
        {
            var response = await _patientLookupService
                .LookupAsync(request, cancellationToken)
                .ConfigureAwait(false);

            return Ok(ApiResponse<PatientLookupResponse>.Success(response));
        }
    }
}
