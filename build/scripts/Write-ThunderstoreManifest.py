#!/usr/bin/env python3
"""Writes a Thunderstore/Hexium manifest.json from MOD_* env vars, properly JSON-escaped.

Called from DrakeMod.Build.targets' WriteThunderstoreManifest target instead of building the
JSON via MSBuild string Replace(), which corrupts backslashes in a replacement string on
non-Windows runners (a literal \" in a Description came out as /").
"""
import json
import os
import sys

out_path = sys.argv[1]

deps = [d for d in os.environ.get("MOD_DEPENDENCIES", "").split(",") if d]

manifest = {
    "name": os.environ.get("MOD_NAME", ""),
    "version_number": os.environ.get("MOD_VERSION", ""),
    "author": os.environ.get("MOD_AUTHOR", ""),
    "description": os.environ.get("MOD_DESCRIPTION", ""),
    "website_url": os.environ.get("MOD_WEBSITE", ""),
    "dependencies": deps,
}

os.makedirs(os.path.dirname(out_path), exist_ok=True)
with open(out_path, "w", encoding="utf-8", newline="\r\n") as f:
    json.dump(manifest, f, indent=4)
    f.write("\n")
