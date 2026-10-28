using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using RemsToRems.Application.Common.Exceptions;

namespace RemsToRems.Api.Models;

/// <summary>
/// An error response: RFC 9457 problem details plus the exchange's requestId (when the request had a valid
/// one), a new responseId and, for validation errors, one entry per failed field.
/// </summary>
public sealed class ExchangeProblem
{
    public string Type { get; init; } = string.Empty;

    public string Title { get; init; } = string.Empty;

    public int Status { get; init; }

    public string Detail { get; init; } = string.Empty;

    public string Instance { get; init; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Guid? RequestId { get; init; }

    public Guid ResponseId { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<ValidationError>? Errors { get; init; }
}

/// <summary>Every error the API returns, as application/problem+json.</summary>
public static class ProblemResponses
{
    public const string ContentType = "application/problem+json";

    private const string TypePrefix = "urn:rems-exchange:problem:";
    private const string ValidationType = "validation-error";
    private const string ValidationTitle = "The request is not valid";

    /// <summary>400: one entry per failed field, with its partner code. The lookup did not run.</summary>
    public static ObjectResult Validation(HttpContext httpContext, IReadOnlyList<ValidationError> errors, Guid? requestId)
    {
        var fields = errors.Count == 1 ? "1 field" : $"{errors.Count} fields";
        return Create(httpContext, StatusCodes.Status400BadRequest, ValidationType, ValidationTitle,
            $"{fields} failed validation. No lookup was performed.", requestId, errors);
    }

    /// <summary>
    /// 400 for a body that is empty or not valid JSON. No error code is agreed for this (pending partner
    /// confirmation), so errors is empty and the detail says what is wrong.
    /// </summary>
    public static ObjectResult MalformedBody(HttpContext httpContext)
    {
        return Create(httpContext, StatusCodes.Status400BadRequest, ValidationType, ValidationTitle,
            "The request body is missing or is not valid JSON. No lookup was performed.", null,
            Array.Empty<ValidationError>());
    }

    public static ObjectResult NotFound(HttpContext httpContext, string detail)
    {
        return Create(httpContext, StatusCodes.Status404NotFound, "not-found", "Not found", detail, null, null);
    }

    /// <summary>500: the cause is logged to [ErrorLog], never returned.</summary>
    public static ObjectResult Unexpected(HttpContext httpContext)
    {
        return Create(httpContext, StatusCodes.Status500InternalServerError, "internal-error",
            "An unexpected error occurred", "The request could not be completed.", null, null);
    }

    private static ObjectResult Create(
        HttpContext httpContext,
        int status,
        string type,
        string title,
        string detail,
        Guid? requestId,
        IReadOnlyList<ValidationError>? errors)
    {
        var problem = new ExchangeProblem
        {
            Type = TypePrefix + type,
            Title = title,
            Status = status,
            Detail = detail,
            Instance = httpContext.Request.Path.Value ?? string.Empty,
            RequestId = requestId,
            ResponseId = Guid.NewGuid(),
            Errors = errors
        };

        return new ObjectResult(problem)
        {
            StatusCode = status,
            ContentTypes = { ContentType }
        };
    }
}
