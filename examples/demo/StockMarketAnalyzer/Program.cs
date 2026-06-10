using _StockMarketAnalyzer.Components;
using _StockMarketAnalyzer.Services;
using Microsoft.AspNetCore.Components;

var builder = WebApplication.CreateBuilder(args);

// Allow synchronous SSE writes.
builder.WebHost.ConfigureKestrel((_, k) =>
{
    k.AllowSynchronousIO = true;
});

// Load appsettings.local.json last so it overrides defaults.
builder.Configuration.AddJsonFile("appsettings.local.json", optional: true, reloadOnChange: true);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddControllers();

// Named HttpClient for OpenAI calls (120 s timeout for LLM calls).
builder.Services.AddHttpClient("openai", c =>
{
    c.Timeout = TimeSpan.FromSeconds(120);
});

// Named HttpClient for Yahoo Finance with User-Agent header.
builder.Services.AddHttpClient("yahoo", c =>
{
    c.Timeout = TimeSpan.FromSeconds(15);
    c.DefaultRequestHeaders.Add("User-Agent",
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/125.0.0.0 Safari/537.36");
});

// Scoped HttpClient for Blazor components (components build full URLs via Navigation.BaseUri).
builder.Services.AddScoped(sp => new HttpClient());

// Domain services
builder.Services.AddTransient<LlmService>();
builder.Services.AddTransient<YahooFinanceService>();
builder.Services.AddTransient<AnalysisWorkflowService>();
builder.Services.AddSingleton<WatchlistService>();

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
