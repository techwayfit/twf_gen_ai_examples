using _003_Automated_NewsLetter.Components;
using _003_Automated_NewsLetter.Services;
using Microsoft.AspNetCore.Components;

var builder = WebApplication.CreateBuilder(args);

// Register the web logger before building so it receives log entries from all services.
var webLoggerService = new WebLoggerService();
builder.Services.AddSingleton(webLoggerService);
builder.Logging.AddProvider(webLoggerService);

// Required so the Action<string> OnChunk callback from LlmNode can write
// synchronously to the SSE response body on the Kestrel thread.
builder.WebHost.ConfigureKestrel(k => k.AllowSynchronousIO = true);

// Add appsettings.local.json to configuration (loaded last to override other settings)
builder.Configuration.AddJsonFile("appsettings.local.json", optional: true, reloadOnChange: true);

// Add services to the container
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Add API controllers support
builder.Services.AddControllers();

// Add HttpClient for Blazor components and services
builder.Services.AddScoped(sp => new HttpClient
{
    BaseAddress = new Uri(sp.GetRequiredService<NavigationManager>().BaseUri)
});

// Register a named HttpClient for feed fetching and content enrichment
builder.Services.AddHttpClient("feeds", c =>
{
    c.DefaultRequestHeaders.Add("User-Agent", "TechWayFit-NewsletterBot/1.0");
    c.Timeout = TimeSpan.FromSeconds(15);
});

// Register application services
builder.Services.AddSingleton<SubscriberProfileService>();
builder.Services.AddSingleton<FeedConfigService>();

builder.Services.AddTransient<RssFeedService>(sp =>
    new RssFeedService(
        sp.GetRequiredService<IHttpClientFactory>().CreateClient("feeds"),
        sp.GetRequiredService<ILogger<RssFeedService>>()));

builder.Services.AddTransient<ContentEnrichmentService>(sp =>
    new ContentEnrichmentService(
        sp.GetRequiredService<IHttpClientFactory>().CreateClient("feeds"),
        sp.GetRequiredService<ILogger<ContentEnrichmentService>>()));

builder.Services.AddScoped<NewsletterWorkflowService>();

// Register the background scheduler
builder.Services.AddHostedService<SchedulerService>();

var app = builder.Build();

// Configure the HTTP request pipeline
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

app.Run();
