using CoverPlayerMP.Web.Components;
using CoverPlayerMP.Shared.Player;
using CoverPlayerMP.Web.Player;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddScoped<CoverPlayerMP.Shared.Localization.AppText>();
builder.Services.AddScoped<IPlaylistPlayer, WebPlaylistPlayer>();
builder.Services.AddScoped<CoverPlayerMP.Shared.Player.CoverSearch>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseAntiforgery();

app.MapStaticAssets();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddAdditionalAssemblies(
        typeof(CoverPlayerMP.Shared._Imports).Assembly);

app.Run();
