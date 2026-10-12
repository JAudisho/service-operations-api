using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.Testing;
using ServiceOperations.Api.Contracts;
using ServiceOperations.Api.Domain;
using Xunit;

namespace ServiceOperations.Api.Tests;

public sealed class ApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public ApiTests(WebApplicationFactory<Program> factory) => _client = factory.CreateClient();

    [Fact]
    public async Task HealthEndpointReportsDemoMode()
    {
        var response = await _client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        Assert.Contains("healthy", json);
        Assert.Contains("Demo", json);
    }

    [Fact]
    public async Task CreateServiceRejectsMissingName()
    {
        var response = await _client.PostAsJsonAsync("/api/services",
            new CreateServiceRequest(" ", "Description"), JsonOptions);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        Assert.Contains("Name is required", json);
    }

    [Fact]
    public async Task ServiceCrudAndStatusHistoryWorkTogether()
    {
        var created = await PostAsync<MonitoredService>("/api/services",
            new CreateServiceRequest($"Billing API {Guid.NewGuid():N}", "Processes billing events"),
            HttpStatusCode.Created);

        var changed = await PostAsync<ServiceStatusHistory>($"/api/services/{created.Id}/status",
            new ChangeServiceStatusRequest(ServiceHealthStatus.Degraded, "Elevated latency"),
            HttpStatusCode.OK);
        Assert.Equal(ServiceHealthStatus.Operational, changed.PreviousStatus);
        Assert.Equal(ServiceHealthStatus.Degraded, changed.NewStatus);

        var history = await _client.GetFromJsonAsync<List<ServiceStatusHistory>>(
            $"/api/services/{created.Id}/status-history", JsonOptions);
        Assert.NotNull(history);
        Assert.Equal(2, history.Count);

        var updateResponse = await _client.PutAsJsonAsync($"/api/services/{created.Id}",
            new UpdateServiceRequest(created.Name, "Updated description"), JsonOptions);
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);

        var deleteResponse = await _client.DeleteAsync($"/api/services/{created.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/api/services/{created.Id}")).StatusCode);
    }

    [Fact]
    public async Task IncidentCanBeTrackedNotedAndResolved()
    {
        var service = await PostAsync<MonitoredService>("/api/services",
            new CreateServiceRequest($"Identity API {Guid.NewGuid():N}", null), HttpStatusCode.Created);
        var incident = await PostAsync<Incident>("/api/incidents",
            new CreateIncidentRequest(service.Id, "Authentication failures",
                "A subset of sign-in requests are failing.", IncidentSeverity.High, null),
            HttpStatusCode.Created);

        var updateResponse = await _client.PutAsJsonAsync($"/api/incidents/{incident.Id}",
            new UpdateIncidentRequest(incident.Title, incident.Description,
                IncidentSeverity.Critical, IncidentStatus.Investigating), JsonOptions);
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);

        await PostAsync<ResolutionNote>($"/api/incidents/{incident.Id}/notes",
            new AddResolutionNoteRequest("Rolled back the latest deployment.", "Operations"),
            HttpStatusCode.Created);
        var resolved = await PostAsync<Incident>($"/api/incidents/{incident.Id}/resolve",
            new ResolveIncidentRequest("Error rate returned to baseline.", "Operations"),
            HttpStatusCode.OK);
        Assert.Equal(IncidentStatus.Resolved, resolved.Status);
        Assert.NotNull(resolved.ResolvedAtUtc);

        var notes = await _client.GetFromJsonAsync<List<ResolutionNote>>(
            $"/api/incidents/{incident.Id}/notes", JsonOptions);
        Assert.NotNull(notes);
        Assert.Equal(2, notes.Count);

        var editResolved = await _client.PutAsJsonAsync($"/api/incidents/{incident.Id}",
            new UpdateIncidentRequest(incident.Title, incident.Description,
                IncidentSeverity.Low, IncidentStatus.Monitoring), JsonOptions);
        Assert.Equal(HttpStatusCode.Conflict, editResolved.StatusCode);
    }

    [Fact]
    public async Task CreatingIncidentForMissingServiceReturnsNotFound()
    {
        var response = await _client.PostAsJsonAsync("/api/incidents",
            new CreateIncidentRequest(999999, "Unavailable dependency",
                "The dependency could not be reached.", IncidentSeverity.High, null), JsonOptions);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private async Task<T> PostAsync<T>(string path, object body, HttpStatusCode expectedStatus)
    {
        var response = await _client.PostAsJsonAsync(path, body, JsonOptions);
        Assert.Equal(expectedStatus, response.StatusCode);
        var value = await response.Content.ReadFromJsonAsync<T>(JsonOptions);
        return Assert.IsType<T>(value);
    }
}

