IF DB_ID(N'ServiceOperations') IS NULL
BEGIN
    CREATE DATABASE ServiceOperations;
END;
GO

USE ServiceOperations;
GO

CREATE TABLE dbo.Services
(
    Id             int IDENTITY(1,1) NOT NULL CONSTRAINT PK_Services PRIMARY KEY,
    Name           nvarchar(120) NOT NULL,
    Description    nvarchar(500) NULL,
    Status         nvarchar(30) NOT NULL,
    CreatedAtUtc   datetimeoffset(0) NOT NULL CONSTRAINT DF_Services_CreatedAtUtc DEFAULT SYSUTCDATETIME(),
    UpdatedAtUtc   datetimeoffset(0) NOT NULL CONSTRAINT DF_Services_UpdatedAtUtc DEFAULT SYSUTCDATETIME(),
    CONSTRAINT UQ_Services_Name UNIQUE (Name),
    CONSTRAINT CK_Services_Status CHECK (Status IN
        ('Operational', 'Degraded', 'PartialOutage', 'MajorOutage', 'Maintenance'))
);
GO

CREATE TABLE dbo.ServiceStatusHistory
(
    Id               int IDENTITY(1,1) NOT NULL CONSTRAINT PK_ServiceStatusHistory PRIMARY KEY,
    ServiceId        int NOT NULL,
    PreviousStatus   nvarchar(30) NULL,
    NewStatus        nvarchar(30) NOT NULL,
    Message          nvarchar(500) NULL,
    ChangedAtUtc     datetimeoffset(0) NOT NULL CONSTRAINT DF_ServiceStatusHistory_ChangedAtUtc DEFAULT SYSUTCDATETIME(),
    CONSTRAINT FK_ServiceStatusHistory_Services FOREIGN KEY (ServiceId)
        REFERENCES dbo.Services(Id) ON DELETE CASCADE,
    CONSTRAINT CK_ServiceStatusHistory_PreviousStatus CHECK (PreviousStatus IS NULL OR PreviousStatus IN
        ('Operational', 'Degraded', 'PartialOutage', 'MajorOutage', 'Maintenance')),
    CONSTRAINT CK_ServiceStatusHistory_NewStatus CHECK (NewStatus IN
        ('Operational', 'Degraded', 'PartialOutage', 'MajorOutage', 'Maintenance'))
);
GO

CREATE INDEX IX_ServiceStatusHistory_ServiceId_ChangedAtUtc
    ON dbo.ServiceStatusHistory(ServiceId, ChangedAtUtc DESC);
GO

CREATE TABLE dbo.Incidents
(
    Id             int IDENTITY(1,1) NOT NULL CONSTRAINT PK_Incidents PRIMARY KEY,
    ServiceId      int NOT NULL,
    Title          nvarchar(160) NOT NULL,
    Description    nvarchar(4000) NOT NULL,
    Severity       nvarchar(20) NOT NULL,
    Status         nvarchar(30) NOT NULL CONSTRAINT DF_Incidents_Status DEFAULT 'Open',
    StartedAtUtc   datetimeoffset(0) NOT NULL,
    ResolvedAtUtc  datetimeoffset(0) NULL,
    UpdatedAtUtc   datetimeoffset(0) NOT NULL CONSTRAINT DF_Incidents_UpdatedAtUtc DEFAULT SYSUTCDATETIME(),
    CONSTRAINT FK_Incidents_Services FOREIGN KEY (ServiceId) REFERENCES dbo.Services(Id),
    CONSTRAINT CK_Incidents_Severity CHECK (Severity IN ('Low', 'Medium', 'High', 'Critical')),
    CONSTRAINT CK_Incidents_Status CHECK (Status IN ('Open', 'Investigating', 'Monitoring', 'Resolved')),
    CONSTRAINT CK_Incidents_Resolution CHECK
        ((Status = 'Resolved' AND ResolvedAtUtc IS NOT NULL) OR (Status <> 'Resolved' AND ResolvedAtUtc IS NULL))
);
GO

CREATE INDEX IX_Incidents_ServiceId_Status_StartedAtUtc
    ON dbo.Incidents(ServiceId, Status, StartedAtUtc DESC);
GO

CREATE TABLE dbo.ResolutionNotes
(
    Id             int IDENTITY(1,1) NOT NULL CONSTRAINT PK_ResolutionNotes PRIMARY KEY,
    IncidentId     int NOT NULL,
    Note           nvarchar(2000) NOT NULL,
    CreatedBy      nvarchar(120) NOT NULL,
    CreatedAtUtc   datetimeoffset(0) NOT NULL CONSTRAINT DF_ResolutionNotes_CreatedAtUtc DEFAULT SYSUTCDATETIME(),
    CONSTRAINT FK_ResolutionNotes_Incidents FOREIGN KEY (IncidentId)
        REFERENCES dbo.Incidents(Id) ON DELETE CASCADE
);
GO

CREATE INDEX IX_ResolutionNotes_IncidentId_CreatedAtUtc
    ON dbo.ResolutionNotes(IncidentId, CreatedAtUtc);
GO

