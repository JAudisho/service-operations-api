namespace ServiceOperations.Api.Domain;

public enum ServiceHealthStatus
{
    Operational,
    Degraded,
    PartialOutage,
    MajorOutage,
    Maintenance
}

public enum IncidentStatus
{
    Open,
    Investigating,
    Monitoring,
    Resolved
}

public enum IncidentSeverity
{
    Low,
    Medium,
    High,
    Critical
}

public sealed record MonitoredService(
    int Id,
    string Name,
    string? Description,
    ServiceHealthStatus Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record ServiceStatusHistory(
    int Id,
    int ServiceId,
    ServiceHealthStatus? PreviousStatus,
    ServiceHealthStatus NewStatus,
    string? Message,
    DateTimeOffset ChangedAtUtc);

public sealed record Incident(
    int Id,
    int ServiceId,
    string Title,
    string Description,
    IncidentSeverity Severity,
    IncidentStatus Status,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? ResolvedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record ResolutionNote(
    int Id,
    int IncidentId,
    string Note,
    string CreatedBy,
    DateTimeOffset CreatedAtUtc);

