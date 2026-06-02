using _036_AI_Pair_Programmer.Components;
using _036_AI_Pair_Programmer.Services;
using Microsoft.AspNetCore.Components;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddJsonFile("appsettings.local.json", optional: true, reloadOnChange: true);

// Add services to the container.
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

builder.Services.AddHttpClient("qdrant", c =>
{
    c.Timeout = TimeSpan.FromSeconds(120);
});

builder.Services.AddSingleton<CodeIndexStoreService>();
builder.Services.AddSingleton<CodeChunkerService>();
builder.Services.AddSingleton<IndexingJobService>();
builder.Services.AddTransient<QdrantVectorStoreService>();
builder.Services.AddTransient<LlmService>();
builder.Services.AddTransient<CodeIndexingWorkflowService>();
builder.Services.AddTransient<PairProgrammingWorkflowService>();
builder.Services.AddHostedService<BackgroundIndexingService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapControllers();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
