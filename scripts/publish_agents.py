"""Publish self-contained Agent binaries without requiring .NET on target hosts."""
from pathlib import Path
import argparse
import subprocess

ROOT = Path(__file__).resolve().parents[1]


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--dotnet", default="dotnet", help="Path to the .NET 10 SDK executable")
    parser.add_argument("--runtime", choices=("linux-x64", "win-x64", "all"), default="all")
    args = parser.parse_args()
    for runtime in ("linux-x64", "win-x64") if args.runtime == "all" else (args.runtime,):
        subprocess.run([args.dotnet, "publish", "src/DepPilot.Agent", "-c", "Release", "-r", runtime,
                        "--self-contained", "true", "-o", str(ROOT / "artifacts" / runtime)], cwd=ROOT, check=True)


if __name__ == "__main__":
    main()
