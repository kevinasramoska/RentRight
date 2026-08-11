using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.Options;

namespace RentRight.Api;

public sealed class RentRightProblemDetailsFactory(
    IOptions<ApiBehaviorOptions> apiBehaviorOptions) : ProblemDetailsFactory
{
    private readonly ApiBehaviorOptions _apiBehaviorOptions = apiBehaviorOptions.Value;

    public override ProblemDetails CreateProblemDetails(
        HttpContext httpContext,
        int? statusCode = null,
        string? title = null,
        string? type = null,
        string? detail = null,
        string? instance = null)
    {
        var problemDetails = new ProblemDetails
        {
            Status = statusCode ?? StatusCodes.Status500InternalServerError,
            Title = title,
            Type = type,
            Detail = detail,
            Instance = instance
        };

        ApplyDefaults(problemDetails, httpContext);
        return problemDetails;
    }

    public override ValidationProblemDetails CreateValidationProblemDetails(
        HttpContext httpContext,
        ModelStateDictionary modelStateDictionary,
        int? statusCode = null,
        string? title = null,
        string? type = null,
        string? detail = null,
        string? instance = null)
    {
        ArgumentNullException.ThrowIfNull(modelStateDictionary);

        var errors = modelStateDictionary
            .Where(entry => entry.Value is { Errors.Count: > 0 })
            .ToDictionary(
                entry => ApiProblemDetailsDefaults.ToJsonFieldName(entry.Key),
                entry => entry.Value!.Errors
                    .Select(error => string.IsNullOrWhiteSpace(error.ErrorMessage)
                        ? "The supplied value is invalid."
                        : error.ErrorMessage)
                    .ToArray(),
                StringComparer.Ordinal);

        var problemDetails = new ValidationProblemDetails(errors)
        {
            Status = statusCode ?? StatusCodes.Status400BadRequest,
            Title = title,
            Type = type,
            Detail = detail,
            Instance = instance
        };

        ApplyDefaults(problemDetails, httpContext, ApiErrorCodes.ValidationFailed);
        return problemDetails;
    }

    private void ApplyDefaults(
        ProblemDetails problemDetails,
        HttpContext httpContext,
        string? errorCode = null)
    {
        var statusCode = problemDetails.Status ?? StatusCodes.Status500InternalServerError;
        if (_apiBehaviorOptions.ClientErrorMapping.TryGetValue(statusCode, out var clientError))
        {
            problemDetails.Title ??= clientError.Title;
            problemDetails.Type ??= clientError.Link;
        }

        ApiProblemDetailsDefaults.Apply(problemDetails, httpContext, errorCode);
    }
}
