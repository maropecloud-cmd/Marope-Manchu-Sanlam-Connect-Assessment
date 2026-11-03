using System.Text.Json.Serialization;
using IHub.Api.Features.Activities;
using IHub.Api.Observability;
using IHub.Api.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddScoped<CorrelationContext>();
builder.Services.AddSingleton<HubMetrics>();

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddPersistence();
builder.Services.AddActivityIngestion();

builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

var app = builder.Build();

app.MapActivityEndpoints();

await app.RunAsync();

public partial class Program;
