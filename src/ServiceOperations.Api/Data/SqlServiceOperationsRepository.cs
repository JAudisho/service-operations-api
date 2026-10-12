using System.Data;
using Microsoft.Data.SqlClient;
using ServiceOperations.Api.Contracts;
using ServiceOperations.Api.Domain;

namespace ServiceOperations.Api.Data;

public sealed class SqlServiceOperationsRepository(IConfiguration configuration) : IServiceOperationsRepository
{
    private string ConnectionString => configuration.GetConnectionString("ServiceOperations")
        ?? throw new InvalidOperationException("ConnectionStrings:ServiceOperations is required in SqlServer mode.");

    public async Task<IReadOnlyList<MonitoredService>> ListServicesAsync(CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT Id, Name, Description, Status, CreatedAtUtc, UpdatedAtUtc
            FROM dbo.Services ORDER BY Name;
            """;
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var results = new List<MonitoredService>();
        while (await reader.ReadAsync(cancellationToken)) results.Add(ReadService(reader));
        return results;
    }

    public async Task<MonitoredService?> GetServiceAsync(int id, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT Id, Name, Description, Status, CreatedAtUtc, UpdatedAtUtc
            FROM dbo.Services WHERE Id = @id;
            """;
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        AddInt(command, "@id", id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadService(reader) : null;
    }

    public async Task<MonitoredService> CreateServiceAsync(
        CreateServiceRequest request, CancellationToken cancellationToken)
    {
        const string insertService = """
            INSERT dbo.Services (Name, Description, Status)
            OUTPUT INSERTED.Id, INSERTED.Name, INSERTED.Description, INSERTED.Status,
                   INSERTED.CreatedAtUtc, INSERTED.UpdatedAtUtc
            VALUES (@name, @description, @status);
            """;
        const string insertHistory = """
            INSERT dbo.ServiceStatusHistory (ServiceId, PreviousStatus, NewStatus, Message)
            VALUES (@serviceId, NULL, @status, @message);
            """;
        await using var connection = await OpenAsync(cancellationToken);
        await using SqlTransaction transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            MonitoredService service;
            await using (var command = new SqlCommand(insertService, connection, transaction))
            {
                AddNVarChar(command, "@name", 120, request.Name!.Trim());
                AddNVarChar(command, "@description", 500, Clean(request.Description));
                AddNVarChar(command, "@status", 30, request.InitialStatus.ToString());
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                await reader.ReadAsync(cancellationToken);
                service = ReadService(reader);
            }
            await using (var command = new SqlCommand(insertHistory, connection, transaction))
            {
                AddInt(command, "@serviceId", service.Id);
                AddNVarChar(command, "@status", 30, service.Status.ToString());
                AddNVarChar(command, "@message", 500, "Service added to monitoring.");
                await command.ExecuteNonQueryAsync(cancellationToken);
            }
            await transaction.CommitAsync(cancellationToken);
            return service;
        }
        catch (SqlException exception) when (IsUniqueViolation(exception))
        {
            await transaction.RollbackAsync(cancellationToken);
            throw new ResourceConflictException($"A service named '{request.Name!.Trim()}' already exists.");
        }
    }

