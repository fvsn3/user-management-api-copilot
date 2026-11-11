# Verification record

Executed on **11 November 2026** by Codex while preparing the assignment. These checks do not establish use of Microsoft Copilot or GitHub Copilot.

## Environment and build

- Windows x64, .NET SDK **10.0.404**, ASP.NET Core runtime **10.0.15**.
- The original reference ZIP targeted .NET 10 but had not been compiled. Its CRUD, validation, storage, and middleware logic was retained.
- Added the public `Program` test entry point, isolated test project, solution, documentation, and CI workflow. The application project excludes the nested test sources and build content.
- NuGet restore completed successfully. This workstation required moving NuGet caches into the writable workspace and using a single MSBuild worker.
- Release build succeeded. An initial xUnit style warning in a test assertion was corrected; the final Release test rebuild emitted no warnings.

## Integration tests

**31 passed, 0 failed, 0 skipped.**

The final command, with the local SDK on PATH and workspace cache variables set, was:

```powershell
dotnet test UserManagementApi.sln --configuration Release --no-restore -m:1 -nr:false /p:UseSharedCompilation=false --logger trx --results-directory TestResults
```

The suite covers real application routing and middleware with `WebApplicationFactory`: complete CRUD, readback after changes, trimming, invalid fields and JSON bodies, 415 content type, 404 IDs/routes, email conflicts without unintended changes, authentication, ID stability, concurrent requests, and custom log output. Each test creates a fresh application/store and a generated test token.

Local TRX files are in the Git-ignored `TestResults` directory. They are excluded from the submission because they contain machine-specific paths and metadata. GitHub Actions is configured to publish its own TRX artifact when the workflow runs.

## Live HTTP verification

In addition to the integration suite, separate copies ran on actual local Kestrel servers. The original application source remained unchanged (hash checked). Both servers were stopped after verification.

**37 assertions passed across 28 HTTP requests.** See [sanitized request/response evidence](LIVE_HTTP_RESULTS.json).

| Check | Observed result |
| --- | --- |
| Controlled copy with intentional `_users.Count + 1` defect | After creating IDs 1 and 2 and deleting 1, the next POST returned safe Problem JSON 500; ID 2 remained unchanged |
| Corrected copy using the final `_nextId++` implementation | Same sequence returned 201, ID 3, and Location `/api/users/3`; ID 2 remained unchanged |
| Duplicate email with different case | 409, records unchanged |
| Invalid PUT | 400, existing record unchanged |
| Malformed JSON | 400, not 500 |
| Missing or incorrect token | 401, no unauthorized insert |
| Valid PUT and readback | 204, then GET showed the updated values |
| Missing ID and noninteger route | 404 |
| Custom middleware logs | Final 201, 400, and controlled 500 status with duration; separate 401 entry; generated token absent from logs |

A separate startup check without a configured token exited with a nonzero code and the expected diagnostic. Its first attempt timed out while Windows handled the unhandled exception; the repeat with crash-report dialogs suppressed for that child process completed successfully. This did not change the API or machine-wide settings.

The deliberately defective copy is outside the submitted project. It is a labeled teaching experiment, not an undisclosed defect in the final API. These before/after results were produced by Codex before the learner's Copilot session.

## GitHub publication and hosted verification

Published on 11 November 2026 to the public repository [fvsn3/user-management-api-copilot](https://github.com/fvsn3/user-management-api-copilot). The initial source commit is `de719c225babd8814d52c89dac08f2160ecc0703`.

The [first GitHub Actions run](https://github.com/fvsn3/user-management-api-copilot/actions/runs/43033769253) completed successfully on Ubuntu 26.04.1 with .NET SDK 10.0.404: restore succeeded, Release build produced **0 warnings and 0 errors**, and **31 tests passed, 0 failed, 0 skipped**. The workflow uploaded its TRX test-results artifact. Subsequent workflow status is available from the README badge.

## Remaining submission steps

- Complete the real Copilot interaction and update [COPILOT_DEBUGGING.md](COPILOT_DEBUGGING.md).
- Submit the verified repository URL through Coursera. No Coursera submission has been made as part of this verification record.
