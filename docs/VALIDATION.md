# Validation

Local verification: **20 October 2026**, using Windows 11, .NET SDK 10.0.402, and Docker with Linux containers.

## Results

| Check | Result |
|---|---|
| Release tests on Windows and Linux | 31 passed on each platform |
| Two-Agent registration, IP/capabilities, and resource metrics | Passed |
| MinIO deployment on both targets | Passed |
| MinIO readiness and S3 upload/download | Passed on both targets |
| Repeat deployment | Passed with `changed=0` |
| Master and MinIO restart persistence | Passed |
| Offline detection, queued cancellation, and Agent recovery | Passed |
| Authentication, job ownership, and known-secret redaction | Passed |
| HTTPS trust and hostname rejection | Passed |
| First-install and update rollback, preserving data | Passed |
| Deregistration, pending cleanup, and local credential retirement | Passed |
| Configurable bind storage and rollback | Passed |
| Dashboard selection, Busy hosts, logs, and audit | Passed in browser; no console warnings/errors |
| Form state, auto-refresh, duplicate submission, and connection recovery | Passed in local DOM checks |
| Self-contained Windows/Linux binary enrollment and metrics | Passed |
| Image builds, Compose, and script syntax checks | Passed |
| NuGet vulnerability audit | No known vulnerabilities reported at verification time |

## Run the checks

```sh
dotnet test DepPilot.sln -c Release
```

For a Docker-based test run without a local .NET SDK:

```sh
docker build -f docker/Tests.Dockerfile -t dep-pilot/tests:local .
```

Test the running demo:

```sh
python scripts/start_demo.py
python scripts/smoke_test.py
python scripts/failure_scenarios.py
python scripts/rollback_test.py
python scripts/security_lifecycle_test.py
```

Use a disposable demo: these checks create deployments, restart services, and temporarily stop Agent B.

To build and check self-contained binaries with the .NET 10 SDK:

```sh
python scripts/publish_agents.py
python scripts/native_agent_test.py
```

Windows Service Control Manager/systemd installation and Windows WSL deployment execution have not been end-to-end validated on this machine. Native binary tests cover HTTPS enrollment, resource reporting, and Windows local logging.

These are local results. Check GitHub Actions separately for CI results. Production load, penetration, and disaster-recovery testing remain future work.
