using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using Tracker.Api.Data;
using Tracker.Api.Endpoints;
using Tracker.Api.Services;
using Tracker.Api.Services.Ai;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<TrackerDb>(o =>
    o.UseNpgsql(builder.Configuration.GetConnectionString("Tracker")
                ?? "Host=localhost;Port=5432;Database=tracker;Username=tracker;Password=tracker"));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<SettingsService>();
builder.Services.AddScoped<ImportService>();
builder.Services.AddScoped<PackageService>();
builder.Services.AddScoped<TestRunner>();
builder.Services.AddScoped<AttemptService>();
builder.Services.AddScoped<QueueService>();
builder.Services.AddScoped<StatsService>();
builder.Services.AddScoped<AiService>();
builder.Services.AddScoped<IAiProvider, AnthropicProvider>();
builder.Services.AddScoped<IAiProvider, DeepSeekProvider>();
builder.Services.AddScoped<IAiProvider, ClaudeCodeProvider>();
builder.Services.AddHttpClient("ai", c => c.Timeout = TimeSpan.FromMinutes(5));
builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
});
builder.Services.Configure<FormOptions>(o => o.MultipartBodyLengthLimit = 100L * 1024 * 1024);
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = 100L * 1024 * 1024);

var app = builder.Build();

// Schema only — problems are never seeded by migrations, they come from an import.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<TrackerDb>();
    for (var i = 0; ; i++)
    {
        try { await db.Database.MigrateAsync(); break; }
        catch (Exception ex) when (i < 15)
        {
            app.Logger.LogWarning("Database not ready ({Message}); retrying…", ex.Message);
            await Task.Delay(2000);
        }
    }
}

app.UseExceptionHandler(e => e.Run(async ctx =>
{
    var ex = ctx.Features.Get<IExceptionHandlerFeature>()?.Error;
    var (status, details) = ex switch
    {
        NotFoundException => (404, (object?)null),
        ConflictException c => (409, c.Details),
        BadRequestException or UploadException => (400, null),
        AiException => (502, null),
        _ => (500, null)
    };
    if (status == 500) app.Logger.LogError(ex, "Unhandled error");
    ctx.Response.StatusCode = status;
    await ctx.Response.WriteAsJsonAsync(new { error = status == 500 ? "Unexpected server error: " + ex?.Message : ex?.Message, details });
}));

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapTrackerApi();
app.MapFallbackToFile("index.html");

app.Run();

public partial class Program;
