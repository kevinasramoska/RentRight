using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using RentRight.Api;
using RentRight.Features.Properties.Contracts;

namespace RentRight.Controllers.Api.V1;

[ApiController]
[Area("Api")]
[Route(ApiConventions.RoutePrefix + "/[controller]")]
[ProducesResponseType<ProblemDetails>(
    StatusCodes.Status400BadRequest,
    "application/problem+json")]
[ProducesResponseType<ProblemDetails>(
    StatusCodes.Status401Unauthorized,
    "application/problem+json")]
[ProducesResponseType<ProblemDetails>(
    StatusCodes.Status403Forbidden,
    "application/problem+json")]
[ProducesResponseType<ProblemDetails>(
    StatusCodes.Status404NotFound,
    "application/problem+json")]
[ProducesResponseType<ProblemDetails>(
    StatusCodes.Status409Conflict,
    "application/problem+json")]
public abstract class ApiV1ControllerBase : ControllerBase
{
    protected IActionResult MapPropertyQueryFailure<T>(
        PropertyQueryOutcome<T> outcome,
        bool concealForbidden = false)
        where T : class => outcome.Status switch
        {
            PropertyOperationStatus.Unauthenticated => ApiProblem(
                StatusCodes.Status401Unauthorized,
                ApiErrorCodes.AuthenticationRequired),
            PropertyOperationStatus.Forbidden when concealForbidden => ApiProblem(
                StatusCodes.Status404NotFound,
                ApiErrorCodes.ResourceNotFound),
            PropertyOperationStatus.Forbidden => ApiProblem(
                StatusCodes.Status403Forbidden,
                ApiErrorCodes.AccessDenied),
            PropertyOperationStatus.NotFound => ApiProblem(
                StatusCodes.Status404NotFound,
                ApiErrorCodes.ResourceNotFound),
            _ => throw new InvalidOperationException(
                "A successful or unsupported property query outcome cannot be mapped as a failure.")
        };

    protected IActionResult MapPropertyMutationFailure(
        PropertyMutationOutcome outcome,
        bool concealForbidden = false) => outcome.Status switch
        {
            PropertyOperationStatus.Unauthenticated => ApiProblem(
                StatusCodes.Status401Unauthorized,
                ApiErrorCodes.AuthenticationRequired),
            PropertyOperationStatus.Forbidden when concealForbidden => ApiProblem(
                StatusCodes.Status404NotFound,
                ApiErrorCodes.ResourceNotFound),
            PropertyOperationStatus.Forbidden => ApiProblem(
                StatusCodes.Status403Forbidden,
                ApiErrorCodes.AccessDenied),
            PropertyOperationStatus.NotFound => ApiProblem(
                StatusCodes.Status404NotFound,
                ApiErrorCodes.ResourceNotFound),
            PropertyOperationStatus.ValidationFailed => ApiValidationProblem(
                outcome.ValidationErrors ?? new Dictionary<string, string[]>()),
            PropertyOperationStatus.Conflict or PropertyOperationStatus.HasHistory => ApiProblem(
                StatusCodes.Status409Conflict,
                ApiErrorCodes.ResourceConflict),
            _ => throw new InvalidOperationException(
                "A successful or unsupported property mutation outcome cannot be mapped as a failure.")
        };

    private ObjectResult ApiProblem(int statusCode, string errorCode)
    {
        var problemDetails = HttpContext.RequestServices
            .GetRequiredService<ProblemDetailsFactory>()
            .CreateProblemDetails(HttpContext, statusCode);
        ApiProblemDetailsDefaults.Apply(problemDetails, HttpContext, errorCode);

        return new ObjectResult(problemDetails)
        {
            StatusCode = statusCode,
            ContentTypes = { "application/problem+json" }
        };
    }

    private ObjectResult ApiValidationProblem(
        IReadOnlyDictionary<string, string[]> validationErrors)
    {
        foreach (var (field, errors) in validationErrors)
        {
            foreach (var error in errors)
            {
                ModelState.AddModelError(field, error);
            }
        }

        var problemDetails = HttpContext.RequestServices
            .GetRequiredService<ProblemDetailsFactory>()
            .CreateValidationProblemDetails(HttpContext, ModelState);

        return new ObjectResult(problemDetails)
        {
            StatusCode = StatusCodes.Status400BadRequest,
            ContentTypes = { "application/problem+json" }
        };
    }
}
