using ASCII_Hero.Client;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

#if STANDALONE_BUILD
// Only registered for the standalone GitHub Pages build (see StandaloneBuild MSBuild
// property in the .csproj and the deploy-gh-pages.yml workflow). When hosted inside the
// ASCII Hero server project, root components are instead declared via @rendermode in
// App.razor/Routes.razor - registering them here too would have WASM try (and fail) to
// mount into a #app element that only exists in this project's standalone index.html.
builder.RootComponents.Add<Routes>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");
#endif

// Used to fetch asset files (sprites, levels) served as static web content under wwwroot/Assets;
// Blazor WebAssembly has no direct filesystem access, so asset loading goes over HTTP.
builder.Services.AddScoped(_ => new HttpClient
{
    BaseAddress = new Uri(builder.HostEnvironment.BaseAddress),
});

await builder.Build().RunAsync();
