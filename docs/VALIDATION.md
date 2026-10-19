# Validation

Local verification: **19 October 2026**, using Windows 11, .NET SDK 10.0.402, and Docker with Linux containers.

## Results

| Check | Result |
|---|---|
| Release tests on Windows and Linux | 20 passed on each platform |
| Two-Agent registration and MinIO deployment | Passed |
| MinIO readiness and S3 upload/download | Passed on both targets |
| Repeat deployment | Passed with `changed=0` |
| Master and MinIO restart persistence | Passed |
| Offline detection, queued cancellation, and Agent recovery | Passed |
| Authentication, job ownership, and known-secret redaction | Passed |
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
```

Use a disposable demo: these checks create deployments, restart services, and temporarily stop Agent B.

These are local results. Check GitHub Actions separately for CI results. Production load, penetration, and disaster-recovery testing remain future work.
