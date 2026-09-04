using ASCII_Hero.Client;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

// Registered so the Client project can also run standalone (e.g. the GitHub Pages build),
// not just hosted inside the ASCII Hero server project's App.razor.
builder.RootComponents.Add<Routes>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// Used to fetch asset files (sprites, levels) served as static web content under wwwroot/Assets;
// Blazor WebAssembly has no direct filesystem access, so asset loading goes over HTTP.
builder.Services.AddScoped(_ => new HttpClient
{
    BaseAddress = new Uri(builder.HostEnvironment.BaseAddress),
});

await builder.Build().RunAsync();
