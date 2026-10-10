"""Packages a WebGL release build as the itch.io zip, but only once the owner's sensitivity review is recorded for every
event with a sensitive tag (spec 5.5; docs/m3-release.md). The gate is the sim's own (Statecraft.Cli --release-gate),
run on the content INSIDE the build folder, so it checks exactly what would ship. Stdlib only (plus the .NET SDK).

  python3 tools/package_release.py [--build unity/Builds/Web-brotli] [--zip unity/Builds/statecraft-m3-web.zip]

Exit 0 and "PACKAGED <zip> <bytes>" on success; exit 1 and "PACKAGE REFUSED ..." when the gate refuses, the build is
not a WebGL build folder, or it was built with the pending-rights local prototypes (Build.cs marks such a folder).
"""
import argparse
import subprocess
import sys
import zipfile
from pathlib import Path

REPO = Path(__file__).resolve().parents[1]
PROTOTYPE_MARKER = "PENDING-RIGHTS-PROTOTYPES.txt"   # Build.cs PrototypeBuildMark.Marker


def gate(content_dir: Path):
    """Runs the sim's release gate on a content folder: (passed, message)."""
    r = subprocess.run(["dotnet", "run", "--project", str(REPO / "sim/Statecraft.Cli"), "-v", "q", "--",
                        "--release-gate", "--content", str(content_dir)], capture_output=True, text=True)
    lines = [l for l in (r.stdout + r.stderr).splitlines() if "RELEASE GATE" in l or "error" in l.lower()]
    return r.returncode == 0, "\n".join(lines) or f"release gate exited {r.returncode}"


def package(build: Path, zip_path: Path) -> int:
    if not (build / "index.html").is_file():
        print(f"PACKAGE REFUSED: {build} has no index.html (not a WebGL build folder)")
        return 1
    if (build / PROTOTYPE_MARKER).exists():   # the models' rights are pending (docs/art-prototypes.md)
        print(f"PACKAGE REFUSED: {build} was built with the local prototype models; move unity/Assets/Art/Settlements/Prototype "
              "out of the project and rebuild")
        return 1
    content = build / "StreamingAssets" / "content"
    if not content.is_dir():
        print(f"PACKAGE REFUSED: {build} has no StreamingAssets/content")
        return 1
    ok, message = gate(content)
    print(message)
    if not ok:
        print("PACKAGE REFUSED: the build may not be shared until the sensitivity review is recorded")
        return 1
    zip_path.unlink(missing_ok=True)
    with zipfile.ZipFile(zip_path, "w", zipfile.ZIP_DEFLATED) as z:   # index.html at the zip root, as itch.io needs
        for f in sorted(p for p in build.rglob("*") if p.is_file()):
            z.write(f, f.relative_to(build).as_posix())
    print(f"PACKAGED {zip_path} {zip_path.stat().st_size} bytes")
    return 0


def main(argv=None) -> int:
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("--build", default=str(REPO / "unity/Builds/Web-brotli"))
    p.add_argument("--zip", default=str(REPO / "unity/Builds/statecraft-m3-web.zip"))
    a = p.parse_args(argv)
    return package(Path(a.build), Path(a.zip))


if __name__ == "__main__":
    sys.exit(main())
