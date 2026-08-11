using RentRight.Application.Abstractions;

namespace RentRight.Application;

public static class ApplicationServiceCollectionExtensions
{
    public static IServiceCollection AddApplicationFoundation(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddSingleton<IClock, SystemClock>();
        services.AddScoped<ICurrentUser, HttpContextCurrentUser>();

        return services;
    }
}