    public async Task<MonitoredService?> UpdateServiceAsync(
        int id, UpdateServiceRequest request, CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE dbo.Services
            SET Name = @name, Description = @description, UpdatedAtUtc = SYSUTCDATETIME()
            OUTPUT INSERTED.Id, INSERTED.Name, INSERTED.Description, INSERTED.Status,
                   INSERTED.CreatedAtUtc, INSERTED.UpdatedAtUtc
            WHERE Id = @id;
            """;
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        AddInt(command, "@id", id);
        AddNVarChar(command, "@name", 120, request.Name!.Trim());
        AddNVarChar(command, "@description", 500, Clean(request.Description));
        try
        {
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            return await reader.ReadAsync(cancellationToken) ? ReadService(reader) : null;
        }
        catch (SqlException exception) when (IsUniqueViolation(exception))
        {
            throw new ResourceConflictException($"A service named '{request.Name!.Trim()}' already exists.");
        }
    }

    public async Task<bool> DeleteServiceAsync(int id, CancellationToken cancellationToken)
    {
        const string sql = "DELETE dbo.Services WHERE Id = @id;";
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        AddInt(command, "@id", id);
        try { return await command.ExecuteNonQueryAsync(cancellationToken) > 0; }
        catch (SqlException exception) when (exception.Number == 547)
        {
            throw new ResourceConflictException("Delete the service's incidents before deleting the service.");
        }
    }

    public async Task<IReadOnlyList<ServiceStatusHistory>> GetStatusHistoryAsync(
        int serviceId, CancellationToken cancellationToken)
    {
        const string sql = """
            IF NOT EXISTS (SELECT 1 FROM dbo.Services WHERE Id = @serviceId)
                THROW 51000, 'Service not found.', 1;
            SELECT Id, ServiceId, PreviousStatus, NewStatus, Message, ChangedAtUtc
            FROM dbo.ServiceStatusHistory
            WHERE ServiceId = @serviceId ORDER BY ChangedAtUtc DESC, Id DESC;
            """;
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        AddInt(command, "@serviceId", serviceId);
        try
        {
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            var results = new List<ServiceStatusHistory>();
            while (await reader.ReadAsync(cancellationToken)) results.Add(ReadHistory(reader));
            return results;
        }
        catch (SqlException exception) when (exception.Number == 51000)
        {
            throw new ResourceNotFoundException("Service not found.");
        }
    }

    public async Task<ServiceStatusHistory?> ChangeServiceStatusAsync(
        int serviceId, ChangeServiceStatusRequest request, CancellationToken cancellationToken)
    {
        const string select = "SELECT Status FROM dbo.Services WITH (UPDLOCK, HOLDLOCK) WHERE Id = @serviceId;";
        const string update = """
            UPDATE dbo.Services SET Status = @status, UpdatedAtUtc = SYSUTCDATETIME() WHERE Id = @serviceId;
            INSERT dbo.ServiceStatusHistory (ServiceId, PreviousStatus, NewStatus, Message)
            OUTPUT INSERTED.Id, INSERTED.ServiceId, INSERTED.PreviousStatus, INSERTED.NewStatus,
                   INSERTED.Message, INSERTED.ChangedAtUtc
            VALUES (@serviceId, @previousStatus, @status, @message);
            """;
        await using var connection = await OpenAsync(cancellationToken);
        await using SqlTransaction transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
        string? current;
        await using (var command = new SqlCommand(select, connection, transaction))
        {
            AddInt(command, "@serviceId", serviceId);
            current = (string?)await command.ExecuteScalarAsync(cancellationToken);
        }
        if (current is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }
        if (current == request.Status.ToString())
        {
            await transaction.RollbackAsync(cancellationToken);
            throw new ResourceConflictException($"Service is already {request.Status}.");
        }
        await using var updateCommand = new SqlCommand(update, connection, transaction);
        AddInt(updateCommand, "@serviceId", serviceId);
        AddNVarChar(updateCommand, "@previousStatus", 30, current);
        AddNVarChar(updateCommand, "@status", 30, request.Status.ToString());
        AddNVarChar(updateCommand, "@message", 500, Clean(request.Message));
        await using var reader = await updateCommand.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        var history = ReadHistory(reader);
        await reader.DisposeAsync();
        await transaction.CommitAsync(cancellationToken);
        return history;
    }

    public async Task<IReadOnlyList<Incident>> ListIncidentsAsync(
        int? serviceId, IncidentStatus? status, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT Id, ServiceId, Title, Description, Severity, Status,
                   StartedAtUtc, ResolvedAtUtc, UpdatedAtUtc
            FROM dbo.Incidents
            WHERE (@serviceId IS NULL OR ServiceId = @serviceId)
              AND (@status IS NULL OR Status = @status)
            ORDER BY StartedAtUtc DESC, Id DESC;
            """;
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        AddNullableInt(command, "@serviceId", serviceId);
        AddNVarChar(command, "@status", 30, status?.ToString());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var results = new List<Incident>();
        while (await reader.ReadAsync(cancellationToken)) results.Add(ReadIncident(reader));
        return results;
    }

    public async Task<Incident?> GetIncidentAsync(int id, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT Id, ServiceId, Title, Description, Severity, Status,
                   StartedAtUtc, ResolvedAtUtc, UpdatedAtUtc
            FROM dbo.Incidents WHERE Id = @id;
            """;
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        AddInt(command, "@id", id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadIncident(reader) : null;
    }

    public async Task<Incident> CreateIncidentAsync(
        CreateIncidentRequest request, CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT dbo.Incidents (ServiceId, Title, Description, Severity, Status, StartedAtUtc)
            OUTPUT INSERTED.Id, INSERTED.ServiceId, INSERTED.Title, INSERTED.Description,
                   INSERTED.Severity, INSERTED.Status, INSERTED.StartedAtUtc,
                   INSERTED.ResolvedAtUtc, INSERTED.UpdatedAtUtc
            VALUES (@serviceId, @title, @description, @severity, 'Open', @startedAtUtc);
            """;
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        AddInt(command, "@serviceId", request.ServiceId);
        AddNVarChar(command, "@title", 160, request.Title!.Trim());
        AddNVarChar(command, "@description", 4000, request.Description!.Trim());
        AddNVarChar(command, "@severity", 20, request.Severity.ToString());
        AddDateTimeOffset(command, "@startedAtUtc", request.StartedAtUtc ?? DateTimeOffset.UtcNow);
        try
        {
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            await reader.ReadAsync(cancellationToken);
            return ReadIncident(reader);
        }
        catch (SqlException exception) when (exception.Number == 547)
        {
            throw new ResourceNotFoundException("Service not found.");
        }
    }

