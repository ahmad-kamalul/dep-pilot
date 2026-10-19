# Security model

## Trust boundaries

Administrators are trusted to select approved deployment actions. The Master is trusted to distribute playbooks. An Agent is trusted to execute them on its target host, which grants substantial deployment capability. A compromised Master or trusted playbook can affect all enrolled targets; content hashing alone does not change that trust relationship.

The reference platform is intended for local evaluation. Before operational deployment, apply the controls appropriate to the target network, data, and service requirements.

## Implemented controls

| Risk | Control |
|---|---|
| Anonymous administrative access | Admin cookie authentication; password verification using ASP.NET Core PasswordHasher |
| Browser CSRF | Razor form antiforgery and mandatory `X-CSRF-TOKEN` on admin API mutations |
| Agent impersonating administrator | Separate authentication schemes; bearer Agent tokens do not authorize admin endpoints |
| One Agent altering another's job | Host identity comes from authentication; ownership enforced on every job resource |
| Installation takeover | Enrollment replay requires the existing per-installation token; conflicting credentials rejected |
| Database credential disclosure | Per-Agent random tokens are stored as SHA-256 hashes |
| Transport interception | HTTPS in Compose and native mode; certificate and hostname validation; redirects disabled on Agent HTTP client |
| Arbitrary shell injection | Fixed executable plus `ProcessStartInfo.ArgumentList`, no shell |
| Changed playbook in transit | SHA-256 check before writing or starting an execution |
| Unbounded process execution | Job timeout, renewable lease, cancellation, process-tree termination |
| Log flooding | Bounded output capture, chunk size limit, and per-job log quota |
| Sensitive job output | Ansible `no_log` on credential-bearing tasks; known credential and JSON-escaped-value redaction |
| Exposed temporary credentials | Private job directory and variables file, cleanup after execution |
| Untraceable changes | Persisted actors, job transitions, playbook versions, and audit events |
| Untrusted browser rendering | Text-only DOM insertion, same-origin CSP, frame denial, nosniff, no-store |
| Basic login/enrollment abuse | IP-partitioned request rate limits |

## Demo and recovery boundaries

- The demo generates a private certificate; only its public certificate is supplied to Agents. Browser trust is a local setup step. The Agent uses a random opaque bearer token, not a JWT; admin access uses a cookie and CSRF protection.
- Privileged Docker-in-Docker sidecars simulate target hosts. Docker access is a host-administration permission; keep the demo on a trusted machine with loopback-bound published ports.
- MinIO rollback restores managed container configuration and preserves volumes; it does not reverse application writes, storage-format changes, cancellation, or host crashes.
- Deregistration revokes credentials, removes live inventory/metrics, and cancels pending work. Identity tombstones and job/audit history remain for traceability.
- Review workload maintenance and third-party licensing before production use. SSO, credential rotation, encrypted key storage, and production recovery drills remain future work.
