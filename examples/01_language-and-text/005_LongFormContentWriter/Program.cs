using _005_LongFormContentWriter.Components;
using _005_LongFormContentWriter.Services;
using Microsoft.AspNetCore.Components;

var builder = WebApplication.CreateBuilder(args);

// Required so the Action<string> OnChunk callback from LlmNode can write
// synchronously to the SSE response body on the Kestrel thread.
builder.WebHost.ConfigureKestrel(k => k.AllowSynchronousIO = true);

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

// Register a named HttpClient for the search API
builder.Services.AddHttpClient("search", c =>
{
    c.Timeout = TimeSpan.FromSeconds(15);
});

// Register application services
builder.Services.AddTransient<ContentWorkflowService>();

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

app.Run();
