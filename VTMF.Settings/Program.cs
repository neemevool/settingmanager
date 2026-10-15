using Microsoft.AspNetCore.DataProtection;
using VTMF.Settings.Components;
using VTMF.Settings.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddEnvironmentVariables();
// add user secrets in development
if (builder.Environment.IsDevelopment())
{
    builder.Configuration.AddUserSecrets<Program>();
}

builder.Services.AddSettingsServices();

// Add services to the container.
builder.Services.AddRazorComponents()
.AddInteractiveServerComponents();

builder.Services.AddBlazorBootstrap();

// Configure data protection to persist keys to a shared directory
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo("/app/keys"))
    .SetApplicationName("VTMF-Settings");

// Register the settings service
// TODO: Replace MockSettingsService with your actual implementation
//builder.Services.AddSingleton<ISettingsService, MockSettingsService>();

var app = builder.Build();


// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapGet("/wiki/image/{id:int}", async (int id, IWikiImageService svc, HttpContext httpContext, CancellationToken ct) =>
{
    var image = await svc.GetByIdAsync(id, ct);
    if (image is null)
        return Results.NotFound();

    httpContext.Response.Headers.CacheControl = "public, max-age=3600";
    return Results.File(image.Content, image.MimeType, image.Filename);
});

app.Run();
