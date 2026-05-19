using MediaToolsNext.Web.Components;
using MediaToolsNext.Desktop;
using MediaToolsNext.Core;
using MediaToolsNext.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Register desktop-compatible services
builder.Services.AddSingleton<FolderPickerService>();
builder.Services.AddSingleton<ToolInstallService>();
builder.Services.AddSingleton<ScanWorkflowState>();
builder.Services.AddMediaToolsNext(_ =>
    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "media-tools-next", "media-tools-next-web.db"));

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
