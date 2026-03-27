using Moka.Blazor.Json.AI.Extensions;
using Moka.Blazor.Json.Demo.Components;
using Moka.Blazor.Json.Extensions;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
	.AddInteractiveServerComponents();

builder.Services.AddMokaJsonViewer(options =>
{
	options.DefaultExpandDepth = 2;
	options.EnableEditMode = true;
	options.MaxDocumentSizeBytes = 1024L * 1024 * 1024; // 1 GB for stress testing
});

builder.Services.AddMokaJsonAi(options =>
{
	// LM Studio at localhost:1234 (default) — uses whatever model is loaded
	// For Ollama: options.Provider = AiProvider.Ollama; options.DefaultModel = "llama3.2";
});

WebApplication app = builder.Build();

if (!app.Environment.IsDevelopment())
{
	app.UseExceptionHandler("/Error");
	app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
	.AddInteractiveServerRenderMode();

app.Run();
