"""Verify published self-contained Agents can enroll and report native OS metrics."""
import os
from pathlib import Path
import subprocess
import tempfile
import uuid
from smoke_test import Demo, ROOT
from demo_support import compose_configuration, published_port


def main():
    config = compose_configuration()
    demo = Demo(f'https://localhost:{published_port(config, "master", 8080)}')
    demo.login()

    checked = 0

    def verify(name):
        def sampled():
            hosts = demo.call("/api/admin/dashboard")["hosts"]
            return next((h for h in hosts if h["hostname"] == name and h.get("metrics") and h["metrics"]["cpuPercent"] is not None), None)
        host = demo.wait(sampled, timeout=60)
        assert host["metrics"]["memoryTotalBytes"] > 0 and host["metrics"]["diskTotalBytes"] > 0
        print(f'PASS: {name} enrolls over HTTPS and reports CPU, memory, disk, and IP')
        return host["id"]

    def retire(name):
        for host in demo.call("/api/admin/dashboard")["hosts"]:
            if host["hostname"] == name:
                demo.call(f'/api/admin/hosts/{host["id"]}/deregister', {})

    windows = ROOT / "artifacts/win-x64/DepPilot.Agent.exe"
    if os.name == "nt" and windows.exists():
        name = "native-windows-" + uuid.uuid4().hex[:8]
        with tempfile.TemporaryDirectory(prefix="dep-pilot-native-") as temporary:
            env = dict(os.environ, Agent__MasterUrl=demo.base, Agent__Hostname=name,
                       Agent__StateDirectory=temporary, Agent__HeartbeatSeconds="2",
                       Agent__ServerCertificateFile=str(ROOT / "secrets/master.crt"),
                       DEP_PILOT_BOOTSTRAP_TOKEN_FILE=str(ROOT / "secrets/bootstrap-token.txt"),
                       DEP_PILOT_MINIO_PASSWORD_FILE=str(ROOT / "secrets/minio-password.txt"))
            with open(Path(temporary) / "console.log", "w") as output:
                process = subprocess.Popen([str(windows)], cwd=ROOT, env=env, stdout=output, stderr=output,
                                           creationflags=subprocess.CREATE_NO_WINDOW)
                try:
                    verify(name)
                    checked += 1
                    assert (Path(temporary) / "agent.log").exists()
                finally:
                    process.terminate()
                    process.wait(timeout=15)
                    retire(name)

    linux = ROOT / "artifacts/linux-x64/DepPilot.Agent"
    if linux.exists():
        name = "native-linux-" + uuid.uuid4().hex[:8]
        command = ["docker", "run", "-d", "--rm", "--name", name, "--network", config["networks"]["control"]["name"],
                   "--mount", f"type=bind,source={linux.parent},target=/agent,readonly",
                   "--entrypoint", "/agent/DepPilot.Agent"]
        for filename in ("master.crt", "bootstrap-token.txt", "minio-password.txt"):
            command.extend(["--mount", f'type=bind,source={ROOT / "secrets" / filename},target=/run/demo/{filename},readonly'])
        settings = dict(Agent__MasterUrl="https://master:8080", Agent__Hostname=name,
                        Agent__StateDirectory="/tmp/native-state", Agent__HeartbeatSeconds="2",
                        Agent__ServerCertificateFile="/run/demo/master.crt",
                        DEP_PILOT_BOOTSTRAP_TOKEN_FILE="/run/demo/bootstrap-token.txt",
                        DEP_PILOT_MINIO_PASSWORD_FILE="/run/demo/minio-password.txt")
        for key, value in settings.items():
            command.extend(["-e", key + "=" + value])
        try:
            subprocess.run(command + ["dep-pilot/agent:local"], cwd=ROOT, check=True, stdout=subprocess.DEVNULL)
            verify(name)
            checked += 1
        finally:
            subprocess.run(["docker", "rm", "-f", name], cwd=ROOT, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
            retire(name)
    if checked == 0:
        raise RuntimeError("Publish the Agents first: python scripts/publish_agents.py")
    print("Native binary checks passed. Service-manager installation and Windows WSL execution require their own target-host validation.")


if __name__ == "__main__":
    main()
