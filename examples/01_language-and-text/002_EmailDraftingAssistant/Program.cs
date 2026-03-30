using _002_EmailDraftingAssistant.Components;
using _002_EmailDraftingAssistant.Services;
using Microsoft.AspNetCore.Components;

var builder = WebApplication.CreateBuilder(args);

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

// Register email services (CSV loaded once at startup, kept in memory)
builder.Services.AddSingleton<EmailAnalysisService>(_ => new EmailAnalysisService(new HttpClient()));
builder.Services.AddSingleton<EmailService>(sp =>
{
    var csvPath = Path.Combine(builder.Environment.ContentRootPath, "Data", "techwayfit_emails.csv");
    return new EmailService(csvPath, sp.GetRequiredService<EmailAnalysisService>());
});

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
