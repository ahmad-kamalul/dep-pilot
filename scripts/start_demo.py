"""Build the stack and import the pinned MinIO image into each isolated demo host."""
from pathlib import Path
import shutil
import subprocess
from init_secrets import initialize
from init_tls import initialize_tls
from demo_support import compose_configuration, published_port

ROOT = Path(__file__).resolve().parents[1]


def run(*arguments):
    subprocess.run(["docker", "compose", *arguments], cwd=ROOT, check=True)


def preflight():
    if shutil.which("docker") is None:
        raise RuntimeError(
            "Docker CLI was not found. Install Docker Desktop, start it with the WSL 2 "
            "Linux-container backend, then open a new terminal and retry. "
            "Installation guide: https://docs.docker.com/desktop/setup/install/windows-install/"
        )
    compose = subprocess.run(["docker", "compose", "version"], capture_output=True, text=True, timeout=30)
    if compose.returncode != 0:
        raise RuntimeError("Docker Compose v2 is unavailable. Install or repair Docker Desktop, then retry.")
    engine = subprocess.run(["docker", "info", "--format", "{{.OSType}}"], capture_output=True, text=True, timeout=30)
    if engine.returncode != 0:
        raise RuntimeError("Docker Engine is not reachable. Start Docker Desktop, wait until the engine is running, and check 'docker info' before retrying.")
    if engine.stdout.strip() != "linux":
        raise RuntimeError("This demo requires Linux containers. Switch Docker Desktop to Linux containers, then retry.")


def main():
    preflight()
    initialize()
    run("--profile", "tools", "build", "master", "agent-a", "minio-image")
    initialize_tls()
    run("up", "-d", "--wait", "--wait-timeout", "180", "master", "docker-host-a", "docker-host-b")
    for host in ("docker-host-a", "docker-host-b"):
        print(f"Loading source-built MinIO into {host}…", flush=True)
        save = subprocess.Popen(["docker", "image", "save", "dep-pilot/minio:2025-10-15"], stdout=subprocess.PIPE, cwd=ROOT)
        try:
            load = subprocess.run(["docker", "compose", "exec", "-T", host, "docker", "image", "load"], stdin=save.stdout, cwd=ROOT)
        finally:
            save.stdout.close()
        if save.wait() != 0 or load.returncode != 0:
            raise RuntimeError(f"Image import failed for {host}")
    run("up", "-d", "--no-build", "agent-a", "agent-b")
    port = published_port(compose_configuration(), "master", 8080)
    print(f"Dep Pilot started. Open https://localhost:{port} and sign in as admin using secrets/admin-password.txt.")
    print("Next: python scripts/smoke_test.py")


if __name__ == "__main__":
    try:
        main()
    except (RuntimeError, OSError, subprocess.CalledProcessError, subprocess.TimeoutExpired) as exc:
        raise SystemExit(f"Demo startup failed: {exc}") from None
