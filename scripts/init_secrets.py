"""Generate local demo credentials without printing values or overwriting existing files."""
from pathlib import Path
import os
import secrets

ROOT = Path(__file__).resolve().parents[1]


def initialize():
    directory = ROOT / "secrets"
    directory.mkdir(mode=0o700, exist_ok=True)
    if os.name != "nt":
        directory.chmod(0o700)
    for name in ("admin-password.txt", "bootstrap-token.txt", "minio-password.txt"):
        path = directory / name
        try:
            descriptor = os.open(path, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
        except FileExistsError:
            if os.name != "nt":
                path.chmod(0o644)
            continue
        with os.fdopen(descriptor, "w", encoding="utf-8") as stream:
            stream.write(secrets.token_hex(32))
        # Local Compose secrets are bind mounts: preserve readability for the
        # non-root container UID. The private parent directory protects host access.
        if os.name != "nt":
            path.chmod(0o644)
    print("Local credentials ready in secrets/. Existing credentials were preserved.")


if __name__ == "__main__":
    initialize()
