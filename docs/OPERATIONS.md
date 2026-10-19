# Operations

Run commands from the project folder with Docker running in Linux-container mode.

## Start and use

```sh
python scripts/start_demo.py
```

1. Open `http://localhost:8080` (or your configured port).
2. Sign in as `admin` with `secrets/admin-password.txt`.
3. Wait for both Agents to appear online.
4. Select hosts, name the deployment, and run the MinIO playbook.
5. Check deployment status, logs, and audit history in the dashboard.

## Useful commands

| Action | Command |
|---|---|
| Check services | `docker compose ps` |
| Follow logs | `docker compose logs -f master agent-a agent-b` |
| Inspect MinIO on target A | `docker compose exec docker-host-a docker ps -a` |
| Stop, keeping data | `docker compose stop` |
| Restart existing services | `docker compose up -d` |

MinIO runs inside each simulated host's Docker engine. If an engine volume is recreated, run the start script again to reload its image.

## Common issues

| Problem | What to check |
|---|---|
| Docker unavailable | Start Docker Desktop and enable Linux containers |
| Dashboard unavailable | Master logs and configured port |
| Agent missing or offline | Agent logs, Master address, and enrollment credentials |
| Deployment stays pending | Target Agent is online and available |
| Deployment fails | Job logs, target Docker engine, image, ports, and storage |
| Access returns 401 | Incorrect or revoked credentials |

## Recovery and data

- Cancellation stops execution but does not undo changes already applied. Inspect the target before retrying a failed or timed-out job.
- An online Agent does not guarantee the deployed application is healthy.
- Back up the Master database and cookie keys together while the Master is stopped. Protect Agent identities and back up MinIO data separately.
- Removing Docker volumes deletes stored data and identities. Log retention and automatic rollback are not implemented.

See [configuration](CONFIGURATION.md) for ports and settings, or [validation](VALIDATION.md) to test the demo.
