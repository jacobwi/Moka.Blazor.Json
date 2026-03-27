using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moka.Blazor.AI.Extensions;
using Moka.Blazor.AI.Models;
using Moka.Blazor.AI.Services;
using Moka.Blazor.Json.AI.Services;

#pragma warning disable CA1062 // Validate arguments of public methods

namespace Moka.Blazor.Json.AI.Extensions;

/// <summary>
///     Extension methods for registering the Moka JSON AI assistant services.
/// </summary>
public static class ServiceCollectionExtensions
{
	/// <summary>
	///     Adds the Moka JSON AI assistant with default configuration (LM Studio at localhost:1234).
	/// </summary>
	public static IServiceCollection AddMokaJsonAi(this IServiceCollection services) =>
		services.AddMokaJsonAi(_ => { });

	/// <summary>
	///     Adds the Moka JSON AI assistant with custom configuration.
	/// </summary>
	public static IServiceCollection AddMokaJsonAi(
		this IServiceCollection services,
		Action<AiChatOptions> configure)
	{
		// Register base AI services (IChatClient, AiChatService, etc.)
		services.AddMokaAi(configure);

		// Register JSON-specific services (Scoped so the same instance is shared
		// between MokaJsonAiPanel and AiChatService within a request)
		services.TryAddScoped<JsonContextBuilder>();
		services.TryAddScoped<JsonAiService>();

		// Register the JSON context builder as the IAiContextBuilder for the base library
		services.TryAddScoped<IAiContextBuilder>(sp => sp.GetRequiredService<JsonContextBuilder>());

		return services;
	}

	/// <summary>
	///     Adds the Moka JSON AI assistant with a custom <see cref="Microsoft.Extensions.AI.IChatClient" />.
	///     Use this to bring your own provider.
	/// </summary>
	public static IServiceCollection AddMokaJsonAi(
		this IServiceCollection services,
		IChatClient chatClient,
		Action<AiChatOptions>? configure = null)
	{
		// Register base AI services with custom chat client
		services.AddMokaAi(chatClient, configure);

		// Register JSON-specific services (Scoped so the same instance is shared)
		services.TryAddScoped<JsonContextBuilder>();
		services.TryAddScoped<JsonAiService>();

		// Register the JSON context builder as the IAiContextBuilder for the base library
		services.TryAddScoped<IAiContextBuilder>(sp => sp.GetRequiredService<JsonContextBuilder>());

		return services;
	}
}
