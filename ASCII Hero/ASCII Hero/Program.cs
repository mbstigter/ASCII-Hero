using System.Diagnostics;
using ASCII_Hero.Client.Pages;
using ASCII_Hero.Components;
using Microsoft.AspNetCore.Components;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveWebAssemblyComponents();

// Registered so components (e.g. Home.razor's asset loader) also resolve HttpClient during
// server-side prerendering, before the WebAssembly runtime (and its own HttpClient
// registration in the Client project) takes over. BaseAddress is derived from the current
// request via NavigationManager, matching the standard Blazor Web App prerendering pattern.
builder.Services.AddScoped(sp =>
{
    var navigationManager = sp.GetRequiredService<NavigationManager>();
    return new HttpClient { BaseAddress = new Uri(navigationManager.BaseUri) };
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseWebAssemblyDebugging();
}
else
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof(ASCII_Hero.Client._Imports).Assembly);

// Launches Edge in chromeless "app mode" (no address bar) once the server starts, so the
// game canvas has focus immediately instead of competing with an address bar for it.
// Handled here instead of launchSettings.json's own browser-launch support because Visual
// Studio appends its own launch URL as an extra argument after commandLineArgs, which makes
// Edge open a normal tab alongside the app-mode window.
if (app.Environment.IsDevelopment())
{
    app.Lifetime.ApplicationStarted.Register(() =>
    {
        var url = app.Urls.FirstOrDefault(u => u.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            ?? app.Urls.First();
        LaunchEdgeAppMode(url);
    });
}

app.Run();

static void LaunchEdgeAppMode(string url)
{
    string[] candidatePaths =
    [
        @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
        @"C:\Program Files\Microsoft\Edge\Application\msedge.exe",
    ];
    var edgePath = candidatePaths.FirstOrDefault(File.Exists);
    if (edgePath is null)
    {
        return;
    }

    // Sized to fit the 1280x700 game canvas (see Home.razor) plus room for the
    // font-toggle buttons above it and Edge's app-mode window chrome.
    const int windowWidth = 1320;
    const int windowHeight = 800;

    // A dedicated user-data-dir forces Edge to start a genuinely new, isolated browser
    // process instead of just handing the URL off to any already-running Edge instance -
    // command-line flags like --window-size are silently ignored when handed off, since
    // they only apply when a new process actually starts.
    var userDataDir = Path.Combine(Path.GetTempPath(), "AsciiHeroEdgeAppMode");
    Process.Start(new ProcessStartInfo(edgePath, $"--app={url} --window-size={windowWidth},{windowHeight} --user-data-dir=\"{userDataDir}\"") { UseShellExecute = true });
}
