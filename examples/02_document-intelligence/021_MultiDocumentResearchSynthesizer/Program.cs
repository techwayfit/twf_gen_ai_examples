using _021_MultiDocumentResearchSynthesizer.Components;
using _021_MultiDocumentResearchSynthesizer.Services;
using Microsoft.AspNetCore.Components;
using Qdrant.Client;

var builder = WebApplication.CreateBuilder(args);

// Required so the synchronous SSE writes work on the Kestrel thread.
// MaxRequestBodySize is raised to match the configured upload limit.
builder.WebHost.ConfigureKestrel((ctx, k) =>
{
    k.AllowSynchronousIO = true;
    var maxMb = ctx.Configuration.GetValue<long>("Upload:MaxFileSizeMb", 200);
    k.Limits.MaxRequestBodySize = maxMb * 1024 * 1024;
});

// Add appsettings.local.json to configuration (loaded last to override other settings)
builder.Configuration.AddJsonFile("appsettings.local.json", optional: true, reloadOnChange: true);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Add API controllers support
builder.Services.AddControllers();

// Add HttpClient for Blazor components
builder.Services.AddScoped(sp => new HttpClient
{
    BaseAddress = new Uri(sp.GetRequiredService<NavigationManager>().BaseUri)
});

// Named HttpClient for embedding and OpenAI calls (60s timeout)
builder.Services.AddHttpClient("openai", c =>
{
    c.Timeout = TimeSpan.FromSeconds(60);
});

// Qdrant client — reads host/port/https/apiKey from configuration
builder.Services.AddSingleton(sp =>
{
    var cfg    = sp.GetRequiredService<IConfiguration>();
    var host   = cfg["Qdrant:Host"]  ?? "localhost";
    var port   = int.Parse(cfg["Qdrant:Port"]  ?? "6334");
    var https  = bool.Parse(cfg["Qdrant:Https"] ?? "false");
    var apiKey = cfg["Qdrant:ApiKey"];
    return string.IsNullOrWhiteSpace(apiKey)
        ? new QdrantClient(host, port, https)
        : new QdrantClient(host, port, https, apiKey);
});

// Singleton Qdrant vector store — persists across requests
builder.Services.AddSingleton<QdrantVectorStoreService>();

// Transient per-request services
builder.Services.AddTransient<ChunkingService>();
builder.Services.AddTransient<FileTextExtractorService>();
builder.Services.AddTransient<EmbeddingService>();
builder.Services.AddTransient<QueryWorkflowService>();
builder.Services.AddTransient<IngestWorkflowService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();

// Map API controllers
app.MapControllers();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// Ensure the default Qdrant collection exists before accepting traffic
var qdrant = app.Services.GetRequiredService<QdrantVectorStoreService>();
await qdrant.EnsureCollectionExistsAsync();

app.Run();
