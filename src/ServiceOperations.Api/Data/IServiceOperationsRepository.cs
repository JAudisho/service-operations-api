using ServiceOperations.Api.Contracts;
using ServiceOperations.Api.Domain;

namespace ServiceOperations.Api.Data;

public interface IServiceOperationsRepository
{
    Task<IReadOnlyList<MonitoredService>> ListServicesAsync(CancellationToken cancellationToken);
    Task<MonitoredService?> GetServiceAsync(int id, CancellationToken cancellationToken);
    Task<MonitoredService> CreateServiceAsync(CreateServiceRequest request, CancellationToken cancellationToken);
    Task<MonitoredService?> UpdateServiceAsync(int id, UpdateServiceRequest request, CancellationToken cancellationToken);
    Task<bool> DeleteServiceAsync(int id, CancellationToken cancellationToken);
    Task<IReadOnlyList<ServiceStatusHistory>> GetStatusHistoryAsync(int serviceId, CancellationToken cancellationToken);
    Task<ServiceStatusHistory?> ChangeServiceStatusAsync(int serviceId, ChangeServiceStatusRequest request, CancellationToken cancellationToken);

    Task<IReadOnlyList<Incident>> ListIncidentsAsync(int? serviceId, IncidentStatus? status, CancellationToken cancellationToken);
    Task<Incident?> GetIncidentAsync(int id, CancellationToken cancellationToken);
    Task<Incident> CreateIncidentAsync(CreateIncidentRequest request, CancellationToken cancellationToken);
    Task<Incident?> UpdateIncidentAsync(int id, UpdateIncidentRequest request, CancellationToken cancellationToken);
    Task<bool> DeleteIncidentAsync(int id, CancellationToken cancellationToken);
    Task<IReadOnlyList<ResolutionNote>> GetResolutionNotesAsync(int incidentId, CancellationToken cancellationToken);
    Task<ResolutionNote?> AddResolutionNoteAsync(int incidentId, AddResolutionNoteRequest request, CancellationToken cancellationToken);
    Task<Incident?> ResolveIncidentAsync(int incidentId, ResolveIncidentRequest request, CancellationToken cancellationToken);
}

