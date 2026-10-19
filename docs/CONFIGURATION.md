# Configuration

The Docker demo comes preconfigured. Run `python scripts/start_demo.py` to generate local credentials and start it.

## Ports

Copy `.env.example` to `.env` to change these defaults:

| Setting | Default |
|---|---|
| `MASTER_PORT` | `8080` |
| `MINIO_A_API_PORT` / `MINIO_A_CONSOLE_PORT` | `19000` / `19001` |
| `MINIO_B_API_PORT` / `MINIO_B_CONSOLE_PORT` | `29000` / `29001` |

## Credentials

Sign in as `admin` using `secrets/admin-password.txt`. MinIO uses `dep-pilot` and the password in `secrets/minio-password.txt`. Enrollment uses `secrets/bootstrap-token.txt`.

Credentials are generated locally and preserved on later runs. Keep `.env` and `secrets/` out of Git.

## Application settings

Use environment variables with `__` between sections and names.

| Variable | Purpose | Default |
|---|---|---|
| `Master__DatabasePath` | Master database location | `data/dep-pilot.db` |
| `Master__OfflineAfterSeconds` | Mark a host offline after missed heartbeats | `60` |
| `Master__JobTimeoutSeconds` | Maximum job duration in seconds | `600` |
| `Agent__MasterUrl` | Master address | `https://localhost:7443` |
| `Agent__StateDirectory` | Agent identity and pending results | `state` |
| `Agent__PollSeconds` | Check for jobs every N seconds | `5` |
| `Agent__HeartbeatSeconds` | Send a heartbeat every N seconds | `15` |

Compose uses HTTPS and a generated local certificate. Agents trust the public certificate through `Agent__ServerCertificateFile`; normal installations can use the OS certificate trust store. Full settings are listed in [MasterOptions](../src/DepPilot.Master/Services/MasterOptions.cs), [AgentOptions](../src/DepPilot.Agent/AgentOptions.cs), and [compose.yaml](../compose.yaml).

Additional Agent settings:

| Variable | Purpose |
|---|---|
| `Agent__MinioStoragePath` | Optional absolute directory on the target Docker host; empty uses a named volume |
| `Agent__ServerCertificateFile` | Public certificate for a private/demo CA |
| `Agent__WslDistribution` | Windows execution distro; default `Ubuntu` |

For a bind-mounted MinIO directory, create it on the target first and grant UID/GID `10001` write access. Ports and credentials remain configurable through Agent settings and secret files.

## Data and playbooks

Docker volumes preserve the Master database, Agent identities, and MinIO data. Keep each Agent's identity unique.

Approved applications are defined in `packages/*.json`. Changing a playbook requires a new package version. See [packages](PACKAGES.md); upload/edit is not available in the UI.
