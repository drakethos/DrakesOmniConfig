#!/usr/bin/env python3
"""Cross-platform (Windows/Linux/macOS) replacements for the build-time PowerShell helpers.

Called from build/DrakeMod.Build.targets as:
    $(DrakePython) "$(DrakeBuildPy)" <command> -Option value ...

Option names deliberately match the old .ps1 parameter names (-DllPath, -ZipPath, ...), so the
MSBuild Exec lines only changed their prefix.

Commands
    verify-serversync  -DllPath P                       fail if ServerSync IL was merged into a consumer mod DLL
    deploy-suites      -ProjectDir P -ProfileRoots A;B  copy Tests/suites/* into profile TestLab config (runs Tests/gen_*.py first)
    stage-ci-refs      -ProjectDir P -OutRoot P         fake Valheim tree from the Pfhoenix stub package (CI-equivalent refs)
    install-zip        -ZipPath Z -DestDir D            wipe D and extract a package zip into it
    posix-zip          -SourceDir S -ZipPath Z          zip with forward-slash entries
    update-r2-mod      -ModsYmlPath ... (see below)     upsert an r2modman mods.yml entry (r2modman only; Gale has no mods.yml)
"""
import argparse, os, re, shutil, subprocess, sys, tempfile, time, zipfile
from pathlib import Path


def P(s):
    """Path from an MSBuild string; tolerate backslashes when running on a POSIX system."""
    if s is None:
        return None
    if os.sep == "/" and "\\" in s:
        s = s.replace("\\", "/")
    return Path(s)


# ---------------------------------------------------------------- verify-serversync
def cmd_verify_serversync(a):
    dll = P(a.DllPath)
    if not dll.is_file():
        sys.exit(f"DLL not found: {dll}")
    data = dll.read_bytes()
    # ILRepack internalizes ServerSync into DrakeModsLibs only; consumer DLLs must not contain it.
    for marker in (b"ServerSync", b"ServerSyncManager", b"ServerSync.ConfigSync"):
        if marker in data:
            sys.exit(f"Forbidden embed detected ({marker.decode()}) in {dll}. ServerSync belongs only in DrakeModsLibs.")
    print(f"OK: no ServerSync markers in {dll.name}")


# ---------------------------------------------------------------- deploy-suites
def cmd_deploy_suites(a):
    proj = P(a.ProjectDir)
    tests = proj / "Tests"
    suites = tests / "suites"
    if not suites.is_dir():
        return
    for gen in sorted(tests.glob("gen_*.py")):
        r = subprocess.run([sys.executable, "-I", str(gen)], stdout=subprocess.DEVNULL)
        if r.returncode != 0:
            sys.exit(f"TestLab suites: {gen.name} failed (exit {r.returncode}).")
    owned = [d for d in suites.iterdir() if d.is_dir()]
    for root in (a.ProfileRoots or "").split(";"):
        root = root.strip()
        if not root or not P(root).exists():
            continue
        dest = P(root) / "BepInEx" / "config" / "DrakesTestLab" / "suites"
        dest.mkdir(parents=True, exist_ok=True)
        for s in owned:
            t = dest / s.name
            if t.exists():
                shutil.rmtree(t)
            shutil.copytree(s, t)
        print(f"TestLab suites: {len(owned)} suite(s) -> {dest}")


# ---------------------------------------------------------------- stage-ci-refs
def cmd_stage_ci_refs(a):
    proj = P(a.ProjectDir)
    out = P(a.OutRoot)
    lib = None
    for root in (proj / "packages", proj.parent / "DrakesRenameIt" / "packages", proj.parent / "DrakeModsLibs" / "packages"):
        if not root.is_dir():
            continue
        for d in sorted(root.glob("Pfhoenix.Valheim.ModProjectReferences.*")):
            if (d / "lib" / "net46").is_dir():
                lib = d / "lib" / "net46"
                break
        if lib:
            break
    if lib is None:
        print("STAGE_CI_REFS_SKIP=1")
        return
    managed = out / "valheim_Data" / "Managed"
    pub = managed / "publicized_assemblies"
    bep = out / "BepInEx" / "core"
    for d in (managed, pub, bep):
        d.mkdir(parents=True, exist_ok=True)
    for f in lib.glob("*.dll"):
        shutil.copy2(f, managed / f.name)

    def cp(src, dst):
        s = lib / src
        if s.exists():
            shutil.copy2(s, dst)

    cp("assembly_valheim.dll", pub / "assembly_valheim_publicized.dll")
    cp("assembly_utils.dll", pub / "assembly_utils_publicized.dll")
    cp("gui_framework.dll", pub / "gui_framework_publicized.dll")
    cp("0Harmony.dll", bep / "0Harmony.dll")
    # file name case differs between package versions
    for n in ("bepinex.dll", "BepInEx.dll"):
        if (lib / n).exists():
            shutil.copy2(lib / n, bep / "BepInEx.dll")
            break
    print(f"STAGE_CI_REFS_OK={out}")
    print(f"Pfhoenix lib: {lib}")


