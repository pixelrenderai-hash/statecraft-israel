"""tools/package_release.py: the itch.io zip is made only after the owner's sensitivity review of every sensitive event
(spec 5.5; M3 rereview). The gate is the sim's own (Statecraft.Cli --release-gate), run on the content inside the build."""
import re
import shutil
import zipfile
from pathlib import Path

import package_release

REPO = Path(package_release.REPO)
SENSITIVE = ["altalena", "bernadotte_assassination", "operation_hiram", "operation_horev", "operation_yoav", "reparations_claim", "ten_days"]


def fake_build(tmp_path, reviewed):
    """A build folder with index.html and the real content copied into StreamingAssets/content."""
    build = tmp_path / "Web-brotli"
    (build / "StreamingAssets").mkdir(parents=True)
    (build / "index.html").write_text("<html></html>")
    content = build / "StreamingAssets" / "content"
    shutil.copytree(REPO / "content", content)
    status = "sensitivity-reviewed" if reviewed else "fact-checked"   # the owner's sign-off (D11), or its absence
    for e in SENSITIVE:
        f = content / "events" / f"{e}.yaml"
        f.write_text(re.sub(r"(?m)^status: .*$", f"status: {status}", f.read_text()))
    return build


def test_packaging_is_refused_until_every_sensitive_event_is_reviewed(tmp_path, capsys):
    build, zip_path = fake_build(tmp_path, reviewed=False), tmp_path / "out.zip"
    assert package_release.main(["--build", str(build), "--zip", str(zip_path)]) == 1
    assert not zip_path.exists()
    out = capsys.readouterr().out
    assert "PACKAGE REFUSED" in out
    for e in SENSITIVE:
        assert e in out


def test_packaging_writes_the_zip_once_all_are_reviewed(tmp_path, capsys):
    build, zip_path = fake_build(tmp_path, reviewed=True), tmp_path / "out.zip"
    assert package_release.main(["--build", str(build), "--zip", str(zip_path)]) == 0
    with zipfile.ZipFile(zip_path) as z:
        names = z.namelist()
    assert "index.html" in names                                       # at the zip root, as itch.io needs
    assert "StreamingAssets/content/events/altalena.yaml" in names
    assert "PACKAGED" in capsys.readouterr().out


def test_packaging_is_refused_for_a_build_made_with_the_local_prototypes(tmp_path, capsys):
    build, zip_path = fake_build(tmp_path, reviewed=True), tmp_path / "out.zip"
    (build / package_release.PROTOTYPE_MARKER).write_text("pending-rights")   # as Build.cs PrototypeBuildMark writes it
    assert package_release.main(["--build", str(build), "--zip", str(zip_path)]) == 1
    assert not zip_path.exists()
    assert "PACKAGE REFUSED" in capsys.readouterr().out
