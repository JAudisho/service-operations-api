using ServiceOperations.Api.Contracts;
using ServiceOperations.Api.Domain;

namespace ServiceOperations.Api.Data;

public sealed class InMemoryServiceOperationsRepository : IServiceOperationsRepository
{
    private readonly object _gate = new();
    private readonly Dictionary<int, MonitoredService> _services = [];
    private readonly Dictionary<int, Incident> _incidents = [];
    private readonly List<ServiceStatusHistory> _history = [];
    private readonly List<ResolutionNote> _notes = [];
    private int _serviceId;
    private int _incidentId;
    private int _historyId;
    private int _noteId;

    public InMemoryServiceOperationsRepository()
    {
        var now = DateTimeOffset.UtcNow;
        var service = new MonitoredService(++_serviceId, "Customer Portal", "Public customer portal",
            ServiceHealthStatus.Operational, now, now);
        _services[service.Id] = service;
        _history.Add(new ServiceStatusHistory(++_historyId, service.Id, null,
            service.Status, "Service added to monitoring.", now));
    }

    public Task<IReadOnlyList<MonitoredService>> ListServicesAsync(CancellationToken cancellationToken)
    {
        lock (_gate) return Result<IReadOnlyList<MonitoredService>>(_services.Values.OrderBy(x => x.Name).ToArray());
    }

    public Task<MonitoredService?> GetServiceAsync(int id, CancellationToken cancellationToken)
    {
        lock (_gate) return Result(_services.GetValueOrDefault(id));
    }

