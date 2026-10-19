"""Exercise real Agents and Ansible twice, verify MinIO readiness, then check persistence."""
from pathlib import Path
from urllib import request, parse, error
from http.cookiejar import CookieJar
import ssl
import argparse
import json
import re
import subprocess
import time
import uuid
from demo_support import compose_configuration, published_port
from s3_probe import S3Probe

ROOT = Path(__file__).resolve().parents[1]


class Demo:
    def __init__(self, base):
        self.base = base.rstrip("/")
        self.client = request.build_opener(request.HTTPCookieProcessor(CookieJar()), request.HTTPSHandler(context=ssl.create_default_context(cafile=str(ROOT / "secrets/master.crt"))))
        self.csrf = None

    def call(self, path, body=None, form=False):
        headers = {}
        if self.csrf:
            headers["X-CSRF-TOKEN"] = self.csrf
        if body is None:
            data = None
        elif form:
            data = parse.urlencode(body).encode()
            headers["Content-Type"] = "application/x-www-form-urlencoded"
        else:
            data = json.dumps(body).encode()
            headers["Content-Type"] = "application/json"
        with self.client.open(request.Request(self.base + path, data=data, headers=headers), timeout=15) as response:
            content = response.read().decode()
            return json.loads(content) if "application/json" in response.headers.get("Content-Type", "") else content

    def wait(self, condition, timeout=240):
        end = time.monotonic() + timeout
        while time.monotonic() < end:
            try:
                value = condition()
                if value:
                    return value
            except (error.URLError, TimeoutError, ConnectionError):
                pass
            time.sleep(2)
        raise TimeoutError("Demo condition did not complete within the expected time.")

    def login(self):
        page = self.wait(lambda: self.call("/Login"))
        match = re.search(r'name="__RequestVerificationToken" type="hidden" value="([^"]+)"', page)
        if not match:
            raise RuntimeError("Login page did not contain a CSRF token.")
        self.call("/Login", {"Username": "admin", "Password": (ROOT / "secrets/admin-password.txt").read_text().strip(), "__RequestVerificationToken": match[1]}, form=True)
        self.csrf = self.call("/api/admin/csrf")["token"]

    def rollout(self, name, hosts, playbook):
        deployment = self.call("/api/admin/deployments", {"name": name, "playbookId": playbook, "hostIds": hosts})["id"]

        def finished():
            dashboard = self.call("/api/admin/dashboard")
            jobs = [j for j in dashboard["jobs"] if j["deploymentId"] == deployment]
            return jobs if len(jobs) == len(hosts) and all(j["status"] not in ("Pending", "Running") for j in jobs) else None

        jobs = self.wait(finished, timeout=300)
        for job in jobs:
            if job["status"] != "Succeeded":
                logs = self.call(f'/api/admin/jobs/{job["id"]}/logs')
                raise AssertionError(f'{job["hostname"]}: {job["status"]}\n' + "\n".join(log["text"] for log in logs))
        print(f"PASS: {name} completed on {len(jobs)} hosts")
        return jobs


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--base-url", help="Override the Master URL; default is resolved from Compose.")
    parser.add_argument("--skip-restart", action="store_true")
    args = parser.parse_args()
    configuration = compose_configuration()
    base_url = args.base_url or f'https://localhost:{published_port(configuration, "master", 8080)}'
    demo = Demo(base_url)
    demo.login()

    def ready_hosts():
        dashboard = demo.call("/api/admin/dashboard")
        hosts = [h for h in dashboard["hosts"] if h["online"] and h["hostname"] in ("warehouse-a", "warehouse-b")]
        return (dashboard, hosts) if len(hosts) == 2 else None

    dashboard, hosts = demo.wait(ready_hosts)
    print("PASS: two Agents registered and online")
    def resources_ready():
        current = demo.call("/api/admin/dashboard")["hosts"]
        return all(h.get("metrics") and h["metrics"]["cpuPercent"] is not None and h.get("ipAddress") and "docker" in (h.get("capabilities") or []) for h in current if h["id"] in [host["id"] for host in hosts])
    demo.wait(resources_ready, timeout=60)
    print("PASS: IP, capabilities, CPU, memory, and disk metrics received")
    ids = [h["id"] for h in hosts]
    playbook = dashboard["playbooks"][0]["id"]
    demo.rollout("Smoke / initial deployment", ids, playbook)
    for port in (published_port(configuration, "docker-host-a", 9000), published_port(configuration, "docker-host-b", 9000)):
        with request.urlopen(f"http://127.0.0.1:{port}/minio/health/ready", timeout=10) as response:
            assert response.status == 200
    print("PASS: both standalone MinIO instances report ready")
    bucket = "dep-pilot-smoke-" + uuid.uuid4().hex
    payload = ("Dep Pilot persistence probe " + uuid.uuid4().hex).encode()
    password = (ROOT / "secrets/minio-password.txt").read_text().strip()
    probes = [S3Probe(f"http://127.0.0.1:{published_port(configuration, service, 9000)}", "dep-pilot", password)
              for service in ("docker-host-a", "docker-host-b")]
    for probe in probes:
        probe.call("PUT", "/" + bucket)
        probe.call("PUT", f"/{bucket}/verification.txt", payload)
        assert probe.call("GET", f"/{bucket}/verification.txt") == payload
    print("PASS: authenticated S3 upload and download on both targets")
    repeated = demo.rollout("Smoke / repeat deployment", ids, playbook)
    for job in repeated:
        logs = demo.call(f'/api/admin/jobs/{job["id"]}/logs')
        output = "\n".join(log["text"] for log in logs)
        assert re.search(r"changed=0\b", output), "Repeated playbook should make no changes"
        for filename in ("admin-password.txt", "bootstrap-token.txt", "minio-password.txt"):
            assert (ROOT / "secrets" / filename).read_text().strip() not in output
    print("PASS: repeat deployment is idempotent and output excludes credentials")
    if not args.skip_restart:
        before = {job["id"] for job in demo.call("/api/admin/dashboard")["jobs"]}
        subprocess.run(["docker", "compose", "restart", "master"], cwd=ROOT, check=True)
        after = demo.wait(lambda: demo.call("/api/admin/dashboard"))
        assert before.issubset({job["id"] for job in after["jobs"]})
        print("PASS: Master restart preserves jobs and the admin session")
        for host in hosts:
            service = "docker-host-a" if host["hostname"] == "warehouse-a" else "docker-host-b"
            container = "dep-pilot-minio-" + host["id"].replace("-", "")
            subprocess.run(["docker", "compose", "exec", "-T", service, "docker", "restart", container], cwd=ROOT, check=True)
        for probe in probes:
            assert demo.wait(lambda: probe.call("GET", f"/{bucket}/verification.txt")) == payload
        print("PASS: object data survives MinIO container restarts on both targets")
    for probe in probes:
        assert probe.call("GET", f"/{bucket}/verification.txt") == payload
        probe.call("DELETE", f"/{bucket}/verification.txt")
        probe.call("DELETE", "/" + bucket)
    print("PASS: smoke-test objects cleaned up; other application data is untouched")
    print("All Docker smoke checks passed.")


if __name__ == "__main__":
    main()
