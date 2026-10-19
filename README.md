# Dep Pilot

A .NET deployment system that manages hosts and deploys standalone MinIO instances through Ansible. View host status, deployment results, logs, and audit history from one dashboard.

## How it works

1. Agents register with the Master and send heartbeats.
2. An administrator selects hosts and starts a deployment.
3. Each Agent runs the approved playbook locally and reports its result.

**Stack:** ASP.NET Core 10, .NET Worker Service, Razor Pages, SQLite, Ansible, and Docker Compose. ASP.NET Core 10 satisfies the assessment's 8+ requirement.

## Preview

![Dep Pilot dashboard](docs/screenshots/dashboard.jpg)

[View deployment logs and audit screenshots](docs/SCREENSHOTS.md).

## Try the demo

Requires Docker running Linux containers, Docker Compose v2.20+, Python 3.10+, and internet access for the first build. Allow about 8 GB of memory for Docker. A local .NET SDK is not required for this demo.

From the project folder:

```sh
python scripts/start_demo.py
```

The first build can take several minutes. It creates local credentials and starts a Master with two simulated Linux hosts.

1. Open **http://localhost:8080**.
2. Sign in as **admin** using `secrets/admin-password.txt`.
3. Wait for both hosts to appear online.
4. Select the hosts, name the deployment, and run the MinIO playbook.
5. Open the job logs to inspect the result.

After deployment:

| Host | MinIO S3 API | Console |
|---|---|---|
| warehouse-a | http://localhost:19000 | http://localhost:19001 |
| warehouse-b | http://localhost:29000 | http://localhost:29001 |

MinIO username: `dep-pilot`. Password: `secrets/minio-password.txt`. Each installation generates its own credentials. Copy `.env.example` to `.env` to change ports.

Stop the demo with `docker compose stop`; stored data is preserved.

## Tests

With the .NET 10 SDK:

```sh
dotnet test DepPilot.sln -c Release
```

See [validation](docs/VALIDATION.md) for Docker tests and full demo checks.

## Documentation

| Document | Contents |
|---|---|
| [Architecture](docs/ARCHITECTURE.md) | Components and system flow |
| [API](docs/API.md) | Endpoints and authentication |
| [Configuration](docs/CONFIGURATION.md) | Ports, credentials, and settings |
| [Operations](docs/OPERATIONS.md) | Commands and troubleshooting |
| [Security](docs/SECURITY.md) | Controls and limitations |
| [Validation](docs/VALIDATION.md) | Test results and commands |
| [TODO](docs/TODO.md) | Ideas for industrial use |

This is a local reference demo with one Master and an approved MinIO playbook. Production improvements are listed in TODO. The Docker mock uses privileged engine sidecars; review the security notes before changing network exposure.

Original code is rights reserved; see [LICENSE-NOTICE.md](LICENSE-NOTICE.md). Third-party licenses are listed in [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).
