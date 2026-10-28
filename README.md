# RemsToRems

## 1. Overview

`RemsToRems` is an ASP.NET Core Web API organized using a layered
architecture. The solution separates HTTP handling, application
workflows, business concepts, and technical integrations such as ADO.NET
database access and Azure Blob-based configuration.

The design supports requests that may need to access more than one
sponsor or REMS database during a single API request. Each database
operation should resolve its own approved database key to configuration
and create its own connection; the application must not depend on a
shared, mutable "current database" value.

> **Implementation note:** Azure Blob configuration behavior, database
> provider, database schema, and the meaning of `azureblobkey` must be
> confirmed against the existing implementation and requirements. Names
> and examples in this document describe the intended architecture, not
> a claim that these integrations are already implemented.

### Running the API

The Azure Storage connection string (`AzureBlobConfiguration:ConnectionString`)
is a secret and is **not** stored in `appsettings*.json`. Supply it one of these ways:

| Where | How |
|---|---|
| Local machine | `dotnet user-secrets set "AzureBlobConfiguration:ConnectionString" "<connection string>" --project RemsToRems.Api` (Development only) |
| GitHub Codespaces | Add a Codespaces secret named `AzureBlobConfiguration__ConnectionString` for this repository (GitHub → Settings → Codespaces → Secrets). The dev container also prompts for it. |
| Azure App Service | App setting `AzureBlobConfiguration__ConnectionString` (or Key Vault reference) |

**Codespaces:** open the repository in a codespace (Code → Codespaces → Create).
The `.devcontainer` installs the .NET 10 SDK and restores the solution. Then run:

```bash
dotnet run --project RemsToRems.Api --launch-profile http
```

Port 5269 is forwarded; use `RemsToRems.Api/RemsToRems.Api.http` (REST Client
extension) to send requests. The Azure SQL server firewall must allow
connections from Codespaces for database calls to succeed.

## 2. Solution and project structure

The solution (`RemsToRems.slnx`, at the repository root) contains four projects.
Namespaces follow folders (`RemsToRems.Infrastructure.Database.Resolvers`, ...).

``` text
RemsToRems.Api/                             # Presentation layer: HTTP API
├── Controllers/PatientController.cs        # POST registry/match
├── Filters/ApiExceptionFilter.cs           # exceptions -> problem+json (400/404/500) + [ErrorLog]
├── Models/ProblemResponses.cs              # RFC 9457 error responses (application/problem+json)
└── Program.cs

RemsToRems.Application/                     # Use cases and the contracts they need
├── Abstractions/
│   ├── Auditing/       IAuditTrailService, AuditEntry, ErrorLog, AuditTransactionType
│   ├── Database/       DatabaseContext, DatabaseContextOptions
│   └── Repositories/   IPatientLookupRepository
├── Common/
│   ├── Auditing/       AuditTrailBuilder (INSERT/UPDATE/DELETE field diffs)
│   └── Exceptions/     ValidationException + ValidationError (400), NotFoundException (404)
├── Features/PatientLookup/
│   ├── IPatientLookupService, PatientLookupService, PatientLookupValidator
│   ├── PatientLookupRequest, PatientLookupRequestReader, PatientLookupErrors (fields, codes)
│   ├── PatientLookupResponse, PatientMatch, PatientMatchTier, FieldMatchResult
│   └── Matching/       PatientNameNormalizer, PatientMatcher, PatientMatchRelease, MatchUniqueId
└── DependencyInjection.cs

RemsToRems.Domain/                          # Empty for now; add business rules here when they appear

RemsToRems.Infrastructure/                  # Technical implementations (all internal)
├── Auditing/           SqlAuditTrailService, SqlAuditTrailWriter, IAuditTrailWriter
├── AzureBlob/          BlobConfigurationProvider (per-environment cache), options, document
├── Database/
│   ├── Abstractions/   IDbConnectionFactory, IDbCommandFactory, IDbCommandExecutor,
│   │                   ISurveyDatabaseResolver, IRemsDatabaseResolver
│   ├── Factories/      SqlConnectionFactory, SqlCommandFactory
│   ├── Execution/      DbCommandExecutor (all SQL runs through this)
│   ├── Models/         ResolvedDatabase
│   └── Resolvers/      SurveyDatabaseResolver (Blob), RemsDatabaseResolver (Survey Client table, cached)
├── Repositories/       PatientLookupRepository
└── DependencyInjection.cs
```

Rules this layout follows:

