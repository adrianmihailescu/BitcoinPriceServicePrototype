using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using BitcoinPriceService.Data;
using BitcoinPriceService.Services;
using Microsoft.OpenApi.Models;
using Serilog;

var logFilePath = "Logs/api.log";  // Define log file path

// Configure Serilog for file logging
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .WriteTo.File(logFilePath, rollingInterval: RollingInterval.Day)
    .CreateLogger();
var builder = WebApplication.CreateBuilder(args);

// Ensure the "Logs" directory exists
if (!Directory.Exists("Logs"))
{
    Directory.CreateDirectory("Logs");
}

// Add Serilog to logging system
builder.Logging.ClearProviders();
builder.Logging.AddSerilog(Log.Logger);

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite("Data Source=bitcoinprices.db"));

builder.Services.AddMemoryCache();
builder.Services.AddScoped<BitcoinPriceAggregatorService>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Bitcoin Price Aggregator API",
        Version = "v1",
        Description = "A prototype API to fetch, aggregate, and cache Bitcoin prices from external sources (Bitstamp & Bitfinex).",
        Contact = new OpenApiContact
        {
            Name = "Support Team",
            Email = "support@BitcoinFetch.com",
            Url = new Uri("https://BitcoinFetch.com/contact")
        }
    });
});
builder.Services.AddLogging();

var app = builder.Build();

// Middleware to log each API request
app.Use(async (context, next) =>
{
    var requestTime = DateTime.UtcNow;
    var requestPath = context.Request.Path;
    var method = context.Request.Method;

    Log.Information("Incoming Request: {Method} {Path} at {RequestTime}", method, requestPath, requestTime);

    await next(); // Call the next middleware

    var responseTime = DateTime.UtcNow;
    var statusCode = context.Response.StatusCode;
    Log.Information("Response: {Method} {Path} - Status: {StatusCode} at {ResponseTime}",
        method, requestPath, statusCode, responseTime);
});

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "Bitcoin Price Aggregator API");
        options.DocumentTitle = "Bitcoin Price API Docs";
    });
}

app.UseHttpsRedirection();

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    dbContext.Database.EnsureCreated(); // Creates the database if it doesn't exist
}

app.MapGet("/api/price/{timestamp}", async (DateTime timestamp, BitcoinPriceAggregatorService service) =>
{
    Log.Information("Fetching Bitcoin price for timestamp: {Timestamp}", timestamp);
    var price = await service.GetAggregatedPriceAsync(timestamp);

    if (price != null)
    {
        Log.Information("Returning price: {Price} for timestamp {Timestamp}", price, timestamp);
        return Results.Ok(price);
    }
    else
    {
        Log.Warning("Price data not found for timestamp: {Timestamp}", timestamp);
        return Results.NotFound("Price data unavailable.");
    }

    return price != null ? Results.Ok(price) : Results.NotFound("Price data unavailable.");
}).WithName("GetAggregatedPrice");

app.Run();
