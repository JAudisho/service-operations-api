# Service Operations & Incident API

A .NET 10 REST API for tracking monitored services, service-health changes, incidents, and resolution notes. The project supports an in-memory demo mode for local exploration and a SQL Server mode backed by parameterized ADO.NET queries.

## Features

- Create, read, update, and delete monitored services and incidents.
- Record an immutable status-history entry whenever service health changes.
- Filter incidents by service and status.
- Add investigation notes and resolve incidents with an auditable resolution note.
- Validate request data and return consistent Problem Details responses for invalid, missing, conflicting, and unexpected operations.
- Execute related SQL writes in transactions and use explicitly typed SQL parameters.
- Run unit and API tests with xUnit and `WebApplicationFactory`.
- Run the test suite on every push and pull request with GitHub Actions.

## Run locally

Install the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0). From the repository root, run:

```bash
dotnet test tests/ServiceOperations.Api.Tests/ServiceOperations.Api.Tests.csproj
dotnet run --project src/ServiceOperations.Api/ServiceOperations.Api.csproj
```

The API starts in demo mode at `http://localhost:5192`. Demo mode keeps data in memory and resets it whenever the application restarts.

## API endpoints

| Method | Endpoint | Purpose |
| --- | --- | --- |
| `GET` | `/health` | Check API health and active data mode |
| `GET` | `/api/services` | List monitored services |
| `POST` | `/api/services` | Create a monitored service |
| `GET` | `/api/services/{id}` | Get one service |
| `PUT` | `/api/services/{id}` | Update service details |
| `DELETE` | `/api/services/{id}` | Delete a service with no incidents |
| `POST` | `/api/services/{id}/status` | Change service health and record history |
| `GET` | `/api/services/{id}/status-history` | View service-health history |
| `GET` | `/api/incidents?serviceId=&status=` | List or filter incidents |
| `POST` | `/api/incidents` | Create an incident |
| `GET` | `/api/incidents/{id}` | Get one incident |
| `PUT` | `/api/incidents/{id}` | Update an unresolved incident |
| `DELETE` | `/api/incidents/{id}` | Delete an incident and its notes |
| `POST` | `/api/incidents/{id}/notes` | Add an investigation or resolution note |
| `GET` | `/api/incidents/{id}/notes` | View incident notes |
| `POST` | `/api/incidents/{id}/resolve` | Resolve an incident and add its final note |

Enums are serialized as names such as `Operational`, `MajorOutage`, `Investigating`, and `Critical`.

## Example workflow

Create a service:

```http
POST /api/services
Content-Type: application/json

{
  "name": "Payments API",
  "description": "Processes card payments",
  "initialStatus": "Operational"
}
```

Create an incident for the returned service ID:

```http
POST /api/incidents
Content-Type: application/json

{
  "serviceId": 2,
  "title": "Elevated payment failures",
  "description": "Payment authorization failures exceeded the alert threshold.",
  "severity": "High"
}
```

Resolve the incident:

```http
POST /api/incidents/1/resolve
Content-Type: application/json

{
  "resolutionSummary": "Rolled back the gateway configuration change.",
  "resolvedBy": "Operations"
}
```

## SQL Server mode

Run `database/schema.sql` in SQL Server Management Studio or the VS Code MSSQL extension. The script creates a `ServiceOperations` database with normalized tables, foreign keys, check constraints, and supporting indexes.

Set the data mode and connection string in the terminal session before starting the API:

```powershell
$env:Data__Mode = 'SqlServer'
$env:ConnectionStrings__ServiceOperations = 'Server=YOUR_SERVER;Database=ServiceOperations;Integrated Security=True;Encrypt=True;TrustServerCertificate=True;'
dotnet run --project src/ServiceOperations.Api/ServiceOperations.Api.csproj
```

Replace `YOUR_SERVER` with the SQL Server instance name. `TrustServerCertificate=True` is intended for local development with a self-signed certificate. Use a trusted certificate outside local development, and keep credentials out of committed configuration files.

## Design

`Services` stores each monitored service's current health. `ServiceStatusHistory` provides a chronological audit trail. `Incidents` belongs to a service, while `ResolutionNotes` belongs to an incident. Deleting an incident cascades to its notes; deleting a service is blocked while incidents still reference it.

The API selects either the in-memory or SQL Server repository through dependency injection. Validation and workflow rules remain consistent across both implementations. SQL Server operations use `Microsoft.Data.SqlClient`, typed parameters, and transactions for multi-table state changes.

## Tests and continuous integration

The test suite covers request validation, service CRUD, state transitions, incident resolution, notes, missing resources, and conflict behavior. API tests host the application in memory and exercise real HTTP routes. `.github/workflows/ci.yml` restores and runs the tests on pushes and pull requests to `main` or `master`.