    public Task<MonitoredService> CreateServiceAsync(CreateServiceRequest request, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (_services.Values.Any(x => x.Name.Equals(request.Name!.Trim(), StringComparison.OrdinalIgnoreCase)))
                throw new ResourceConflictException($"A service named '{request.Name.Trim()}' already exists.");
            var now = DateTimeOffset.UtcNow;
            var service = new MonitoredService(++_serviceId, request.Name.Trim(), Clean(request.Description),
                request.InitialStatus, now, now);
            _services[service.Id] = service;
            _history.Add(new ServiceStatusHistory(++_historyId, service.Id, null,
                service.Status, "Service added to monitoring.", now));
            return Result(service);
        }
    }

    public Task<MonitoredService?> UpdateServiceAsync(int id, UpdateServiceRequest request, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (!_services.TryGetValue(id, out var service)) return Result<MonitoredService?>(null);
            if (_services.Values.Any(x => x.Id != id && x.Name.Equals(request.Name!.Trim(), StringComparison.OrdinalIgnoreCase)))
                throw new ResourceConflictException($"A service named '{request.Name.Trim()}' already exists.");
            service = service with
            {
                Name = request.Name.Trim(), Description = Clean(request.Description), UpdatedAtUtc = DateTimeOffset.UtcNow
            };
            _services[id] = service;
            return Result<MonitoredService?>(service);
        }
    }

    public Task<bool> DeleteServiceAsync(int id, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (_incidents.Values.Any(x => x.ServiceId == id))
                throw new ResourceConflictException("Delete the service's incidents before deleting the service.");
            var removed = _services.Remove(id);
            if (removed) _history.RemoveAll(x => x.ServiceId == id);
            return Result(removed);
        }
    }

    public Task<IReadOnlyList<ServiceStatusHistory>> GetStatusHistoryAsync(int serviceId, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (!_services.ContainsKey(serviceId)) throw new ResourceNotFoundException("Service not found.");
            return Result<IReadOnlyList<ServiceStatusHistory>>(_history.Where(x => x.ServiceId == serviceId)
                .OrderByDescending(x => x.ChangedAtUtc).ToArray());
        }
    }

    public Task<ServiceStatusHistory?> ChangeServiceStatusAsync(
        int serviceId, ChangeServiceStatusRequest request, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (!_services.TryGetValue(serviceId, out var service)) return Result<ServiceStatusHistory?>(null);
            if (service.Status == request.Status)
                throw new ResourceConflictException($"Service is already {request.Status}.");
            var now = DateTimeOffset.UtcNow;
            var entry = new ServiceStatusHistory(++_historyId, serviceId, service.Status,
                request.Status, Clean(request.Message), now);
            _history.Add(entry);
            _services[serviceId] = service with { Status = request.Status, UpdatedAtUtc = now };
            return Result<ServiceStatusHistory?>(entry);
        }
    }

    public Task<IReadOnlyList<Incident>> ListIncidentsAsync(
        int? serviceId, IncidentStatus? status, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var query = _incidents.Values.AsEnumerable();
            if (serviceId.HasValue) query = query.Where(x => x.ServiceId == serviceId.Value);
            if (status.HasValue) query = query.Where(x => x.Status == status.Value);
            return Result<IReadOnlyList<Incident>>(query.OrderByDescending(x => x.StartedAtUtc).ToArray());
        }
    }

    public Task<Incident?> GetIncidentAsync(int id, CancellationToken cancellationToken)
    {
        lock (_gate) return Result(_incidents.GetValueOrDefault(id));
    }

    public Task<Incident> CreateIncidentAsync(CreateIncidentRequest request, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (!_services.ContainsKey(request.ServiceId)) throw new ResourceNotFoundException("Service not found.");
            var now = DateTimeOffset.UtcNow;
            var incident = new Incident(++_incidentId, request.ServiceId, request.Title!.Trim(),
                request.Description!.Trim(), request.Severity, IncidentStatus.Open,
                request.StartedAtUtc ?? now, null, now);
            _incidents[incident.Id] = incident;
            return Result(incident);
        }
    }

    public Task<Incident?> UpdateIncidentAsync(int id, UpdateIncidentRequest request, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (!_incidents.TryGetValue(id, out var incident)) return Result<Incident?>(null);
            if (incident.Status == IncidentStatus.Resolved)
                throw new ResourceConflictException("Resolved incidents cannot be edited.");
            if (request.Status == IncidentStatus.Resolved)
                throw new ResourceConflictException("Use the resolve endpoint to resolve an incident.");
            incident = incident with
            {
                Title = request.Title!.Trim(), Description = request.Description!.Trim(),
                Severity = request.Severity, Status = request.Status, UpdatedAtUtc = DateTimeOffset.UtcNow
            };
            _incidents[id] = incident;
            return Result<Incident?>(incident);
        }
    }

    public Task<bool> DeleteIncidentAsync(int id, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var removed = _incidents.Remove(id);
            if (removed) _notes.RemoveAll(x => x.IncidentId == id);
            return Result(removed);
        }
    }

    public Task<IReadOnlyList<ResolutionNote>> GetResolutionNotesAsync(int incidentId, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (!_incidents.ContainsKey(incidentId)) throw new ResourceNotFoundException("Incident not found.");
            return Result<IReadOnlyList<ResolutionNote>>(_notes.Where(x => x.IncidentId == incidentId)
                .OrderBy(x => x.CreatedAtUtc).ToArray());
        }
    }

    public Task<ResolutionNote?> AddResolutionNoteAsync(
        int incidentId, AddResolutionNoteRequest request, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (!_incidents.ContainsKey(incidentId)) return Result<ResolutionNote?>(null);
            var note = new ResolutionNote(++_noteId, incidentId, request.Note!.Trim(),
                request.CreatedBy!.Trim(), DateTimeOffset.UtcNow);
            _notes.Add(note);
            return Result<ResolutionNote?>(note);
        }
    }

    public Task<Incident?> ResolveIncidentAsync(
        int incidentId, ResolveIncidentRequest request, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (!_incidents.TryGetValue(incidentId, out var incident)) return Result<Incident?>(null);
            if (incident.Status == IncidentStatus.Resolved)
                throw new ResourceConflictException("Incident is already resolved.");
            var now = DateTimeOffset.UtcNow;
            _notes.Add(new ResolutionNote(++_noteId, incidentId, request.ResolutionSummary!.Trim(),
                request.ResolvedBy!.Trim(), now));
            incident = incident with { Status = IncidentStatus.Resolved, ResolvedAtUtc = now, UpdatedAtUtc = now };
            _incidents[incidentId] = incident;
            return Result<Incident?>(incident);
        }
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static Task<T> Result<T>(T value) => Task.FromResult(value);
}

