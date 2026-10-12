using ServiceOperations.Api.Domain;

namespace ServiceOperations.Api.Contracts;

public sealed record CreateServiceRequest(
    string? Name,
    string? Description,
    ServiceHealthStatus InitialStatus = ServiceHealthStatus.Operational);

public sealed record UpdateServiceRequest(string? Name, string? Description);

public sealed record ChangeServiceStatusRequest(ServiceHealthStatus Status, string? Message);

public sealed record CreateIncidentRequest(
    int ServiceId,
    string? Title,
    string? Description,
    IncidentSeverity Severity,
    DateTimeOffset? StartedAtUtc);

public sealed record UpdateIncidentRequest(
    string? Title,
    string? Description,
    IncidentSeverity Severity,
    IncidentStatus Status);

public sealed record AddResolutionNoteRequest(string? Note, string? CreatedBy);

public sealed record ResolveIncidentRequest(string? ResolutionSummary, string? ResolvedBy);

