"""Check HTTPS validation and deregistration without changing real Agent identities."""
import json
import secrets
import subprocess
import sys
import tempfile
from pathlib import Path
import socket
import ssl
import uuid
from urllib import request, error
from smoke_test import Demo, ROOT
from demo_support import compose_configuration, published_port


def main():
    port = published_port(compose_configuration(), "master", 8080)
    base = f"https://localhost:{port}"
    context = ssl.create_default_context(cafile=str(ROOT / "secrets/master.crt"))
    try:
        # Use an isolated trust store: the user may already trust this demo CA.
        request.urlopen(base + "/healthz", timeout=10, context=ssl.SSLContext(ssl.PROTOCOL_TLS_CLIENT))
    except error.URLError as exc:
        assert isinstance(exc.reason, ssl.SSLCertVerificationError)
    else:
        raise AssertionError("Untrusted local certificate was accepted")
    try:
        with socket.create_connection(("localhost", port), timeout=10) as connection:
            context.wrap_socket(connection, server_hostname="wrong-host.example")
    except ssl.SSLCertVerificationError:
        pass
    else:
        raise AssertionError("TLS hostname mismatch was accepted")
    print("PASS: HTTPS rejects an untrusted certificate and a hostname mismatch")

    bootstrap = (ROOT / "secrets/bootstrap-token.txt").read_text().strip()
    token = secrets.token_hex(32)
    payload = dict(installationId=str(uuid.uuid4()), hostname="lifecycle-test-" + uuid.uuid4().hex[:8],
                   operatingSystem="Test host", agentVersion="1.1.0", token=token,
                   ipAddress="192.0.2.10", capabilities=["linux", "docker", "ansible"])

    def post(path, body, credential, expected=200):
        req = request.Request(base + path, data=json.dumps(body).encode(), headers={
            "Content-Type": "application/json", "Authorization": "Bearer " + credential})
        try:
            with request.urlopen(req, context=context, timeout=10) as response:
                assert response.status == expected
                return json.loads(response.read()) if response.status == 200 else None
        except error.HTTPError as exc:
            assert exc.code == expected, f"Expected {expected}, got {exc.code}"

    demo = Demo(base)
    demo.login()
    host = post("/api/agents/register", payload, bootstrap)["hostId"]
    try:
        catalog = demo.call("/api/admin/dashboard")["playbooks"]
        deployment = demo.call("/api/admin/deployments", dict(name="Lifecycle / pending cleanup", playbookId=catalog[0]["id"], hostIds=[host]))["id"]
        demo.call(f"/api/admin/hosts/{host}/deregister", {})
        dashboard = demo.call("/api/admin/dashboard")
        assert all(h["id"] != host for h in dashboard["hosts"])
        assert next(j for j in dashboard["jobs"] if j["deploymentId"] == deployment)["status"] == "Cancelled"
        post("/api/agent/heartbeat", {"agentVersion": "1.1.0"}, token, expected=401)
        post("/api/agents/register", payload, bootstrap, expected=409)
        print("PASS: deregistration hides the host, cancels pending work, retains history, and blocks identity reuse")
    finally:
        demo.call(f"/api/admin/hosts/{host}/deregister", {})
    # Retry retirement using a local identity file; remote retirement is idempotent.
    with tempfile.TemporaryDirectory(prefix="dep-pilot-retirement-") as state:
        identity = Path(state) / "identity.json"
        identity.write_text(json.dumps({"HostId": host, "Token": token}))
        subprocess.run([sys.executable, str(ROOT / "scripts/deregister_agent.py"), "--master-url", base,
                        "--state-directory", state, "--certificate", str(ROOT / "secrets/master.crt")], check=True)
        assert not identity.exists() and (Path(state) / "deregistered.json").exists()
    print("PASS: retirement retries safely and removes the local identity credential")
    print("All security and lifecycle checks passed.")


if __name__ == "__main__":
    main()