    public async Task<Incident?> UpdateIncidentAsync(
        int id, UpdateIncidentRequest request, CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE dbo.Incidents
            SET Title = @title, Description = @description, Severity = @severity,
                Status = @status, UpdatedAtUtc = SYSUTCDATETIME()
            OUTPUT INSERTED.Id, INSERTED.ServiceId, INSERTED.Title, INSERTED.Description,
                   INSERTED.Severity, INSERTED.Status, INSERTED.StartedAtUtc,
                   INSERTED.ResolvedAtUtc, INSERTED.UpdatedAtUtc
            WHERE Id = @id AND Status <> 'Resolved';
            """;
        if (request.Status == IncidentStatus.Resolved)
            throw new ResourceConflictException("Use the resolve endpoint to resolve an incident.");
        await using var connection = await OpenAsync(cancellationToken);
        var existing = await GetIncidentStatusAsync(connection, id, cancellationToken);
        if (existing is null) return null;
        if (existing == IncidentStatus.Resolved.ToString())
            throw new ResourceConflictException("Resolved incidents cannot be edited.");
        await using var command = new SqlCommand(sql, connection);
        AddInt(command, "@id", id);
        AddNVarChar(command, "@title", 160, request.Title!.Trim());
        AddNVarChar(command, "@description", 4000, request.Description!.Trim());
        AddNVarChar(command, "@severity", 20, request.Severity.ToString());
        AddNVarChar(command, "@status", 30, request.Status.ToString());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadIncident(reader) : null;
    }

    public async Task<bool> DeleteIncidentAsync(int id, CancellationToken cancellationToken)
    {
        const string sql = "DELETE dbo.Incidents WHERE Id = @id;";
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        AddInt(command, "@id", id);
        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    public async Task<IReadOnlyList<ResolutionNote>> GetResolutionNotesAsync(
        int incidentId, CancellationToken cancellationToken)
    {
        const string sql = """
            IF NOT EXISTS (SELECT 1 FROM dbo.Incidents WHERE Id = @incidentId)
                THROW 51001, 'Incident not found.', 1;
            SELECT Id, IncidentId, Note, CreatedBy, CreatedAtUtc
            FROM dbo.ResolutionNotes WHERE IncidentId = @incidentId
            ORDER BY CreatedAtUtc, Id;
            """;
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        AddInt(command, "@incidentId", incidentId);
        try
        {
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            var results = new List<ResolutionNote>();
            while (await reader.ReadAsync(cancellationToken)) results.Add(ReadNote(reader));
            return results;
        }
        catch (SqlException exception) when (exception.Number == 51001)
        {
            throw new ResourceNotFoundException("Incident not found.");
        }
    }

    public async Task<ResolutionNote?> AddResolutionNoteAsync(
        int incidentId, AddResolutionNoteRequest request, CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT dbo.ResolutionNotes (IncidentId, Note, CreatedBy)
            OUTPUT INSERTED.Id, INSERTED.IncidentId, INSERTED.Note,
                   INSERTED.CreatedBy, INSERTED.CreatedAtUtc
            VALUES (@incidentId, @note, @createdBy);
            """;
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        AddInt(command, "@incidentId", incidentId);
        AddNVarChar(command, "@note", 2000, request.Note!.Trim());
        AddNVarChar(command, "@createdBy", 120, request.CreatedBy!.Trim());
        try
        {
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            await reader.ReadAsync(cancellationToken);
            return ReadNote(reader);
        }
        catch (SqlException exception) when (exception.Number == 547) { return null; }
    }

