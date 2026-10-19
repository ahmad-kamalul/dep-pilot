"""Test Agent outage, queued-job cancellation, recovery, and access isolation in the local demo."""
import subprocess
from urllib import request, error
from smoke_test import Demo, ROOT
from demo_support import compose_configuration, published_port


def run(*arguments):
    subprocess.run(["docker", "compose", *arguments], cwd=ROOT, check=True)


def main():
    config = compose_configuration()
    demo = Demo(f'http://localhost:{published_port(config, "master", 8080)}')
    demo.login()
    initial = demo.call("/api/admin/dashboard")
    host = next(h for h in initial["hosts"] if h["hostname"] == "warehouse-b" and not h["revoked"])
    if any(j["hostId"] == host["id"] and j["status"] in ("Pending", "Running") for j in initial["jobs"]):
        raise RuntimeError("Finish active work on warehouse-b before running failure scenarios.")
    base = demo.base
    for path, data, headers in (("/api/admin/dashboard", None, {}),
                                ("/api/agent/jobs/claim", b"{}", {"Content-Type": "application/json", "Authorization": "Bearer " + "0" * 64})):
        try:
            request.urlopen(request.Request(base + path, data=data, headers=headers), timeout=10)
        except error.HTTPError as exc:
            assert exc.code == 401, f"Expected 401, received {exc.code}"
        else:
            raise AssertionError("Unauthenticated request unexpectedly succeeded")
    print("PASS: anonymous admin access and invalid Agent credentials are rejected")
    try:
        run("stop", "agent-b")
        def offline():
            return next(h for h in demo.call("/api/admin/dashboard")["hosts"] if h["id"] == host["id"])["online"] is False
        demo.wait(offline, timeout=90)
        print("PASS: stopped Agent transitions to offline after heartbeat expiry")
        deployment = demo.call("/api/admin/deployments", {"name": "Failure scenario / offline cancellation", "playbookId": initial["playbooks"][0]["id"], "hostIds": [host["id"]]})["id"]
        def job():
            return next(j for j in demo.call("/api/admin/dashboard")["jobs"] if j["deploymentId"] == deployment)
        pending = job()
        assert pending["status"] == "Pending"
        demo.call(f'/api/admin/jobs/{pending["id"]}/cancel', {})
        assert job()["status"] == "Cancelled"
        print("PASS: offline work stays pending and can be cancelled before execution")
    finally:
        run("start", "agent-b")
    def recovered():
        return next(h for h in demo.call("/api/admin/dashboard")["hosts"] if h["id"] == host["id"])["online"]
    demo.wait(recovered, timeout=60)
    assert job()["status"] == "Cancelled"
    assert demo.call(f'/api/admin/jobs/{pending["id"]}/logs') == []
    print("PASS: Agent recovers with the same identity and does not execute cancelled work")
    print("All failure scenarios passed.")


if __name__ == "__main__":
    main()
