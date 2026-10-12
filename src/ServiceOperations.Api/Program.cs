using System.Text.Json.Serialization;
using ServiceOperations.Api.Contracts;
using ServiceOperations.Api.Data;
using ServiceOperations.Api.Domain;
using ServiceOperations.Api.Middleware;
using ServiceOperations.Api.Validation;

var builder = WebApplication.CreateBuilder(args);
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

var dataMode = builder.Configuration["Data:Mode"] ?? "Demo";
if (dataMode.Equals("SqlServer", StringComparison.OrdinalIgnoreCase))
{
    if (string.IsNullOrWhiteSpace(builder.Configuration.GetConnectionString("ServiceOperations")))
        throw new InvalidOperationException("ConnectionStrings:ServiceOperations is required in SqlServer mode.");
    builder.Services.AddScoped<IServiceOperationsRepository, SqlServiceOperationsRepository>();
}
else if (dataMode.Equals("Demo", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddSingleton<IServiceOperationsRepository, InMemoryServiceOperationsRepository>();
}
else
{
    throw new InvalidOperationException("Data:Mode must be Demo or SqlServer.");
}

var app = builder.Build();
app.UseMiddleware<ExceptionHandlingMiddleware>();

app.MapGet("/health", () => Results.Ok(new { status = "healthy", dataMode }));

var services = app.MapGroup("/api/services");
services.MapGet("/", async (IServiceOperationsRepository repository, CancellationToken ct) =>
    Results.Ok(await repository.ListServicesAsync(ct)));

services.MapGet("/{id:int}", async (int id, IServiceOperationsRepository repository, CancellationToken ct) =>
    await repository.GetServiceAsync(id, ct) is { } service ? Results.Ok(service) : Results.NotFound());

services.MapPost("/", async (CreateServiceRequest request, IServiceOperationsRepository repository, CancellationToken ct) =>
{
    RequestValidation.Validate(request);
    var service = await repository.CreateServiceAsync(request, ct);
    return Results.Created($"/api/services/{service.Id}", service);
});

services.MapPut("/{id:int}", async (
    int id, UpdateServiceRequest request, IServiceOperationsRepository repository, CancellationToken ct) =>
{
    RequestValidation.Validate(request);
    return await repository.UpdateServiceAsync(id, request, ct) is { } service
        ? Results.Ok(service) : Results.NotFound();
});

services.MapDelete("/{id:int}", async (int id, IServiceOperationsRepository repository, CancellationToken ct) =>
    await repository.DeleteServiceAsync(id, ct) ? Results.NoContent() : Results.NotFound());

services.MapGet("/{id:int}/status-history", async (
    int id, IServiceOperationsRepository repository, CancellationToken ct) =>
    Results.Ok(await repository.GetStatusHistoryAsync(id, ct)));

services.MapPost("/{id:int}/status", async (
    int id, ChangeServiceStatusRequest request, IServiceOperationsRepository repository, CancellationToken ct) =>
{
    RequestValidation.Validate(request);
    return await repository.ChangeServiceStatusAsync(id, request, ct) is { } history
        ? Results.Ok(history) : Results.NotFound();
});

var incidents = app.MapGroup("/api/incidents");
incidents.MapGet("/", async (
    int? serviceId, IncidentStatus? status, IServiceOperationsRepository repository, CancellationToken ct) =>
    Results.Ok(await repository.ListIncidentsAsync(serviceId, status, ct)));

incidents.MapGet("/{id:int}", async (int id, IServiceOperationsRepository repository, CancellationToken ct) =>
    await repository.GetIncidentAsync(id, ct) is { } incident ? Results.Ok(incident) : Results.NotFound());

incidents.MapPost("/", async (CreateIncidentRequest request, IServiceOperationsRepository repository, CancellationToken ct) =>
{
    RequestValidation.Validate(request);
    var incident = await repository.CreateIncidentAsync(request, ct);
    return Results.Created($"/api/incidents/{incident.Id}", incident);
});

incidents.MapPut("/{id:int}", async (
    int id, UpdateIncidentRequest request, IServiceOperationsRepository repository, CancellationToken ct) =>
{
    RequestValidation.Validate(request);
    return await repository.UpdateIncidentAsync(id, request, ct) is { } incident
        ? Results.Ok(incident) : Results.NotFound();
});

incidents.MapDelete("/{id:int}", async (int id, IServiceOperationsRepository repository, CancellationToken ct) =>
    await repository.DeleteIncidentAsync(id, ct) ? Results.NoContent() : Results.NotFound());

incidents.MapGet("/{id:int}/notes", async (
    int id, IServiceOperationsRepository repository, CancellationToken ct) =>
    Results.Ok(await repository.GetResolutionNotesAsync(id, ct)));

incidents.MapPost("/{id:int}/notes", async (
    int id, AddResolutionNoteRequest request, IServiceOperationsRepository repository, CancellationToken ct) =>
{
    RequestValidation.Validate(request);
    return await repository.AddResolutionNoteAsync(id, request, ct) is { } note
        ? Results.Created($"/api/incidents/{id}/notes", note) : Results.NotFound();
});

incidents.MapPost("/{id:int}/resolve", async (
    int id, ResolveIncidentRequest request, IServiceOperationsRepository repository, CancellationToken ct) =>
{
    RequestValidation.Validate(request);
    return await repository.ResolveIncidentAsync(id, request, ct) is { } incident
        ? Results.Ok(incident) : Results.NotFound();
});

app.Run();

public partial class Program;

