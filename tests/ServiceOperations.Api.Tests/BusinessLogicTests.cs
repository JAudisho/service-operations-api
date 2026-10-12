using ServiceOperations.Api.Contracts;
using ServiceOperations.Api.Data;
using ServiceOperations.Api.Domain;
using ServiceOperations.Api.Validation;
using Xunit;

namespace ServiceOperations.Api.Tests;

public sealed class BusinessLogicTests
{
    [Fact]
    public void IncidentValidationRejectsFutureStartTime()
    {
        var request = new CreateIncidentRequest(1, "Future incident", "Invalid start time",
            IncidentSeverity.Low, DateTimeOffset.UtcNow.AddHours(1));
        var exception = Assert.Throws<RequestValidationException>(() => RequestValidation.Validate(request));
        Assert.Contains(nameof(request.StartedAtUtc), exception.Errors.Keys);
    }

    [Fact]
    public async Task RepositoryRecordsPreviousAndNewServiceStatus()
    {
        var repository = new InMemoryServiceOperationsRepository();
        var service = await repository.CreateServiceAsync(
            new CreateServiceRequest("Search", null), CancellationToken.None);
        var history = await repository.ChangeServiceStatusAsync(service.Id,
            new ChangeServiceStatusRequest(ServiceHealthStatus.MajorOutage, "Search is unavailable."),
            CancellationToken.None);
        Assert.NotNull(history);
        Assert.Equal(ServiceHealthStatus.Operational, history.PreviousStatus);
        Assert.Equal(ServiceHealthStatus.MajorOutage, history.NewStatus);
    }

    [Fact]
    public async Task ResolvingIncidentAddsNoteAndPreventsSecondResolution()
    {
        var repository = new InMemoryServiceOperationsRepository();
        var service = (await repository.ListServicesAsync(CancellationToken.None)).Single();
        var incident = await repository.CreateIncidentAsync(new CreateIncidentRequest(
            service.Id, "Queue delay", "Messages are delayed.", IncidentSeverity.Medium, null),
            CancellationToken.None);
        var resolved = await repository.ResolveIncidentAsync(incident.Id,
            new ResolveIncidentRequest("Queue drained.", "On-call engineer"), CancellationToken.None);
        Assert.Equal(IncidentStatus.Resolved, resolved!.Status);
        Assert.Single(await repository.GetResolutionNotesAsync(incident.Id, CancellationToken.None));
        await Assert.ThrowsAsync<ResourceConflictException>(() => repository.ResolveIncidentAsync(
            incident.Id, new ResolveIncidentRequest("Again", "Engineer"), CancellationToken.None));
    }
}

