using IHub.Api.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddPersistence();

var app = builder.Build();

await app.RunAsync();

public partial class Program;
