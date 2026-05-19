using MediaToolsNext.Web.Components;
using MediaToolsNext.Desktop;
using MediaToolsNext.Core;
using MediaToolsNext.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// Restore the original working directory if launched via run-web.ps1
var initCwd = Environment.GetEnvironmentVariable("INIT_CWD");
if (!string.IsNullOrEmpty(initCwd) && Directory.Exists(initCwd))
{
    Environment.CurrentDirectory = initCwd;
}

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

// In development, explicitly serve the Desktop's wwwroot since linked files 
// can be flaky with dotnet watch and MapStaticAssets.
if (app.Environment.IsDevelopment())
{
    var currentDir = new DirectoryInfo(builder.Environment.ContentRootPath);
    while (currentDir != null && currentDir.Name != "src" && currentDir.Name != "media-tools-next")
    {
        currentDir = currentDir.Parent;
    }
    
    if (currentDir != null)
    {
        // currentDir is either 'src' or the repo root
        var srcDir = currentDir.Name == "src" ? currentDir.FullName : Path.Combine(currentDir.FullName, "src");
        var desktopWwwRoot = Path.Combine(srcDir, "MediaToolsNext.Desktop", "wwwroot");
        
        Console.WriteLine($"[DEBUG] Resolved desktopWwwRoot: {desktopWwwRoot}");
        if (Directory.Exists(desktopWwwRoot))
        {
            app.UseStaticFiles(new StaticFileOptions
            {
                FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(desktopWwwRoot),
                RequestPath = ""
            });
        }
    }
}

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
