# Agent service installation

The Docker demo is the quickest evaluation path. Native Agents use the same APIs and credentials.

## Publish self-contained binaries

With the .NET 10 SDK on the build machine:

```sh
python scripts/publish_agents.py
```

Output: `artifacts/linux-x64/` and `artifacts/win-x64/`. Targets need the OS native libraries and execution tools, but no installed .NET runtime.

## Linux service

1. Install Docker, Python, and a Python venv at `/opt/dep-pilot/ansible`. Install `ansible-core==2.19.3` and `requests==2.32.5` in that venv.
2. Install `playbooks/requirements.yml` with `ansible-galaxy collection install -r playbooks/requirements.yml -p /opt/dep-pilot/collections` using the venv's executable.
3. Copy the Linux publish output to `/opt/dep-pilot`. Create a dedicated `dep-pilot` service account and `/var/lib/dep-pilot` owned by that account, mode `0700`.
4. Copy `deploy/agent.env.example` to `/etc/dep-pilot/agent.env`; set the Master URL and protected secret/certificate paths. Grant the service account read access to those files and authorized access to Docker.
5. Build/load `dep-pilot/minio:2025-10-15` on the target engine. See `docker/Minio.Dockerfile`.
6. Copy `deploy/dep-pilot-agent.service` to `/etc/systemd/system/`, then run:

```sh
sudo systemctl daemon-reload
sudo systemctl enable --now dep-pilot-agent
sudo systemctl status dep-pilot-agent
```

## Windows service

1. Prepare an account with a WSL Ubuntu distro, Docker access, and Ansible/Requests/the Docker collection installed inside that distro. Ansible runs locally through WSL.
2. Copy the Windows publish output to a permanent install directory. Copy `deploy/windows-appsettings.json.example` there as `appsettings.json` and edit its paths and Master URL.
3. Give that account access to the state directory, secret files, certificate, and its WSL distro. Restrict state and secret-file ACLs to the service account and trusted administrators.
4. Preload the MinIO image in the Docker engine reachable from WSL. In elevated PowerShell:

```powershell
$account = Get-Credential
.\deploy\install-windows-service.ps1 -InstallDirectory 'C:\DepPilot\Agent' -ServiceCredential $account
Start-Service DepPilotAgent
```

Use the account that owns the configured WSL distro. The supplied service template does not install a distro or configure service logon rights automatically.

## Logs and retirement

Agent operational logs are stored as `agent.log` in its state directory, with one rotated file. Deployment output is available in the dashboard.

Stop a native service before using the [retirement helper](OPERATIONS.md#deregister-a-host). Application data and deployment history are preserved.

See [validation](VALIDATION.md) for what has been tested; binary smoke tests do not replace installation testing under systemd or Windows Service Control Manager.
