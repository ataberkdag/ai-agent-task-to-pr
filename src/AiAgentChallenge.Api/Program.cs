using AiAgentChallenge.Api.Abstractions;
using AiAgentChallenge.Api.Services;
using AiAgentChallenge.Infrastructure;
using AiAgentChallenge.Infrastructure.Paths;
using Serilog;
using Serilog.Events;

var builder = WebApplication.CreateBuilder(args);
var logsDirectory = ApplicationPathResolver.ResolveAgainstApplicationBase(
    builder.Configuration["Serilog:FilePath"],
    "logs");
Directory.CreateDirectory(logsDirectory);

builder.Host.UseSerilog((context, services, loggerConfiguration) => loggerConfiguration
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .WriteTo.File(
        Path.Combine(logsDirectory, "app-.log"),
        rollingInterval: RollingInterval.Day,
        restrictedToMinimumLevel: LogEventLevel.Information,
        shared: true));

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddSingleton<ITraceIdAccessor, HttpContextTraceIdAccessor>();
builder.Services.AddInfrastructure(builder.Configuration);

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();

public partial class Program;
