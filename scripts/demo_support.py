"""Resolve effective Compose ports, including values supplied through .env."""
from pathlib import Path
import json
import subprocess

ROOT = Path(__file__).resolve().parents[1]


def compose_configuration():
    result = subprocess.run(["docker", "compose", "config", "--format", "json"], cwd=ROOT,
                            check=True, capture_output=True, text=True, timeout=30)
    return json.loads(result.stdout)


def published_port(configuration, service, target):
    for port in configuration["services"][service].get("ports", []):
        if int(port["target"]) == target:
            return int(port["published"])
    raise RuntimeError(f"Missing published port {target} for {service}")
