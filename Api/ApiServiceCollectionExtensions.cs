using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;

namespace RentRight.Api;

public static class ApiServiceCollectionExtensions
{
    public static IServiceCollection AddApiConventions(this IServiceCollection services)
    {
        services.AddProblemDetails(options =>
        {
            options.CustomizeProblemDetails = context =>
                ApiProblemDetailsDefaults.Apply(
                    context.ProblemDetails,
                    context.HttpContext);
        });
        services.AddSingleton<Microsoft.AspNetCore.Mvc.Infrastructure.ProblemDetailsFactory,
            RentRightProblemDetailsFactory>();
        services.AddAntiforgery(options =>
            options.HeaderName = ApiConventions.AntiforgeryHeaderName);
        services.ConfigureApplicationCookie(options =>
        {
            options.Events.OnRedirectToLogin = context =>
                HandleCookieRedirectAsync(context, StatusCodes.Status401Unauthorized);
            options.Events.OnRedirectToAccessDenied = context =>
                HandleCookieRedirectAsync(context, StatusCodes.Status403Forbidden);
        });

        services.AddOpenApi(ApiConventions.OpenApiDocumentName, options =>
        {
            options.ShouldInclude = description =>
                description.RelativePath?.StartsWith(
                    ApiConventions.RoutePrefix + '/',
                    StringComparison.OrdinalIgnoreCase) == true;
            options.AddDocumentTransformer<ApiOpenApiDocumentTransformer>();
            options.AddOperationTransformer<ApiOpenApiOperationTransformer>();
        });

        return services;
    }

    public static IMvcBuilder AddApiJsonConventions(this IMvcBuilder mvcBuilder)
    {
        mvcBuilder.AddMvcOptions(options =>
            options.Conventions.Add(new RouteTokenTransformerConvention(
                new KebabCaseRouteTokenTransformer())));

        return mvcBuilder.AddJsonOptions(options =>
        {
            options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
            options.JsonSerializerOptions.DictionaryKeyPolicy = JsonNamingPolicy.CamelCase;
            options.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
            options.JsonSerializerOptions.DefaultIgnoreCondition =
                JsonIgnoreCondition.WhenWritingNull;
        });
    }

    public static IApplicationBuilder UseApiProblemDetails(
        this IApplicationBuilder applicationBuilder)
    {
        applicationBuilder.UseWhen(
            context => context.Request.Path.StartsWithSegments(
                '/' + ApiConventions.RoutePrefix),
            branch =>
            {
                branch.UseExceptionHandler();
                branch.UseStatusCodePages();
            });

        return applicationBuilder;
    }

    private static Task HandleCookieRedirectAsync(
        RedirectContext<CookieAuthenticationOptions> context,
        int apiStatusCode)
    {
        if (context.Request.Path.StartsWithSegments('/' + ApiConventions.RoutePrefix))
        {
            context.Response.StatusCode = apiStatusCode;
        }
        else
        {
            context.Response.Redirect(context.RedirectUri);
        }

        return Task.CompletedTask;
    }
}

internal sealed partial class KebabCaseRouteTokenTransformer :
    IOutboundParameterTransformer
{
    public string? TransformOutbound(object? value) => value is null
        ? null
        : RouteWordBoundary().Replace(value.ToString()!, "$1-$2").ToLowerInvariant();

    [GeneratedRegex("([a-z0-9])([A-Z])")]
    private static partial Regex RouteWordBoundary();
}

internal sealed class ApiOpenApiDocumentTransformer(
    IOptionsMonitor<CookieAuthenticationOptions> cookieOptions) :
    IOpenApiDocumentTransformer
{
    public Task TransformAsync(
        OpenApiDocument document,
        OpenApiDocumentTransformerContext context,
        CancellationToken cancellationToken)
    {
        document.Info = new OpenApiInfo
        {
            Title = "RentRight API",
            Version = "v1",
            Description =
                "Same-origin JSON API. Identity cookies authenticate requests; " +
                $"unsafe requests also require the {ApiConventions.AntiforgeryHeaderName} header."
        };
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??=
            new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes[ApiConventions.IdentityCookieSecurityScheme] =
            new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.ApiKey,
                In = ParameterLocation.Cookie,
                Name = cookieOptions
                    .Get(IdentityConstants.ApplicationScheme)
                    .Cookie.Name ?? ".AspNetCore.Identity.Application",
                Description = "ASP.NET Core Identity application cookie."
            };

        return Task.CompletedTask;
    }
}

internal sealed class ApiOpenApiOperationTransformer : IOpenApiOperationTransformer
{
    public Task TransformAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        var metadata = context.Description.ActionDescriptor.EndpointMetadata;
        if (metadata.OfType<IAllowAnonymous>().Any() ||
            !metadata.OfType<IAuthorizeData>().Any())
        {
            return Task.CompletedTask;
        }

        operation.Security ??= [];
        operation.Security.Add(new OpenApiSecurityRequirement
        {
            [new OpenApiSecuritySchemeReference(
                ApiConventions.IdentityCookieSecurityScheme,
                context.Document)] = []
        });

        return Task.CompletedTask;
    }
}