    public async Task<Incident?> ResolveIncidentAsync(
        int incidentId, ResolveIncidentRequest request, CancellationToken cancellationToken)
    {
        const string select = "SELECT Status FROM dbo.Incidents WITH (UPDLOCK, HOLDLOCK) WHERE Id = @id;";
        const string insertNote = """
            INSERT dbo.ResolutionNotes (IncidentId, Note, CreatedBy)
            VALUES (@id, @note, @createdBy);
            """;
        const string update = """
            UPDATE dbo.Incidents
            SET Status = 'Resolved', ResolvedAtUtc = SYSUTCDATETIME(), UpdatedAtUtc = SYSUTCDATETIME()
            OUTPUT INSERTED.Id, INSERTED.ServiceId, INSERTED.Title, INSERTED.Description,
                   INSERTED.Severity, INSERTED.Status, INSERTED.StartedAtUtc,
                   INSERTED.ResolvedAtUtc, INSERTED.UpdatedAtUtc
            WHERE Id = @id;
            """;
        await using var connection = await OpenAsync(cancellationToken);
        await using SqlTransaction transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
        string? status;
        await using (var command = new SqlCommand(select, connection, transaction))
        {
            AddInt(command, "@id", incidentId);
            status = (string?)await command.ExecuteScalarAsync(cancellationToken);
        }
        if (status is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }
        if (status == IncidentStatus.Resolved.ToString())
        {
            await transaction.RollbackAsync(cancellationToken);
            throw new ResourceConflictException("Incident is already resolved.");
        }
        await using (var noteCommand = new SqlCommand(insertNote, connection, transaction))
        {
            AddInt(noteCommand, "@id", incidentId);
            AddNVarChar(noteCommand, "@note", 2000, request.ResolutionSummary!.Trim());
            AddNVarChar(noteCommand, "@createdBy", 120, request.ResolvedBy!.Trim());
            await noteCommand.ExecuteNonQueryAsync(cancellationToken);
        }
        Incident incident;
        await using (var updateCommand = new SqlCommand(update, connection, transaction))
        {
            AddInt(updateCommand, "@id", incidentId);
            await using var reader = await updateCommand.ExecuteReaderAsync(cancellationToken);
            await reader.ReadAsync(cancellationToken);
            incident = ReadIncident(reader);
        }
        await transaction.CommitAsync(cancellationToken);
        return incident;
    }

    private async Task<SqlConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private static async Task<string?> GetIncidentStatusAsync(
        SqlConnection connection, int id, CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand("SELECT Status FROM dbo.Incidents WHERE Id = @id;", connection);
        AddInt(command, "@id", id);
        return (string?)await command.ExecuteScalarAsync(cancellationToken);
    }

    private static MonitoredService ReadService(SqlDataReader reader) => new(
        reader.GetInt32(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2),
        Enum.Parse<ServiceHealthStatus>(reader.GetString(3)), reader.GetDateTimeOffset(4), reader.GetDateTimeOffset(5));

    private static ServiceStatusHistory ReadHistory(SqlDataReader reader) => new(
        reader.GetInt32(0), reader.GetInt32(1), reader.IsDBNull(2) ? null : Enum.Parse<ServiceHealthStatus>(reader.GetString(2)),
        Enum.Parse<ServiceHealthStatus>(reader.GetString(3)), reader.IsDBNull(4) ? null : reader.GetString(4),
        reader.GetDateTimeOffset(5));

    private static Incident ReadIncident(SqlDataReader reader) => new(
        reader.GetInt32(0), reader.GetInt32(1), reader.GetString(2), reader.GetString(3),
        Enum.Parse<IncidentSeverity>(reader.GetString(4)), Enum.Parse<IncidentStatus>(reader.GetString(5)),
        reader.GetDateTimeOffset(6), reader.IsDBNull(7) ? null : reader.GetDateTimeOffset(7), reader.GetDateTimeOffset(8));

    private static ResolutionNote ReadNote(SqlDataReader reader) => new(
        reader.GetInt32(0), reader.GetInt32(1), reader.GetString(2), reader.GetString(3), reader.GetDateTimeOffset(4));

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static bool IsUniqueViolation(SqlException exception) => exception.Number is 2601 or 2627;
    private static void AddInt(SqlCommand command, string name, int value) =>
        command.Parameters.Add(name, SqlDbType.Int).Value = value;
    private static void AddNullableInt(SqlCommand command, string name, int? value) =>
        command.Parameters.Add(name, SqlDbType.Int).Value = value.HasValue ? value.Value : DBNull.Value;
    private static void AddNVarChar(SqlCommand command, string name, int size, string? value) =>
        command.Parameters.Add(name, SqlDbType.NVarChar, size).Value = value is null ? DBNull.Value : value;
    private static void AddDateTimeOffset(SqlCommand command, string name, DateTimeOffset value) =>
        command.Parameters.Add(name, SqlDbType.DateTimeOffset).Value = value;
}

