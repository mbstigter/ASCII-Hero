using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

// Used to fetch asset files (sprites, levels) served as static web content under wwwroot/Assets;
// Blazor WebAssembly has no direct filesystem access, so asset loading goes over HTTP.
builder.Services.AddScoped(_ => new HttpClient
{
    BaseAddress = new Uri(builder.HostEnvironment.BaseAddress),
});

await builder.Build().RunAsync();
