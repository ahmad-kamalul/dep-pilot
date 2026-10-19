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

Compose supplies its own Master address and enables HTTP for the local demo. Use HTTPS outside that demo. Full settings are listed in [MasterOptions](../src/DepPilot.Master/Services/MasterOptions.cs), [AgentOptions](../src/DepPilot.Agent/AgentOptions.cs), and [compose.yaml](../compose.yaml).

## Data and playbooks

Docker volumes preserve the Master database, Agent identities, and MinIO data. Keep each Agent's identity unique.

The Master includes one approved MinIO playbook. Changing its content requires a new version in the Master seed configuration; playbook upload/edit is not available in the UI.
