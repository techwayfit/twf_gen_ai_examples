using _032_TextbookChapterQuestionGenerator.Components;
using _032_TextbookChapterQuestionGenerator.Services;
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

// HttpClient used by Blazor components to call the local API.
builder.Services.AddScoped(sp => new HttpClient
{
    BaseAddress = new Uri(sp.GetRequiredService<NavigationManager>().BaseUri)
});

// Named HttpClient for OpenAI calls (120 s timeout for parallel generation).
builder.Services.AddHttpClient("openai", c =>
{
    c.Timeout = TimeSpan.FromSeconds(120);
});

// Domain services
builder.Services.AddSingleton<PdfOcrCacheService>();   // Singleton: owns the SQLite connection
builder.Services.AddTransient<LlmService>();
builder.Services.AddTransient<ChapterTextExtractorService>();
builder.Services.AddTransient<QuestionGenerationWorkflowService>();

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
