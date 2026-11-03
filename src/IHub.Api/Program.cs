using System.Text.Json.Serialization;
using IHub.Api;
using IHub.Api.Features.Activities;
using IHub.Api.Features.Statements;
using IHub.Api.Observability;
using IHub.Api.Persistence;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddObservability();

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddPersistence();
builder.Services.AddActivityIngestion();
builder.Services.AddStatements();

builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddOpenApi();

var app = builder.Build();

app.UseObservability();

if (app.Configuration.GetSection(ApiOptions.SectionName).Get<ApiOptions>()?.EnableApiReference == true)
{
    app.MapOpenApi();
    app.MapScalarApiReference(options => options.WithTitle("IHub Activity Statement API"));
    app.MapGet("/", () => Results.Redirect("/scalar")).ExcludeFromDescription();
}

app.MapActivityEndpoints();
app.MapStatementEndpoints();
app.MapOperationalEndpoints();

await app.RunAsync();

public partial class Program;
