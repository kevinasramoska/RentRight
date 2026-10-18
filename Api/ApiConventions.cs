using System.Diagnostics;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;

namespace RentRight.Api;

public static class ApiConventions
{
    public const string RoutePrefix = "api/v1";
    public const string OpenApiDocumentName = "v1";
    public const string OpenApiDocumentPath = "/openapi/v1.json";
    public const string AntiforgeryHeaderName = "X-CSRF-TOKEN";
    public const string IdentityCookieSecurityScheme = "IdentityCookie";

    public const int DefaultPage = 1;
    public const int DefaultPageSize = 20;
    public const int MaximumPageSize = 100;
    public const int MaximumPage = 10_000;
}

public static class ApiErrorCodes
{
    public const string InvalidRequest = "invalid_request";
    public const string ValidationFailed = "validation_failed";
    public const string AuthenticationRequired = "authentication_required";
    public const string AccessDenied = "access_denied";
    public const string ResourceNotFound = "resource_not_found";
    public const string ResourceConflict = "resource_conflict";
    public const string InternalError = "internal_error";
    public const string RateLimited = "rate_limited";
}

public sealed class ApiPageRequest
{
    [System.ComponentModel.DataAnnotations.Range(1, ApiConventions.MaximumPage)]
    public int Page { get; init; } = ApiConventions.DefaultPage;

    [System.ComponentModel.DataAnnotations.Range(1, ApiConventions.MaximumPageSize)]
    public int PageSize { get; init; } = ApiConventions.DefaultPageSize;
}

public sealed record ApiPage<T>(
    IReadOnlyList<T> Items,
    int Page,
    int PageSize,
    int TotalCount)
{
    public int TotalPages => Math.Max(
        1,
        (int)Math.Ceiling(TotalCount / (double)PageSize));
}

internal static class ApiProblemDetailsDefaults
{
    public static void Apply(
        ProblemDetails problemDetails,
        HttpContext httpContext,
        string? errorCode = null)
    {
        problemDetails.Instance ??= httpContext.Request.Path;
        problemDetails.Extensions["code"] = errorCode ??
            GetDefaultErrorCode(problemDetails.Status ?? StatusCodes.Status500InternalServerError);
        problemDetails.Extensions.TryAdd(
            "traceId",
            Activity.Current?.Id ?? httpContext.TraceIdentifier);
    }

    public static string ToJsonFieldName(string fieldName)
    {
        if (string.IsNullOrEmpty(fieldName) || fieldName == "$")
        {
            return fieldName;
        }

        return string.Join(
            '.',
            fieldName.Split('.').Select(segment =>
            {
                var bracketIndex = segment.IndexOf('[');
                var propertyName = bracketIndex < 0
                    ? segment
                    : segment[..bracketIndex];
                var suffix = bracketIndex < 0 ? string.Empty : segment[bracketIndex..];
                return JsonNamingPolicy.CamelCase.ConvertName(propertyName) + suffix;
            }));
    }

    private static string GetDefaultErrorCode(int statusCode) => statusCode switch
    {
        StatusCodes.Status400BadRequest => ApiErrorCodes.InvalidRequest,
        StatusCodes.Status401Unauthorized => ApiErrorCodes.AuthenticationRequired,
        StatusCodes.Status403Forbidden => ApiErrorCodes.AccessDenied,
        StatusCodes.Status404NotFound => ApiErrorCodes.ResourceNotFound,
        StatusCodes.Status409Conflict => ApiErrorCodes.ResourceConflict,
        StatusCodes.Status429TooManyRequests => ApiErrorCodes.RateLimited,
        _ when statusCode >= StatusCodes.Status500InternalServerError =>
            ApiErrorCodes.InternalError,
        _ => ApiErrorCodes.InvalidRequest
    };
}
