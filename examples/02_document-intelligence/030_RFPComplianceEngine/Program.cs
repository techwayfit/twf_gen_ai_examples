using _030_RFPComplianceEngine.Components;
using _030_RFPComplianceEngine.Services;
using Microsoft.AspNetCore.Components;
using Qdrant.Client;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel((ctx, k) =>
{
    k.AllowSynchronousIO = true;
    var maxMb = ctx.Configuration.GetValue<long>("Upload:MaxFileSizeMb", 200);
    k.Limits.MaxRequestBodySize = maxMb * 1024 * 1024;
});

builder.Configuration.AddJsonFile("appsettings.local.json", optional: true, reloadOnChange: true);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddControllers();

builder.Services.AddScoped(sp => new HttpClient
{
    BaseAddress = new Uri(sp.GetRequiredService<NavigationManager>().BaseUri)
});

builder.Services.AddHttpClient("openai", c =>
{
    c.Timeout = TimeSpan.FromSeconds(120);
});

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

builder.Services.AddSingleton<QdrantVectorStoreService>();

builder.Services.AddTransient<ChunkingService>();
builder.Services.AddTransient<FileTextExtractorService>();
builder.Services.AddTransient<EmbeddingService>();
builder.Services.AddTransient<LlmService>();
builder.Services.AddTransient<IngestWorkflowService>();
builder.Services.AddTransient<RfpComplianceWorkflowService>();
builder.Services.AddTransient<ContractQueryWorkflowService>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseAntiforgery();
app.MapStaticAssets();
app.MapControllers();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

var qdrant = app.Services.GetRequiredService<QdrantVectorStoreService>();
await qdrant.EnsureCollectionExistsAsync(builder.Configuration["Qdrant:Collections:Capabilities"] ?? "capabilities");
await qdrant.EnsureCollectionExistsAsync(builder.Configuration["Qdrant:Collections:Policies"] ?? "policies");
await qdrant.EnsureCollectionExistsAsync(builder.Configuration["Qdrant:Collections:Regulations"] ?? "regulations");
await qdrant.EnsureCollectionExistsAsync(builder.Configuration["Qdrant:Collections:Contracts"] ?? "contracts");

app.Run();
