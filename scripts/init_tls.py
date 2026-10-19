"""Create a local HTTPS certificate with SANs for the browser and Agents."""
from pathlib import Path
import subprocess

ROOT = Path(__file__).resolve().parents[1]


def initialize_tls():
    directory = ROOT / "secrets"
    certificate, key = directory / "master.crt", directory / "master.key"
    if certificate.exists() and key.exists():
        return
    if certificate.exists() or key.exists():
        raise RuntimeError("Incomplete TLS identity: restore both master.crt and master.key before retrying.")
    subprocess.run([
        "docker", "run", "--rm", "--user", "0", "--entrypoint", "sh",
        "--mount", f"type=bind,source={directory},target=/certs", "dep-pilot/master:local", "-c",
        "umask 077; openssl req -x509 -newkey rsa:3072 -sha256 -nodes -days 365 "
        "-keyout /certs/master.key -out /certs/master.crt -subj '/CN=Dep Pilot local demo' "
        "-addext 'subjectAltName=DNS:localhost,DNS:master,IP:127.0.0.1' "
        "-addext 'basicConstraints=critical,CA:TRUE' -addext 'extendedKeyUsage=serverAuth' "
        "&& chmod 644 /certs/master.key /certs/master.crt"
    ], cwd=ROOT, check=True, stdout=subprocess.DEVNULL, stderr=subprocess.PIPE)
    print("Local HTTPS certificate ready. Scripts and Agents trust secrets/master.crt.")


if __name__ == "__main__":
    initialize_tls()
