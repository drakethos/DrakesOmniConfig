"""Builds ConfigBuilder/rules.html from rules.template.html plus a NodeCatalog.json snapshot.

The page also loads a server's own NodeCatalog.json at runtime; the baked-in snapshot only makes it usable
before one is loaded. Items are left out of the snapshot (a test profile's item list includes other mods').

  python Server/Tools/build_rules_page.py <path to NodeCatalog.json>
"""
import json
import os
import sys

here = os.path.dirname(os.path.abspath(__file__))
catalog = json.load(open(sys.argv[1], encoding="utf-8"))
catalog["items"] = []
snapshot = json.dumps(catalog, separators=(",", ":")).replace("</", "<\\/")

template = open(os.path.join(here, "rules.template.html"), encoding="utf-8").read()
out = os.path.join(here, "ConfigBuilder", "rules.html")
open(out, "w", encoding="utf-8").write(template.replace("__DEFAULT_CATALOG__", snapshot))
print("wrote", out)