# ---------------------------------------------------------------- install-zip
def cmd_install_zip(a):
    z = P(a.ZipPath)
    dest = P(a.DestDir)
    if not z.is_file():
        sys.exit(f"Zip not found: {z}")
    with tempfile.TemporaryDirectory(prefix="pkgzip-") as tmp:
        with zipfile.ZipFile(z) as zf:
            zf.extractall(tmp)
        kids = list(Path(tmp).iterdir())
        src = kids[0] if len(kids) == 1 and kids[0].is_dir() else Path(tmp)
        if dest.exists():
            shutil.rmtree(dest)
        shutil.copytree(src, dest)
    print(f"Installed zip -> {dest}")


# ---------------------------------------------------------------- posix-zip
def cmd_posix_zip(a):
    src = P(a.SourceDir).resolve()
    zp = P(a.ZipPath)
    zp.parent.mkdir(parents=True, exist_ok=True)
    if zp.exists():
        zp.unlink()
    with zipfile.ZipFile(zp, "w", zipfile.ZIP_DEFLATED) as z:
        for f in sorted(src.rglob("*")):
            if f.is_file():
                z.write(f, f.relative_to(src).as_posix())
    print(f"Posix zip: {zp}")


# ---------------------------------------------------------------- update-r2-mod
def _ver_parts(v):
    core = re.split(r"[-+]", v, maxsplit=1)[0].strip()
    parts = (core.split(".") + ["0", "0", "0"])[:3]
    return [int(re.sub(r"\D", "", p) or 0) for p in parts]


def _yaml_desc(text):
    text = text.replace("\r\n", "\n").strip()
    if not text:
        return "  description: ''"
    if "\n" not in text:
        return "  description: '" + text.replace("'", "''") + "'"
    body = "\n".join("    " + ln.rstrip() for ln in text.split("\n"))
    return "  description: >-\n" + body


def cmd_update_r2_mod(a):
    df = P(a.DescriptionFile)
    if not df.is_file():
        sys.exit(f"Description file not found: {df}")
    desc = df.read_text(encoding="utf-8-sig").strip()
    full = f"{a.AuthorName}-{a.PackageName}"
    ma, mi, pa = _ver_parts(a.Version)
    deps = [d.strip() for d in (a.Dependencies or "").split(";") if d.strip()]
    dep_yaml = "  dependencies: []" if not deps else "  dependencies:\n" + "\n".join(f"    - {d}" for d in deps)
    site = a.WebsiteUrl or f"https://thunderstore.io/c/valheim/p/{a.AuthorName}/{a.PackageName}/"
    block = f"""- manifestVersion: 1
  name: {full}
  authorName: {a.AuthorName}
  websiteUrl: {site}
  displayName: {a.DisplayName}
{_yaml_desc(desc)}
  gameVersion: '0'
  networkMode: both
  packageType: other
  installMode: managed
  installedAtTime: {int(time.time() * 1000)}
  loaders: []
{dep_yaml}
  incompatibilities: []
  optionalDependencies: []
  versionNumber:
    major: {ma}
    minor: {mi}
    patch: {pa}
  enabled: true""".rstrip()

    yml = P(a.ModsYmlPath)
    yml.parent.mkdir(parents=True, exist_ok=True)
    existing = yml.read_text(encoding="utf-8") if yml.exists() else ""
    blocks = [m.group(0).rstrip() for m in re.finditer(r"(?ms)^- manifestVersion:.*?(?=\r?\n- manifestVersion:|\Z)", existing.rstrip())] if existing.strip() else []
    out, updated = [], False
    for b in blocks:
        m = re.search(r"(?m)^\s+name:\s+(.+?)\s*$", b)
        if m and m.group(1).strip() == full:
            out.append(block)
            updated = True
        else:
            out.append(b)
    if not updated:
        out.append(block)
    yml.write_text("\n".join(out) + "\n", encoding="utf-8")
    print(f"{'Updated' if updated else 'Added'} mods.yml entry: {full} ({a.Version})")


# ---------------------------------------------------------------- CLI
def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = ap.add_subparsers(dest="cmd", required=True)

    def add(name, fn, **opts):
        sp = sub.add_parser(name)
        for o, req in opts.items():
            sp.add_argument(f"-{o}", required=req, default="")
        sp.set_defaults(fn=fn)

    add("verify-serversync", cmd_verify_serversync, DllPath=True)
    add("deploy-suites", cmd_deploy_suites, ProjectDir=True, ProfileRoots=False)
    add("stage-ci-refs", cmd_stage_ci_refs, ProjectDir=True, OutRoot=True)
    add("install-zip", cmd_install_zip, ZipPath=True, DestDir=True)
    add("posix-zip", cmd_posix_zip, SourceDir=True, ZipPath=True)
    add("update-r2-mod", cmd_update_r2_mod, ModsYmlPath=True, AuthorName=True, PackageName=True, DisplayName=True,
        Version=True, DescriptionFile=True, WebsiteUrl=False, Dependencies=False)
    a = ap.parse_args()
    a.fn(a)


if __name__ == "__main__":
    main()
