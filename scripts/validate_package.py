"""Check metadata consistency and ensure no game libraries ship in the release."""
import json
from pathlib import Path
import xml.etree.ElementTree as ET
from zipfile import ZipFile

root = Path(__file__).resolve().parents[1]
metadata = json.loads((root / "modinfo.json").read_text())
version = ET.parse(root / "src/ShelteredSounds/ShelteredSounds.csproj").findtext(".//Version")
assert metadata["version"] == version, "Project and mod versions differ"
assert metadata["side"] == "Client", "This must remain a client-only mod"
archive = root / "artifacts" / f"shelteredsounds-{version}.zip"
with ZipFile(archive) as package:
    assert set(package.namelist()) == {"ShelteredSounds.dll", "modinfo.json", "README.md"}, package.namelist()
    assert json.loads(package.read("modinfo.json")) == metadata
    assert len(package.read("ShelteredSounds.dll")) > 0
print(f"Validated {archive.relative_to(root)}")
