# Deployment packages

An approved package consists of a JSON manifest in `packages/` and a local YAML playbook in `playbooks/`.

The MinIO example includes:

- `packages/minio.json`: name, immutable version, playbook filename, and prerequisites.
- `playbooks/minio.yml`: configuration checks, Docker/image checks, installation, readiness, and rollback.
- `playbooks/requirements.yml`: required Ansible collections.

## Add an application

1. Copy `packages/application.json.example` to a new `.json` file.
2. Copy `playbooks/application.yml.example` to a `.yml` file and implement prerequisites, installation, readiness, and safe rollback. The template intentionally refuses execution until completed.
3. Set the manifest's playbook filename and version. Install its required tools in the Agent runtime.
4. Review the changes and rebuild the Master. It imports the approved package and verifies that existing version content has not changed.

The latest version is available for new deployments; old versions remain in deployment history. There is no arbitrary playbook upload endpoint.

The runner supplies MinIO variables for the sample workload. Other playbooks must define their own non-secret defaults or use protected Agent-side configuration; arbitrary per-deployment parameter editing is not implemented.

## MinIO recovery

On a normal Ansible installation/readiness failure, the playbook restores the previous managed container image, environment, ports, mounts, and restart policy. A failed first installation removes its container. Persistent storage is retained, and the job remains **Failed**.

This restores container configuration, not object contents or storage formats. Cancellation, process termination, or host failure can interrupt recovery; inspect the target before retrying.