-   Controllers only call an application service; no validation, SQL or
    database selection.
-   Application validates input and throws `ValidationException` /
    `NotFoundException`; the controller (and `ApiExceptionFilter` as a
    safety net) maps them to 400 / 404 and everything else to 500 (written
    to `[ErrorLog]`), all as `application/problem+json`.
-   Repository interfaces take a `DatabaseContext` plus
    feature-specific criteria, never the HTTP request type.
-   Infrastructure types are `internal`; only `AddInfrastructure` is public.
-   Audit rows go to the organization's REMS database only, never the root
    Survey database.

### 2.1 `RemsToRems.Api` --- Presentation layer

Responsibilities:

-   Expose HTTP endpoints through controllers.
-   Bind and validate incoming HTTP request models.
-   Call application service interfaces.
-   Return appropriate HTTP status codes and response DTOs.
-   Configure middleware and dependency injection.

Controllers should not contain SQL, resolve Azure Blob configuration, or
select connection strings directly.

### 2.2 `RemsToRems.Application` --- Application layer

Responsibilities:

-   Coordinate the patient-lookup use cases.
-   Apply application-level validation and workflow rules.
-   Define service contracts and repository abstractions required by
    those workflows.
-   Define request and response DTOs used by the application.
-   Remain independent of infrastructure implementations.

Recommended types:

-   `IPatientLookupService`: contract for the matching use case.
-   `PatientLookupService`: implementation of the application workflow.
-   `IPatientLookupRepository`: contract for the data operations the
    workflow needs.
-   `PatientLookupRequest` and `PatientLookupResponse`: request/response DTOs.

The service interface and service implementation can both live in
Application. The repository interface also lives in Application because
the application service depends on it.

### 2.3 `RemsToRems.Domain` --- Domain layer

Responsibilities:

-   Hold core business concepts, rules, and domain-specific exceptions
    where useful.
-   Avoid references to API, Application, Infrastructure, database
    providers, or Azure SDKs.

Keep this project lightweight. Do not create entities solely because the
solution uses a layered architecture.

### 2.4 `RemsToRems.Infrastructure` --- Infrastructure layer

Responsibilities:

-   Implement application repository interfaces.
-   Execute database queries using ADO.NET.
-   Resolve approved database identifiers to database configuration.
-   Read configuration from Azure Blob storage when required by the
    existing design.
-   Create and dispose database connections.
-   Keep SQL and provider-specific code out of Application.

Recommended types:

-   `PatientLookupRepository`: implements `IPatientLookupRepository`.
-   `RemsDatabaseResolver`: resolves an approved database key to connection
    information.
-   `SqlConnectionFactory`: creates a provider-specific `DbConnection`.
-   `BlobConfigurationProvider`: encapsulates the actual Azure Blob
    configuration retrieval.
-   `the repository`: holds query text if separating SQL from
    repository code is useful.

If the database resolver and connection factory are only used internally
by Infrastructure, their interfaces can remain in Infrastructure. Put an
abstraction in Application only when Application itself needs to depend
on that capability.

## 3. Project references

Use the following project references:

  -----------------------------------------------------------------------
  Project                             References
  ----------------------------------- -----------------------------------
  `RemsToRems.Api`                     `RemsToRems.Application`,
                                      `RemsToRems.Infrastructure`

  `RemsToRems.Application`            `RemsToRems.Domain`

  `RemsToRems.Infrastructure`         `RemsToRems.Application`,
                                      `RemsToRems.Domain`

  `RemsToRems.Domain`                 None
  -----------------------------------------------------------------------

### Why Application does not reference Infrastructure

Application defines what it needs through interfaces. Infrastructure
references Application and implements those interfaces. At startup, the
API project references both projects and registers the implementation
with dependency injection.

This keeps Application independent of technical details such as SQL
Server, ADO.NET, Azure Blob Storage, and concrete repository classes.

## 4. Data flow

Typical request flow:

``` text
HTTP client
    |
    v
RemsToRems.Api
PatientController
    |
    v
IPatientLookupService
    |
    v
PatientLookupService (Application)
    |
    v
IPatientLookupRepository (Application contract)
    |
    v
PatientLookupRepository (Infrastructure implementation)
    |
    +--> RemsDatabaseResolver
    |       |
    |       +--> BlobConfigurationProvider (if required)
    |       |
    |       +--> Validated database connection configuration
    |
    +--> SqlConnectionFactory
    |
    v
ADO.NET connection and SQL query
    |
    v
Sponsor / REMS database
    |
    v
Rows mapped to DTOs
    |
    v
Repository -> Service -> Controller -> HTTP response
```

