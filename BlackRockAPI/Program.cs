// # docker build -t blk-hacking-ind-{name-lastname} .
using BlackrockChallenge.Models;
using BlackrockChallenge.Services;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<FinancialService>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "BlackRock Auto-Savings API", Version = "v1" });
});

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
});

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();


app.MapGet("/health", () => new { status = "ok", service = "BlackRock Auto-Savings API" });


app.MapPost("/blackrock/challenge/v1/transactions:parse", (List<ExpenseRequest> expenses, FinancialService svc) =>
{
    return Results.Ok(svc.ParseTransactions(expenses));
});


app.MapPost("/blackrock/challenge/v1/transactions:validator", (ValidatorRequest req, FinancialService svc) =>
{
    return Results.Ok(svc.ValidateTransactions(req));
});


app.MapPost("/blackrock/challenge/v1/transactions:filter", (FilterRequest req, FinancialService svc) =>
{
    return Results.Ok(svc.FilterTransactions(req));
});

app.MapPost("/blackrock/challenge/v1/returns:nps", (ReturnsRequest req, FinancialService svc) =>
{
    return Results.Ok(svc.CalculateReturns(req, InvestmentType.NPS));
});


app.MapPost("/blackrock/challenge/v1/returns:index", (ReturnsRequest req, FinancialService svc) =>
{
    return Results.Ok(svc.CalculateReturns(req, InvestmentType.Index));
});


app.MapGet("/blackrock/challenge/v1/performance", () =>
{
    var process = Process.GetCurrentProcess();
    var memMb = process.WorkingSet64 / (1024.0 * 1024.0);
    var uptime = DateTime.UtcNow - process.StartTime.ToUniversalTime();
    var timeStr = uptime.ToString(@"hh\:mm\:ss\.fff");

    return Results.Ok(new
    {
        time = timeStr,
        memory = $"{memMb:F2} MB",
        threads = process.Threads.Count
    });
});

app.Run("http://0.0.0.0:5477");