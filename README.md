# User Management API

ASP.NET Core Minimal API for the Coursera **Building a Simple API with Copilot** project. It provides user CRUD operations, input validation, duplicate-email protection, and error-handling, authentication, and logging middleware.

The API uses .NET 10 and an in-memory store. No database or paid service is required. This project builds on the reference example supplied with my course study notes. Integration tests and submission documentation were prepared with Codex assistance. The separate Copilot debugging activity is documented accurately below.

## Assignment criteria

| Criterion | Where to review |
| --- | --- |
| Public GitHub repository | This repository and its source files |
| GET, POST, PUT, DELETE | Route handlers in [Program.cs](Program.cs) |
| Copilot-assisted debugging | [Debugging evidence](docs/COPILOT_DEBUGGING.md) — **pending the actual Copilot session** |
| Input validation | `Validate`, `UserInput`, and duplicate-email checks in `UserStore` |
| Middleware | Three `app.Use` components in `Program.cs`: errors, bearer-token check, request logging |

## Run locally

Install the [.NET 10 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/10.0). Open PowerShell in the repository directory:

```powershell
dotnet restore UserManagementApi.sln
dotnet build UserManagementApi.sln --no-restore
$env:DemoAuth__Token = [guid]::NewGuid().ToString('N')
$env:DemoAuth__Token
dotnet run --project UserManagementApi.csproj --no-build --urls http://localhost:5080
```

Keep the generated token locally for the second terminal. Do not commit it or include it in screenshots. Set the environment variable in the same terminal that starts the server. The application fails at startup with a clear message if it is missing. This HTTP address is for local practice; the application is not deployed as a public service.

In a second PowerShell terminal:

```powershell
$token = Read-Host 'Paste the token from the server terminal'
$headers = @{ Authorization = "Bearer $token" }
$uri = 'http://localhost:5080/api/users'

# Read the collection; initially []
Invoke-RestMethod -Uri $uri -Headers $headers

# Create a user and capture its server-assigned ID
$body = @{ name = 'Ada Lovelace'; email = 'ada@example.com' } | ConvertTo-Json
$user = Invoke-RestMethod -Method Post -Uri $uri -Headers $headers -ContentType 'application/json' -Body $body

# Read, update, and delete that user
Invoke-RestMethod -Uri "$uri/$($user.id)" -Headers $headers
$body = @{ name = 'Ada Byron'; email = 'ada@example.com' } | ConvertTo-Json
Invoke-RestMethod -Method Put -Uri "$uri/$($user.id)" -Headers $headers -ContentType 'application/json' -Body $body
Invoke-RestMethod -Method Delete -Uri "$uri/$($user.id)" -Headers $headers
```

[requests.http](requests.http) provides examples for an HTTP client. Copy it to `requests.local.http` (ignored by Git), set the local token there, and use the actual IDs returned by POST.

## HTTP contract

All routes require `Authorization: Bearer <local-token>`.

| Method | Route | Success | Other expected responses |
| --- | --- | --- | --- |
| GET | `/api/users` | 200, JSON array | 401 |
| GET | `/api/users/{id}` | 200, user | 401, 404 |
| POST | `/api/users` | 201, user and `Location` header | 400, 401, 409, 415 |
| PUT | `/api/users/{id}` | 204, empty body | 400, 401, 404, 409, 415 |
| DELETE | `/api/users/{id}` | 204, empty body | 401, 404 |

POST and PUT accept this JSON shape:

```json
{ "name": "Ada Lovelace", "email": "ada@example.com" }
```

- `name` is required and must contain 2–100 characters after trimming.
- `email` is required, at most 254 characters after trimming, and checked with `EmailAddressAttribute`. Format validation does not verify mailbox existence.
- Emails are unique without regard to case. A duplicate returns 409 without changing existing records.
- The server assigns IDs. Deleting a user does not renumber others or reuse an ID during that process.
- Missing fields, empty bodies, JSON `null`, and malformed JSON return 400. The route constraint rejects `/api/users/abc` with 404.
- PUT validates the body before looking up the ID; an invalid body for an absent ID therefore returns 400.

Field validation returns Problem JSON with an `errors` dictionary. Authentication and exception handling also return Problem JSON. Missing records return an empty 404, while an email conflict returns `{"error":"Email already exists."}`.

## Middleware and design

The request pipeline is **error handling → token check → logging → endpoint**. The error handler preserves bad-request status codes and converts unexpected exceptions to a generic 500 without exposing a stack trace. An invalid or missing token stops the pipeline with 401 and a `WWW-Authenticate: Bearer` header.

Authenticated requests log method, path, final status, and elapsed milliseconds using `OnCompleted`. Rejected requests have a separate 401 log. The custom middleware log entries do not contain tokens, bodies, or query strings; framework hosting logs have their own output. A disconnected client can prevent a completion callback, so this is demonstration logging rather than a durable audit trail.

`UserStore` is a singleton with immutable records and a lock around each operation. Email uniqueness and ID generation are checked inside that lock. Data is kept only in memory: restarting the process clears users and resets the counter. The shared token demonstrates middleware; it does not implement user identities, roles, JWT validation, token issuance, or expiry.

## Automated verification

```powershell
dotnet test UserManagementApi.sln
```

The xUnit integration suite in [tests/UserManagementApi.Tests](tests/UserManagementApi.Tests) hosts the real API with `WebApplicationFactory`. Each test gets an isolated store and an ephemeral test token. It checks the CRUD lifecycle, validation without unintended writes, invalid JSON, missing IDs, duplicate emails, authentication, ID stability after deletion, concurrent requests, and final-status logging.

Actual build and test results are recorded in [VALIDATION.md](docs/VALIDATION.md). GitHub Actions also runs restore, build, and tests on pushes and pull requests.

## Copilot debugging activity

The actual Copilot session is still required. The [Croatian step-by-step exercise](docs/COPILOT_EXERCISE_HR.md) contains a ready-to-send prompt for a clearly labeled controlled bug: using the remaining user count to generate IDs. The [evidence record](docs/COPILOT_DEBUGGING.md) must be completed from the real conversation and before/after checks. Codex assistance and a prepared prompt are not presented as Copilot usage.