### Step-by-step

1.  The client sends a request to an API endpoint.
2.  The controller binds the request and calls `IPatientLookupService`.
3.  Dependency injection supplies the `PatientLookupService` implementation.
4.  The service coordinates the matching workflow and calls
    `IPatientLookupRepository`.
5.  The Infrastructure repository receives a validated
    sponsor/program/database identifier as appropriate for the use case.
6.  Infrastructure resolves that identifier to approved connection
    configuration. If configuration is stored in Azure Blob Storage, the
    blob provider reads it through the configured and authorized access
    mechanism.
7.  The connection factory creates a new connection for the selected
    database.
8.  The repository opens the connection, executes parameterized SQL,
    maps results, and disposes the connection and command/reader
    resources.
9.  The service processes the returned data and produces the application
    response.
10. The controller returns the HTTP response.

The precise request fields, identifier types, SQL, and response shape
must follow the actual API contract and database schema.

## 5. Supporting multiple databases in one API request

A single API call may need to query more than one sponsor or REMS
database. The service can make multiple repository calls, each with the
appropriate approved database key.

Example flow:

``` text
PatientLookupService
    |
    +--> Repository call(databaseKeyA)
    |        |
    |        +--> Resolve A -> create connection A -> query A
    |
    +--> Repository call(databaseKeyB)
             |
             +--> Resolve B -> create connection B -> query B
```

Important rules:

-   Do not store the selected database in a mutable global or singleton
    "current database" variable.
-   Do not reuse a connection for a different database.
-   Resolve and validate each database key for each operation, or use a
    safely scoped/cached configuration resolver with appropriate
    invalidation.
-   Create and dispose connections per operation. ADO.NET connection
    pooling can reduce the cost of repeated open/close operations.
-   Do not accept arbitrary connection strings from API callers.
-   Authorize that the caller may access the requested
    sponsor/program/database before querying it.
-   If operations write to multiple databases and must be atomic, design
    an explicit consistency/coordination strategy. A normal local
    database transaction does not automatically make multiple
    independent databases atomic.

## 6. Azure Blob configuration and database resolution

Keep the Azure Blob integration inside Infrastructure.

The intended responsibility split is:

-   **Application:** identifies the sponsor/program/database needed for
    the use case and calls the repository contract.
-   **Repository:** requests data using the approved database
    identifier.
-   **Database resolver:** validates the identifier and resolves it to
    connection information.
-   **Blob configuration provider:** retrieves configuration from the
    configured Blob source when applicable.
-   **Connection factory:** constructs a database-provider connection
    from the resolved configuration.

Do not assume that `azureblobkey` is itself a database connection
string. Confirm whether it is a blob name, lookup key, secret reference,
storage credential, or another identifier. Avoid logging secrets or
returning connection strings to clients. Prefer managed identity or
another approved secret-access mechanism where supported by the
deployment environment.

## 7. Dependency injection

Register application services in
`RemsToRems.Application/DependencyInjection.cs`:

``` csharp
using Microsoft.Extensions.DependencyInjection;

namespace RemsToRems.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(
        this IServiceCollection services)
    {
        services.AddScoped<IPatientLookupService, PatientLookupService>();

        return services;
    }
}
```

Register infrastructure implementations in
`RemsToRems.Infrastructure/DependencyInjection.cs`:

``` csharp
using Microsoft.Extensions.DependencyInjection;

namespace RemsToRems.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services)
    {
        services.AddScoped<IPatientLookupRepository, PatientLookupRepository>();

        // Register the database resolver, connection factory, and blob
        // configuration provider here using lifetimes appropriate to
        // their dependencies and configuration-refresh behavior.

        return services;
    }
}
```

Then call both registration methods from `RemsToRems.Api/Program.cs`:

``` csharp
builder.Services.AddApplication();
builder.Services.AddInfrastructure();
```

The namespaces and registrations above are examples; adjust them to the
actual namespaces, constructors, provider, and implementation classes in
the solution. Register every concrete dependency required by the
repository before running the API.

## 8. Repository and ADO.NET guidelines

-   Use `System.Data.Common.DbConnection` in shared abstractions when
    provider independence is useful.
-   Use the actual provider package in Infrastructure---for example,
    `Microsoft.Data.SqlClient` only if the target databases are SQL
    Server.
