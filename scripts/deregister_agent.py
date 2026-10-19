"""Retire a stopped Agent and remove its local identity credential; preserve workload data."""
import argparse
import json
from pathlib import Path
import ssl
import time
from urllib import request, parse
import uuid


class NoRedirect(request.HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        return None


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--master-url", required=True)
    parser.add_argument("--state-directory", required=True)
    parser.add_argument("--certificate", help="Private/demo CA certificate; otherwise use the OS trust store")
    args = parser.parse_args()
    origin = parse.urlsplit(args.master_url)
    if origin.scheme != "https" or not origin.hostname or origin.username or origin.password or origin.path not in ("", "/") or origin.query or origin.fragment:
        raise SystemExit("Use an HTTPS Master origin without credentials, path, query, or fragment.")
    directory = Path(args.state_directory).resolve(strict=True)
    marker = directory / "deregistered.json"
    if marker.exists():
        print("Agent already retired. Local logs and application data remain available.")
        return
    identity_file = directory / "identity.json"
    identity = json.loads(identity_file.read_text())
    host = str(uuid.UUID(identity["HostId"]))
    req = request.Request(args.master_url.rstrip("/") + "/api/agent/deregister", data=b"{}", headers={
        "Authorization": "Bearer " + identity["Token"], "Content-Type": "application/json"})
    context = ssl.create_default_context(cafile=args.certificate)
    client = request.build_opener(NoRedirect(), request.HTTPSHandler(context=context))
    with client.open(req, timeout=15) as response:
        if response.status != 204:
            raise RuntimeError("Deregistration was not acknowledged; local identity retained.")
    marker.write_text(json.dumps({"hostId": host, "retiredAt": int(time.time() * 1000)}))
    identity_file.unlink()
    (directory / "identity.json.tmp").unlink(missing_ok=True)
    print("Agent retired; identity credential removed. Stop the Agent service. Logs, history, and workload data are retained.")


if __name__ == "__main__":
    main()
