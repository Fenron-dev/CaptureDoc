"""Zips the portable folder for distribution and marks the macOS/Linux executables as executable.
Only program files, docs and Data/DevVault are included – never user data (projects, media, settings
with vault paths, logs, caches). The other Data folders are added empty.
Usage: python -I make_zip.py <dist/CaptureDoc> <out.zip>"""
import os
import sys
import zipfile

src, out = sys.argv[1], sys.argv[2]
ROOT = "CaptureDoc"  # folder name inside the zip, independent of the build folder
EXEC = ("CaptureDoc-Linux", "CaptureDoc-macOS.app/Contents/MacOS/CaptureDoc")


def add_dir(z, rel):
    info = zipfile.ZipInfo(f"{ROOT}/{rel}/" if rel else f"{ROOT}/")
    info.external_attr = (0o40755 << 16) | 0x10
    z.writestr(info, b"")


def shipped(rel_dir):
    parts = rel_dir.split("/")
    return parts[0] != "Data" or (len(parts) >= 2 and parts[1] == "DevVault")


with zipfile.ZipFile(out, "w", zipfile.ZIP_DEFLATED, compresslevel=6) as z:
    for dirpath, dirnames, filenames in os.walk(src):
        rel_dir = os.path.relpath(dirpath, src).replace("\\", "/")
        rel_dir = "" if rel_dir == "." else rel_dir
        add_dir(z, rel_dir)
        if rel_dir == "Data":
            continue  # files directly in Data are runtime data
        if rel_dir and not shipped(rel_dir):
            continue  # empty folder only
        for f in filenames:
            full = os.path.join(dirpath, f)
            rel = f if not rel_dir else f"{rel_dir}/{f}"
            zi = zipfile.ZipInfo.from_file(full, f"{ROOT}/{rel}")
            mode = 0o755 if rel in EXEC or rel.endswith((".so", ".dylib")) else 0o644
            zi.external_attr = (0o100000 | mode) << 16
            zi.compress_type = zipfile.ZIP_DEFLATED
            with open(full, "rb") as fh:
                z.writestr(zi, fh.read())
print("zip:", out, os.path.getsize(out) // (1024 * 1024), "MB")
