# Retail Payments – Transaction Import

Developer guide. ASP.NET 10 (minimal APIs) + EF Core 10 (Code-First) + SQLite backend that reconciles
card transactions from three source systems (**Store Uptown**, **Store Downtown**, **Online**), plus a
small Angular 22 frontend to browse them. More background: [docs/DESIGN.md](docs/DESIGN.md) and
[docs/architecture.svg](docs/architecture.svg).

## Contents
1. [Quick start](#quick-start)
2. [Repository layout](#repository-layout)
3. [How an import works](#how-an-import-works)
4. [Everyday tasks](#everyday-tasks)
5. [Testing](#testing)
6. [API reference](#api-reference)
7. [Configuration](#configuration)
8. [Design notes](#design-notes)
9. [Troubleshooting](#troubleshooting)
10. [Assumptions and known limits](#assumptions-and-known-limits)

## Quick start

**Prerequisites:** .NET 10 SDK (`global.json` pins 10.0.100, rolls forward to newer 10.0.x features),
Node.js 20.19+ / 22+ with npm. Visual Studio 2026 (v18) if you use VS; older versions cannot target .NET 10.

```bash
dotnet build                          # API + tests
dotnet run --project src/RetailPayments.Api     # http://localhost:5080  (Swagger: /swagger)

cd frontend
npm install
npm start                             # http://localhost:4200, proxies /api to :5080
```

The SQLite file `retailpayments.db` is created and migrated automatically on first start.
Click **Run import now** in the UI (or `curl -X POST http://localhost:5080/api/import`) to load data.

**Visual Studio:** open `RetailPayments.sln`, set `RetailPayments.Api` as startup project, press F5. The
`SpaProxy` package starts the Angular dev server for you (running `npm install` once if `node_modules` is
missing) and opens the UI. Swagger UI is Development-only.

## Repository layout

```
RetailPayments.sln
global.json                          SDK pin
docs/                                design doc, architecture diagram, process walkthrough (Word)
src/RetailPayments.Api/
  Program.cs                         pipeline: logging, error handler, migrate-on-start, Swagger, endpoint mapping
  Startup/ServiceCollectionExtensions.cs  service registration (AddImport, AddSources, AddApiServices)
  Endpoints/ApiEndpoints.cs          all HTTP routes (/api/*) and the dev scenario routes
  Endpoints/Dtos.cs                  JSON shapes returned by the API
  Import/
    ImportService.cs                 IImportService: orchestration, lock, batch bookkeeping, per-source isolation
    TransactionReconciler.cs         the business rules (compare snapshot vs DB, audit, validation)
    ImportResults.cs                 result/counter records
    ImportInProgressException.cs     thrown when an import is already running
    ImportOptions.cs                 "Import" config section (FinalizeAfterHours, SourceTimeoutSeconds)
  Sources/
    ITransactionSource.cs            contract each source adapter implements
    SourceTransaction.cs             a transaction as a source sends it
    SourcesOptions.cs                "Sources" config section and the Json/Mock mode
    JsonFile/JsonFileTransactionSource.cs reads one source's snapshot from a JSON file (the default)
    JsonFile/JsonSourceOptions.cs        the files and source names (Sources:Json)
    JsonFile/JsonScenario.cs             current step when several files are configured
    IScenario.cs                         step selector shared by the JSON and mock demos
    JsonFile/JsonSourceRegistration.cs   registers one JSON source per configured source system
    Mock/MockTransactionSource.cs    hard-coded feeds used for demo and tests
    Mock/MockScenario.cs             steps 0..3 that change the mock feeds between imports
  Domain/                            EF entities: Transaction, AuditEntry, ImportBatch, RejectedRecord; FieldLimits
  Data/PaymentsDbContext.cs          model configuration (indexes, cents conversion, lengths)
  Data/Migrations/                   EF Core migrations (checked in)
tests/RetailPayments.Tests/
  ImportHarness.cs                   real in-memory SQLite + fake clock + scripted sources
  ImportReconciliationTests.cs       one test per rule/edge case
  ApiTests.cs                        WebApplicationFactory integration tests (Mock mode)
  JsonModeApiTests.cs                the same app reading SampleData/step0..3.json, walked through the steps
  JsonSourceTests.cs                 parsing and failure cases of the JSON source
  TestApiFactory.cs                  shared web factory with a throwaway SQLite file
frontend/src/app/
  api.ts                             typed HttpClient wrapper and DTO interfaces
  app.ts / app.html / app.css        page shell: import button, banners, wiring
  transactions/transaction-grid.*    filters, sortable table, paging
  transactions/transaction-detail.*  detail and audit history popup
  imports/rejections-panel.*         rejected records of one import
  imports/recent-imports.*           recent batches table
  shared/                            UtcDatePipe, errorMessage()
  *.spec.ts                          Vitest tests (page with HttpTestingController, popup, pipe)
frontend/proxy.conf.json             dev proxy /api -> http://localhost:5080
```

## How an import works

```
caller (POST /api/import or future scheduler)
  -> ImportService.RunAsync(triggeredBy)         takes ImportLock, inserts ImportBatch (Running)
       for each ITransactionSource:
         FetchAsync()                            failure => recorded, source skipped, nothing revoked
         TransactionReconciler.Reconcile(...)    validate, diff, set status, write audit rows
         SaveChanges()                           one DB transaction per source
       completes the batch: Succeeded | PartiallySucceeded | Failed, with counters
```

Reconciliation rules (24h window is measured from `TransactionTime`, using an injected `TimeProvider`):

| Situation | Result | Audit |
|-----------|--------|-------|
| Unknown `(SourceSystem, TransactionId)` | insert `Active`, revision 1 | `Created` |
| Known key, values differ | update, `Revision++` | one `FieldChanged` per field (old/new) |
| `Active`, absent from snapshot, within 24h | `Revoked` | `Revoked` |
| `Revoked`, reappears within 24h | `Active` | `Restored` (+ field changes) |
| `Active`, older than 24h | `Finalized` (immutable) | `Finalized` |
| New row already older than 24h | created and finalized in the same run | `Created`, `Finalized` |
| `Revoked`, older than 24h | stays `Revoked` | none |
| Absent and older than 24h, or `Finalized` | ignored | none |
| Invalid record | not stored as a transaction; saved to `RejectedRecords` with reason and raw payload | none |

A rejected record still counts as present, so a malformed correction can never cause a revocation.
A failed fetch is never treated as an empty snapshot.

## Everyday tasks

### Add a real source system
1. Implement `ITransactionSource`: expose `SourceSystem` (the name stored on each row) and
   `GetSnapshotAsync(ct)`, which must return *everything* the source currently publishes for the window as
   `SourceTransaction` records. Use `HttpClient` via `IHttpClientFactory`; throw on transport/HTTP errors so
   the import records the failure instead of seeing an empty list (an empty list revokes recent transactions).
2. Register it in `Program.cs` in the source registration block:
   `builder.Services.AddSingleton<ITransactionSource, MyNewSource>();`
3. The JSON file sources are registered by `Sources:Mode = Json`. Skip that branch when you register real
   clients, and keep `Sources:Mode = Mock` only for the demo scenario.
4. Add a test with a scripted source in `ImportHarness` style.

### Change a reconciliation rule
Edit `TransactionReconciler`. Every rule has a test in `ImportReconciliationTests`; change or add the
test first, then the code. Keep it idempotent: identical input must produce zero audit rows.

### Change the database schema
Install the EF tool once (`dotnet tool install --global dotnet-ef`), then:
```bash
dotnet ef migrations add <Name> --project src/RetailPayments.Api
dotnet ef database update --project src/RetailPayments.Api      # optional, startup also migrates
```
Commit the generated files in `Data/Migrations/`. Never edit an applied migration. To reset local data,
delete `retailpayments.db` (and `-shm`/`-wal`).

### Trigger imports on a schedule
Nothing HTTP-specific is needed. Add a `BackgroundService` (or Hangfire/cron job) that resolves a scope,
gets `IImportService` and calls `RunAsync("Scheduler")`. `ImportLock` prevents overlap with manual runs.

### Load data from JSON files (default)
Each file in `src/RetailPayments.Api/SampleData/` holds the data of all three source systems at one moment: one
array of transactions per source, keyed by the source name (the names are in `appsettings.json` under
`Sources:Json:SourceSystems`):

```json
{
  "Store Uptown": [ ... ],
  "Store Downtown": [ ... ],
  "Online": [
    { "sourceSystem": "Online", "transactionId": 100001, "cardNumber": "***4444", "locationCode": "ONLINE",
      "productName": "Webcam", "amount": 54.00, "transactionTime": "now-6h" }
  ]
}
```

`Sources:Json:Files` lists the files. With one file the data is fixed. The sample config lists four
(`step0.json` to `step3.json`), which adds the **step selector** in the UI (and `POST /api/dev/scenario/{step}`):
step N makes every source read the Nth file. The files are the demo data, so you can open them and see exactly
what changes between steps.

`transactionTime` is either an ISO date (`2026-10-21T14:30:00Z`) or relative to app start (`now-6h`, `now-90m`).
Relative times are fixed at startup so repeated imports of unchanged data are identical.

A source with no entry in the file fails the import for that source (it is not treated as an empty list).
Broken JSON or a missing file does the same. Restart the app after editing `appsettings.json`; the JSON
files themselves are read on every import, so you can also edit a step file and import again.

### Walk through the rules with the steps
Pick a step in the UI's step selector (or `POST /api/dev/scenario/{step}`), then press **Run import now**:

| Step | What changes in the files | Result of the import |
|------|---------------------------|----------------------|
| 0 | baseline; Online 100004 is already >24h old; Downtown 100005 has no product name | 11 created, 1 finalized immediately, 1 rejected |
| 0 again | nothing | 11 unchanged, 1 rejected (idempotent) |
| 1 | Uptown 100002 corrected (card and amount); Downtown 100002 no longer listed; Online 100005 added | 1 created, 1 updated (2 audit rows), 1 revoked, 1 rejected |
| 2 | Downtown 100002 listed again; Online 100001 corrected; Online 100003 no longer listed | 1 restored, 1 updated, 1 revoked, 1 rejected |
| 3 | Online has no entry at all; Uptown 100009 has a 60-character product name | `PartiallySucceeded`: Online fails and nothing is revoked for it, 7 unchanged, 2 rejected |

Rejected records are listed under `GET /api/imports/{batchId}/rejections`, or via the "N rejected" link in the UI.
The same walk-through is an automated test (`JsonModeApiTests`), so these numbers are checked on every run.

### Try the rules with the built-in mock
Set `Sources:Mode` to `Mock` for the original in-code demo data (same four steps, but with the data written in
`MockScenario.cs` instead of files). The table below is for that mode.

| Step | Upstream change | Result of the next import |
|------|-----------------|---------------------------|
| 0 | baseline (one Online tx is already >24h old) | 11 created, 1 finalized immediately, 1 rejected |
| 0 again | nothing | 11 unchanged, 1 rejected (idempotent) |
| 1 | Uptown 100002 corrected; Downtown "Monitor Stand" 100002 not published; new Online 100005 | 1 created, 1 updated, 0 revoked, 1 rejected |
| 2 | Downtown 100002 published again; Online 100001 corrected; Online 100003 removed | 1 updated, 1 revoked, 0 restored, 1 rejected |
| 3 | Online returns 503; Uptown publishes a malformed record | `PartiallySucceeded`, 7 unchanged, 2 rejected, nothing revoked for Online |

The mock Downtown feed also contains a record with a blank product name that reuses id 100002, so every run
rejects one record. Because a rejected record still counts as present, it stops "Monitor Stand" 100002 from
being revoked at step 1 (and so there is nothing to restore at step 2). Remove that record from
`MockScenario.cs` to get the plain revoke/restore behaviour. Rejected records are listed under
`GET /api/imports/{batchId}/rejections`, or via the "N rejected" link in the UI.

## Testing

```bash
dotnet test                                   # reconciler rules (real SQLite) + API tests
cd frontend && npm test -- --watch=false      # Vitest tests
```

- Rule tests use `ImportHarness`: in-memory SQLite (a real relational engine, not the EF in-memory
  provider), a fake `TimeProvider` to move time past the 24h window, and scripted sources that can return
  data or throw.
- API tests use `WebApplicationFactory<Program>` (`Program` is `public partial` for this reason).
- Tests do not need Node or the frontend; the `NpmInstall` build target only runs in Debug when
  `frontend/node_modules` is missing and is non-fatal.

## API reference

Interactive docs: http://localhost:5080/swagger (Development).

| Method | Route | Purpose |
|--------|-------|---------|
| POST | `/api/import` | Run one import. `200` with per-source and total counts; `409` if one is already running |
| GET | `/api/imports?take=` | Recent import batches |
| GET | `/health` | Liveness plus a database check; `200 Healthy` or `503` |
| GET | `/api/imports/{batchId}/rejections` | Records rejected in that batch: reason and the payload as received |
| GET | `/api/transactions` | Paged list. Query: `sourceSystem`, `status`, `search`, `sortBy`, `sortDir`, `page`, `pageSize`. Unknown `sortBy` returns `400` |
| GET | `/api/transactions/{sourceSystem}/{transactionId}` | One transaction plus audit history; `404` if unknown |
| GET / POST | `/api/dev/scenario[/{step}]` | Mock-data control; only mapped when `Sources:Mode` is `Mock` |

Errors are `application/json` with `title` and `status` (unhandled exceptions return a generic 500 and are logged).

## Configuration

`src/RetailPayments.Api/appsettings.json` (override with `appsettings.Development.json`, environment
variables such as `Import__FinalizeAfterHours=48`, or user secrets):

| Key | Default | Meaning |
|-----|---------|---------|
| `ConnectionStrings:Default` | `Data Source=retailpayments.db` | SQLite file, relative to the working directory |
| `Import:FinalizeAfterHours` | `24` | Window after which a transaction is finalized (1 to 720; invalid values stop startup) |
| `Import:SourceTimeoutSeconds` | `30` | Per-source fetch timeout; a timed-out source counts as failed (1 to 600) |
| `Serilog` | console + daily file | Log levels and sinks; files go to `logs/retailpayments-<date>.log`, 14 days kept |
| `Sources:Mode` | `Json` | `Json` reads the files below; `Mock` uses built-in demo data and maps `/api/dev/*` |
| `Sources:Json:Folder` | `SampleData` | Folder for the JSON file (relative to the project folder, or absolute) |
| `Sources:Json:Files` | `step0.json` to `step3.json` | Data files; more than one adds the step selector (step N reads file N) |
| `Sources:Json:SourceSystems` | three names | One source is registered per name; each reads its own entry |
| `Database:MigrateOnStartup` | `true` | Apply pending migrations at startup |
| `Cors:AllowedOrigins` | `["http://localhost:4200"]` | Browser origins allowed to call the API |

Ports: API `5080` (`Properties/launchSettings.json`), Angular `4200`.

## Design notes

- **Single entry point.** `IImportService.RunAsync(triggeredBy)` is the whole import; HTTP is just one caller.
- **Layers.** `ITransactionSource` -> `ImportService` (orchestration) -> `TransactionReconciler` (pure rules)
  -> EF Core/SQLite. Endpoints only translate HTTP.
- **Data model.** `Transaction` (unique index on `(SourceSystem, TransactionId)`, `Status`, `Revision`,
  timestamps), `AuditEntry` (append-only, linked to its `ImportBatch`), `ImportBatch` (one per run:
  trigger, status, counters, per-source JSON detail), `RejectedRecord` (reason + raw payload per batch).
  Amounts are stored as integer cents (SQLite cannot order decimal text) and exposed as `decimal(18,2)`;
  all timestamps are UTC.
- **Idempotency.** Identical input writes no inserts, no field/status changes, no audit rows and no revision
  bumps; only a zero-count `ImportBatch` row. An in-process lock stops overlapping runs; the unique index is a backstop.
- **Atomicity.** One `SaveChanges` per source, so a source's changes commit together or not at all.
- **Frontend.** Small standalone components (grid, detail popup, rejections panel, recent imports) under a thin
  page shell, using signals; filtering, sorting and paging are server-side.

## Troubleshooting

| Symptom | Cause / fix |
|---------|-------------|
| `NETSDK1209` / "Visual Studio version does not support targeting .NET 10" | Use Visual Studio 2026 (v18) or build from a terminal |
| "Unable to locate the .NET SDK version '10.0.100'" | Install the .NET 10 SDK (see `global.json`) |
| UI shows "Cannot reach the API" | Start the API on :5080; the dev proxy forwards `/api` there |
| F5 does not start the UI | Node/npm not on PATH for Visual Studio, or `npm start` fails: run it manually in `frontend` and read the error |
| `POST /api/import` returns 409 | An import is already running (single-flight lock) |
| Odd data after changing the model | Delete `retailpayments.db*` and restart, or add a migration |
| Port 5080/4200 in use | Stop the other process or change `applicationUrl` / `ng serve --port` and `proxy.conf.json` |

## Assumptions and known limits
- **Field length:** the spec says strings are `< 20`, but its own sample product `"USB-C Docking Station"` is
  21 chars. Source system, card and location are capped at 19; `ProductName` is widened to 50
  (`FieldLimits.ProductName`). Confirm with the data owners.
- A snapshot is the *complete* set a source publishes for the window; an empty but successful snapshot revokes
  recent transactions.
- Single API instance with SQLite. For several instances replace `ImportLock` with a DB or distributed lock.
- The reconciler loads one source's rows into memory; fine at this scale, would need windowing at volume.
- `/api/dev/*` and the scenario dropdown exist only for demonstration with mock sources.
- Out of scope: authentication, real HTTP source clients, paging of audit history, the scheduler itself.
