using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Mikeka.Api.Bookmaker;
using Mikeka.Api.Data;
using Mikeka.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
    .WithOrigins(builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? ["http://localhost:5173"])
    .AllowAnyHeader().AllowAnyMethod()));

builder.Services.AddDbContext<MikekaDb>(o => o.UseNpgsql(builder.Configuration.GetConnectionString("Mikeka")));

// Key for encrypting stored bookmaker passwords comes from config ("Encryption:Key").
builder.Services.Configure<EncryptionOptions>(builder.Configuration.GetSection("Encryption"));
builder.Services.Configure<BettingRules>(builder.Configuration.GetSection("Betting"));
builder.Services.Configure<EmailOptions>(builder.Configuration.GetSection("Email"));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<AccountSecrets>();
builder.Services.AddSingleton<INotifier, EmailService>();
builder.Services.AddScoped<ExcelLog>();
builder.Services.AddScoped<BettingEngine>();
builder.Services.AddScoped<SlipAnalysis>();
builder.Services.AddScoped<SettingsService>();

builder.Services.Configure<ColdbetOptions>(builder.Configuration.GetSection("Coldbet"));
builder.Services.Configure<OneWinOptions>(builder.Configuration.GetSection("OneWin"));
builder.Services.Configure<BrowserOptions>(builder.Configuration.GetSection("Browser"));
builder.Services.Configure<AnthropicOptions>(builder.Configuration.GetSection("Anthropic"));
builder.Services.AddSingleton<SharedBrowser>(); // one Chrome window for all runs, a tab per run
builder.Services.AddSingleton<ScreenshotOddsReader>();
builder.Services.AddSingleton<ColdbetFactory>();
builder.Services.AddSingleton<OneWinFactory>();
builder.Services.Configure<LeonbetOptions>(builder.Configuration.GetSection("Leonbet"));
builder.Services.AddSingleton<LeonbetFactory>();
// "Mock" = pretend site for all accounts (testing); otherwise each account uses its own site (Account.Site).
var bookmaker = builder.Configuration["Betting:Bookmaker"] ?? "Mock";
if (bookmaker.Equals("Mock", StringComparison.OrdinalIgnoreCase))
    builder.Services.AddSingleton<IBookmakerFactory, MockBookmakerFactory>();
else
    builder.Services.AddSingleton<IBookmakerFactory, SiteBookmakerFactory>();

if (builder.Configuration.GetValue("Betting:SchedulerEnabled", true))
    builder.Services.AddHostedService<DailyScheduler>();

var app = builder.Build();

app.Services.GetRequiredService<AccountSecrets>(); // fail fast if Encryption:Key is missing or invalid

using (var scope = app.Services.CreateScope())
{
    await scope.ServiceProvider.GetRequiredService<MikekaDb>().Database.MigrateAsync();
    await scope.ServiceProvider.GetRequiredService<SettingsService>().ApplyAsync(default); // odds settings from the database
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors();

// Serve the built React app (frontend/dist) from the same address as the API.
var webRoot = Path.GetFullPath(builder.Configuration["WebAppPath"] ?? Path.Combine(app.Environment.ContentRootPath, "..", "..", "frontend", "dist"));
if (Directory.Exists(webRoot))
{
    var files = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(webRoot);
    app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = files });
    app.UseStaticFiles(new StaticFileOptions { FileProvider = files });
    app.MapFallbackToFile("index.html", new StaticFileOptions { FileProvider = files });
}

app.MapControllers();
app.Run();

public partial class Program;