-   Use parameterized SQL commands; do not concatenate untrusted input
    into SQL.
-   Open connections as late as practical and dispose them promptly.
-   Dispose commands and data readers, and pass `CancellationToken`
    through asynchronous database operations.
-   Map database rows into application DTOs or domain objects as
    appropriate.
-   Keep SQL, connection strings, and provider-specific types out of
    controllers and application business workflows.
-   Avoid logging credentials, tokens, connection strings, or sensitive
    patient data.

## 9. Error handling and validation

Recommended responsibilities:

-   **API:** validate HTTP-level input and translate application
    outcomes into suitable HTTP responses.
-   **Application:** enforce use-case rules and reject invalid or
    unauthorized workflow inputs.
-   **Infrastructure:** handle provider/configuration exceptions and add
    useful diagnostic context without exposing secrets.
-   **Middleware:** centralize unexpected exception handling and return
    a safe error response.

Validate database identifiers against a trusted configuration source or
allow-list. Never treat a caller-supplied identifier as permission to
connect to an arbitrary database.

### 9.1 API 1 (`POST /registry/match`) validation errors

Every error is RFC 9457 `application/problem+json` with `type`, `title`,
`status`, `detail`, `instance`, the request's `requestId` (when it was a
valid UUID) and a new `responseId`. A 400 also has `errors`: one
`{ field, reason, code }` per failed field, all reported together, and no
lookup runs. Reasons never include the submitted value.

| Field | Missing / blank | Invalid |
|---|---|---|
| requestId | 101 (also all zeros) | 100 (not an 8-4-4-4-12 UUID) |
| patient.firstName | 105 (also empty after cleanup, e.g. "Dr.") | 104 (> 255), 112 (unsupported character) |
| patient.lastName | 107 (also empty after cleanup) | 106 (> 255), 112 (unsupported character) |
| patient.dateOfBirth | 109 | 108 (not yyyy-MM-dd, not a real date, or in the future) |
| patient.zipcode | 111 | 110 (not exactly 5 ASCII digits, or not a JSON string) |

A missing `patient` object reports its four fields as missing. A body that
is empty or not JSON gets the same 400 problem with `errors: []`. Codes 112,
the future-DOB mapping, the all-zero requestId and non-string values are
pending partner confirmation (`PatientLookupErrorCodes`).

Before the character check, names are made plain ASCII
(`PatientNameNormalizer.ToAscii`): accents removed (José → JOSE), curly
quotes → `'`, long dashes → `-`. Stored names in SQL are not folded yet, so a
stored accented name is still left out of matching.

## 10. Testing approach

-   **Application unit tests:** test matching workflow and validation by
    mocking `IPatientLookupRepository`.
-   **Infrastructure tests:** test database resolution, SQL mapping, and
    Blob configuration behavior with controlled test dependencies.
-   **API integration tests:** test routing, dependency injection,
    serialization, and error responses.
-   **Database integration tests:** test actual queries against an
    isolated test database or approved test environment.

Keep production credentials and patient data out of test fixtures.

## 11. Adding a new feature

For a new use case, such as retrieving REMS programs:

1.  Add a feature folder under `RemsToRems.Application/Features`.
2.  Define its request/response DTOs and service contract.
3.  Implement the application service and depend on repository
    interfaces.
4.  Add or extend repository interfaces in
    `RemsToRems.Application/Abstractions/Repositories`.
5.  Implement them in `RemsToRems.Infrastructure/Repositories`, running
    SQL through `IDbCommandExecutor`.
6.  Register the service in `AddApplication` and the repository in
    `AddInfrastructure`.
7.  Add or update controller endpoints in the API project.
8.  Add tests at the relevant layers.

## 12. Architecture summary

-   Controllers live in `RemsToRems.Api`.
-   Service interfaces and service implementations live in
    `RemsToRems.Application`.
-   Repository interfaces live in `RemsToRems.Application`.
-   Repository implementations, ADO.NET code, database resolution, and
    Azure Blob integration live in `RemsToRems.Infrastructure`.
-   Core business models and rules live in `RemsToRems.Domain` when
    needed.
-   Application does **not** reference Infrastructure.
-   Infrastructure implements the abstractions declared by Application.
-   The API references both Application and Infrastructure and wires
    them together with dependency injection.
-   Every database operation resolves an approved database key and uses
    its own properly disposed connection.

This structure keeps the patient-lookup workflow testable and
maintainable while allowing Infrastructure to handle the details of
database switching and configuration retrieval.
