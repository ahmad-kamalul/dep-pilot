"""Exercise first-install and update rollback on disposable MinIO containers only."""
import json
import re
import time
import subprocess
import uuid
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def command(*args, **kwargs):
    return subprocess.run(["docker", "compose", "exec", "-T", *args], cwd=ROOT,
                          capture_output=True, text=True, **kwargs)


def main():
    name = "dep-pilot-minio-" + uuid.uuid4().hex
    bind_directory = "/tmp/dep-pilot-bind-" + uuid.uuid4().hex
    scratch = "/tmp/dep-pilot-rollback-" + uuid.uuid4().hex
    password = (ROOT / "secrets/minio-password.txt").read_text().strip()
    variables = dict(minio_name=name, minio_root_user="dep-pilot", minio_root_password=password,
                     minio_api_port=19100, minio_console_port=19101, minio_bind_address="0.0.0.0",
                     minio_probe_host="127.0.0.1", minio_ready_retries=1)
    setup = "import sys,json,pathlib,os; p=pathlib.Path(sys.argv[1]); p.mkdir(mode=0o700); d=json.load(sys.stdin); (p/'playbook.yml').write_text(d['yaml'])"
    result = command("agent-a", "python3", "-c", setup, scratch,
                     input=json.dumps({"yaml": (ROOT / "playbooks/minio.yml").read_text()}))
    result.check_returncode()

    def deploy(expected_success):
        write = "import sys,pathlib; p=pathlib.Path(sys.argv[1]); p.write_text(sys.stdin.read()); p.chmod(0o600)"
        command("agent-a", "python3", "-c", write, scratch + "/vars.json", input=json.dumps(variables)).check_returncode()
        result = command("agent-a", "ansible-playbook", "-i", "localhost,", "--extra-vars", "@" + scratch + "/vars.json", scratch + "/playbook.yml")
        assert password not in result.stdout + result.stderr, "Credential appeared in output"
        if (result.returncode == 0) != expected_success:
            raise AssertionError("Unexpected playbook result:\n" + result.stdout[-6000:] + result.stderr[-1000:])
        if not expected_success:
            assert "Previous container configuration restored" in result.stdout, result.stdout[-7000:] + result.stderr[-1000:]

    def engine(*args):
        return command("docker-host-a", "docker", *args)

    # Never touch containers outside this test's randomly generated name.
    try:
        deploy(False)
        assert engine("container", "inspect", name).returncode != 0
        engine("volume", "inspect", name + "-data").check_returncode()
        print("PASS: failed first installation removes its container and retains storage")
        variables["minio_probe_host"] = "docker-host-a"
        deploy(True)
        engine("exec", name, "sh", "-c", "printf rollback-data > /data/rollback-probe.txt").check_returncode()
        variables.update(minio_api_port=19102, minio_probe_host="127.0.0.1")
        deploy(False)
        restored = json.loads(engine("inspect", name).stdout)[0]
        assert restored["NetworkSettings"]["Ports"]["9000/tcp"][0]["HostPort"] == "19100"
        assert engine("exec", name, "cat", "/data/rollback-probe.txt").stdout == "rollback-data"
        probe = "import urllib.request,time; url='http://docker-host-a:19100/minio/health/ready'; "
        # Readiness can lag behind Docker reporting the container as running.
        ready = False
        for _ in range(20):
            if command("agent-a", "python3", "-c", probe + "assert urllib.request.urlopen(url,timeout=3).status==200").returncode == 0:
                ready = True
                break
            time.sleep(1)
        assert ready, "Restored MinIO did not become ready"
        print("PASS: failed update restores previous ports/configuration, readiness, and existing data")
        engine("exec", name, "rm", "/data/rollback-probe.txt").check_returncode()
        command("docker-host-a", "mkdir", "-p", bind_directory).check_returncode()
        command("docker-host-a", "chown", "10001:10001", bind_directory).check_returncode()
        variables.update(minio_api_port=19100, minio_probe_host="docker-host-a", minio_storage_path=bind_directory)
        deploy(True)
        engine("exec", name, "sh", "-c", "printf bind-data > /data/rollback-probe.txt").check_returncode()
        assert command("docker-host-a", "cat", bind_directory + "/rollback-probe.txt").stdout == "bind-data"
        variables.update(minio_api_port=19102, minio_probe_host="127.0.0.1")
        deploy(False)
        assert engine("exec", name, "cat", "/data/rollback-probe.txt").stdout == "bind-data"
        print("PASS: configurable bind storage works and survives a failed update")
    finally:
        engine("rm", "-f", name)
        engine("volume", "rm", name + "-data")
        assert re.fullmatch(r"/tmp/dep-pilot-bind-[0-9a-f]{32}", bind_directory)
        command("docker-host-a", "rm", "-rf", "--", bind_directory).check_returncode()
        command("agent-a", "python3", "-c", "import shutil,sys; shutil.rmtree(sys.argv[1])", scratch)
    print("All rollback scenarios passed.")


if __name__ == "__main__":
    main()
