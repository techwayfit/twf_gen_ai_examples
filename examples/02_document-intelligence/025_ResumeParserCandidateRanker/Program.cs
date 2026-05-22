using _025_ResumeParserCandidateRanker.Components;
using _025_ResumeParserCandidateRanker.Services;
using Microsoft.AspNetCore.Components;

var builder = WebApplication.CreateBuilder(args);

// Allow synchronous SSE writes; raise max request body for resume uploads.
builder.WebHost.ConfigureKestrel((ctx, k) =>
{
    k.AllowSynchronousIO = true;
    var maxMb = ctx.Configuration.GetValue<long>("Upload:MaxFileSizeMb", 10);
    k.Limits.MaxRequestBodySize = maxMb * 1024 * 1024;
});

// Load appsettings.local.json last so it overrides defaults.
builder.Configuration.AddJsonFile("appsettings.local.json", optional: true, reloadOnChange: true);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddControllers();

// HttpClient used by Blazor components to call the local API.
builder.Services.AddScoped(sp => new HttpClient
{
    BaseAddress = new Uri(sp.GetRequiredService<NavigationManager>().BaseUri)
});

// Named HttpClient for OpenAI calls (120 s timeout to cover large batches).
builder.Services.AddHttpClient("openai", c =>
{
    c.Timeout = TimeSpan.FromSeconds(120);
});

// Domain services
builder.Services.AddTransient<EmbeddingService>();
builder.Services.AddTransient<FileTextExtractorService>();
builder.Services.AddTransient<SimilarityService>();
builder.Services.AddTransient<LlmService>();
builder.Services.AddTransient<RankingWorkflowService>();
builder.Services.AddTransient<ResumeRewriteService>();

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

app.Run();
