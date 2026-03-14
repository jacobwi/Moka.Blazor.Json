using Microsoft.Extensions.DependencyInjection;
using Moka.Blazor.Json.Interop;
using Moka.Blazor.Json.Models;
using Moka.Blazor.Json.Services;

namespace Moka.Blazor.Json.Extensions;

/// <summary>
///     Extension methods for registering Moka JSON viewer services with the DI container.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    ///     Adds the Moka JSON viewer services to the service collection with default options.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddMokaJsonViewer(this IServiceCollection services)
    {
        return services.AddMokaJsonViewer(_ => { });
    }

    /// <summary>
    ///     Adds the Moka JSON viewer services to the service collection with the specified options.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">An action to configure the <see cref="MokaJsonViewerOptions" />.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddMokaJsonViewer(
        this IServiceCollection services,
        Action<MokaJsonViewerOptions> configure)
    {
        services.Configure(configure);
        services.AddTransient<JsonDocumentManager>();
        services.AddTransient<JsonTreeFlattener>();
        services.AddTransient<JsonSearchEngine>();
        services.AddScoped<MokaJsonInterop>();
        return services;
    }
}