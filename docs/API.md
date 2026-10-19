# HTTP API reference

Base URLs: `https://localhost:8080` for the local demo; an HTTPS origin for a real deployment. JSON properties use camelCase; job statuses are strings. Timestamps are UTC Unix milliseconds. API errors use HTTP status codes and, for domain validation, Problem Details with a `detail` field.

## Authentication

| Surface | Authentication |
|---|---|
| `POST /api/agents/register` | Bootstrap token as `Authorization: Bearer ...` |
| `/api/agent/*` | Individual Agent token as `Authorization: Bearer ...` |
| `/api/admin/*` | Admin login cookie; mutation requests also require `X-CSRF-TOKEN` |
| `/healthz`, `/Login` | Anonymous |


## Enrollment

`POST /api/agents/register`

```json
{
  "installationId": "b9aeb700-f4ca-463f-8f65-ec970322df67",
  "hostname": "warehouse-a",
  "operatingSystem": "Linux",
  "agentVersion": "1.1.0",
  "ipAddress": "10.0.0.10",
  "capabilities": ["linux", "ansible", "docker"],
  "token": "<64 hexadecimal characters generated and stored by the Agent>"
}
```

## Agent endpoints

| Method | Path | Request / response |
|---|---|---|
| POST | `/api/agent/heartbeat` | `agentVersion`, optional `metrics`, `ipAddress`, `capabilities`; 204 |
| POST | `/api/agent/deregister` | Retire this Agent; 204, safe to retry |
| POST | `/api/agent/jobs/claim` | `{}`; 204 if no work, otherwise assignment JSON |
| GET | `/api/agent/jobs/{jobId}/playbook` | UTF-8 YAML for an owned running job |
| POST | `/api/agent/jobs/{jobId}/lease` | `{ "attemptId": "..." }`; `{ "cancelRequested": false }` |
| POST | `/api/agent/jobs/{jobId}/logs` | `{ "attemptId": "...", "sequence": 0, "text": "..." }`; 204 |
| POST | `/api/agent/jobs/{jobId}/complete` | Completion JSON; 204 |

Assignment:

```json
{
  "jobId": "<guid>",
  "attemptId": "<guid>",
  "playbookId": "<guid>",
  "playbookName": "Deploy standalone MinIO",
  "version": "1.0.0",
  "sha256": "<SHA-256 hex>",
  "timeoutSeconds": 600
}
```

Completion:

```json
{
  "attemptId": "<guid>",
  "status": "Succeeded",
  "exitCode": 0,
  "summary": "Playbook completed and MinIO readiness check passed."
}
```


Metrics contain `cpuPercent` (null until the second sample), `memoryUsedBytes`, `memoryTotalBytes`, `diskFreeBytes`, `diskTotalBytes`, and UTC `sampledAt`. Dashboard hosts include these values plus IP and capabilities. Resource values describe the Agent's OS view; they are not continuous workload health checks.

## Admin endpoints

| Method | Path | Purpose |
|---|---|---|
| GET | `/api/admin/csrf` | Obtain mutation token |
| GET | `/api/admin/dashboard` | Hosts, approved playbooks, latest 200 jobs |
| POST | `/api/admin/deployments` | Create one job per selected host |
| POST | `/api/admin/jobs/{jobId}/cancel` | Cancel pending work or request running cancellation |
| POST | `/api/admin/hosts/{hostId}/revoke` | Revoke credential and terminate active job records |
| POST | `/api/admin/hosts/{hostId}/deregister` | Retire host, cancel pending work, retain history; 409 while running |
| GET | `/api/admin/jobs/{jobId}/logs` | Ordered persisted output |
| GET | `/api/admin/audit` | Latest 200 audit events |

Create deployment:

```json
{
  "name": "UAT storage rollout",
  "playbookId": "<approved playbook guid>",
  "hostIds": ["<host-a guid>", "<host-b guid>"]
}
```
