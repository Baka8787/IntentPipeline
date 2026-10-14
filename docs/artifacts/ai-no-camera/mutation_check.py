"""Temporary-breakage check for AIMovementNoCameraPlayModeTests (docs/57 §14.2, requirement 5).

    python -I docs/artifacts/ai-no-camera/mutation_check.py --compile-only     (no Unity: each mutant must compile)
    python -I docs/artifacts/ai-no-camera/mutation_check.py                    (live: needs a healthy Editor)

Each mutant makes AIMovementSource depend on the camera in a way the static A30 check cannot see (A30 only looks for
the token `CameraTransform`). The new PlayMode test must FAIL on every mutant with the expected assertion, and PASS
again on the restored source.

Safety
- Refuses to start unless AIMovementSource.cs is identical to HEAD (never clobbers uncommitted work).
- The original bytes are restored in `finally` (also on Ctrl-C) and verified by SHA-256; a failed restore is reported
  loudly with the backup path.
- Unity is only driven through docs/artifacts/editor-memory/safe_unity_test.sh (it recompiles first and stops on a
  dirty scene / busy Editor). Any entry verdict other than PASS / TESTS_FAILED makes the check INCOMPLETE, never PASS.
- No Git command other than read-only `git diff --quiet`.
Exit: 0 every mutant caught and the clean run passed · 1 a mutant survived or the clean run failed · 3 incomplete
(environment blocked) · 2 usage / precondition / restore failure.
"""
import argparse
import hashlib
import os
import re
import shutil
import subprocess
import sys
import tempfile

sys.dont_write_bytecode = True
REPO = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", ".."))
TARGET = "Assets/Scripts/Core/Movement/AIMovementSource.cs"
TEST_FILTER = "Project.Tests.PlayMode.AIMovementNoCameraPlayModeTests"
ENTRY = "docs/artifacts/editor-memory/safe_unity_test.sh"
# A bare "bash" in CreateProcess resolves to C:\Windows\System32ash.exe (WSL) before PATH is searched; the safe entry
# needs Git Bash. Resolve it explicitly (first live run hit exactly this and the entry never started).
BASH = shutil.which("bash")

ENGAGED = "            _nonCombat?.OnEngaged();\n"
STRAFE = "                UpdateStrafeDirection(Time.time);\n"
MUTANTS = [
    ("M1", "no intent at all when there is no Camera.main", ENGAGED,
     ENGAGED + "            if (UnityEngine.Camera.main == null) return; // MUTANT M1\n",
     "沒有任何移動意圖"),
    ("M2", "wrong (camera-default) direction when there is no camera", ENGAGED,
     ENGAGED + "            if (UnityEngine.Camera.main == null) { data.MovementIntent.DesiredDirection = Vector3.right;"
               " data.MovementIntent.DesiredSpeedNormalized = 1f; return; } // MUTANT M2\n",
     "沒有朝向目標"),
    ("M3", "only the Strafe branch (the original EditMode scenario) dies without a camera", STRAFE,
     STRAFE + "                if (UnityEngine.Camera.main == null) return; // MUTANT M3\n",
     "Strafe）沒有移動意圖"),
]


def sha(data):
    return hashlib.sha256(data).hexdigest()


def run(cmd, timeout=None):
    return subprocess.run(cmd, cwd=REPO, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, timeout=timeout)


def entry(label):
    """-> (verdict, code, output, report). verdict is None unless the entry printed its own verdict line: a bare exit
    code is never trusted (a shell that failed to start also exits non-zero)."""
    p = run([BASH, ENTRY, "--mode", "PlayMode", "--filter", TEST_FILTER, "--label", label, "--timeout", "900"], timeout=1800)
    out = p.stdout.decode("utf-8", "replace")
    v = re.search(r"^==== safe_unity_test: ([A-Z_]+) \(exit (\d+)\) ====$", out, re.M)
    verdict = v.group(1) if v and int(v.group(2)) == p.returncode else None
    m = re.search(r"^record: (.+)$", out, re.M)
    report = ""
    if m:
        rec = m.group(1).strip()
        if rec.startswith("/") and len(rec) > 2 and rec[2] == "/":          # /d/... -> d:/...
            rec = rec[1] + ":" + rec[2:]
        for name in ("report.txt", "summary.txt"):
            path = os.path.join(rec, name)
            if os.path.isfile(path):
                with open(path, encoding="utf-8", errors="replace") as f:
                    report += f.read()
    return verdict, p.returncode, out, report


def main(argv):
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--compile-only", action="store_true")
    a = ap.parse_args(argv)
    getattr(sys.stdout, "reconfigure", lambda **_: None)(encoding="utf-8")

    if not a.compile_only and not BASH:
        print("PRECONDITION: Git Bash not found on PATH")
        return 2
    path = os.path.join(REPO, TARGET)
    if run(["git", "diff", "--quiet", "HEAD", "--", TARGET]).returncode != 0:
        print("PRECONDITION: %s differs from HEAD — refusing to mutate uncommitted work" % TARGET)
        return 2
    with open(path, "rb") as f:
        original = f.read()
    text = original.decode("utf-8")
    newline = "\r\n" if "\r\n" in text else "\n"
    backup = os.path.join(tempfile.mkdtemp(prefix="nocam-mut-"), "AIMovementSource.cs.orig")
    shutil.copyfile(path, backup)
    results, restore_ok = [], True
    try:
        for mid, desc, anchor, replacement, expected in MUTANTS:
            a_nl, r_nl = anchor.replace("\n", newline), replacement.replace("\n", newline)
            if text.count(a_nl) != 1:
                results.append((mid, "BROKEN_ANCHOR", "anchor found %d times" % text.count(a_nl)))
                continue
            with open(path, "wb") as f:
                f.write(text.replace(a_nl, r_nl).encode("utf-8"))
            try:
                if a.compile_only:
                    p = run(["dotnet", "build", "Project.Runtime.csproj", "-v", "q", "--nologo",
                             "-p:ResolveAssemblyReferenceIgnoreTargetFrameworkAttributeVersionMismatch=true"], timeout=900)
                    ok = p.returncode == 0
                    results.append((mid, "COMPILES" if ok else "DOES_NOT_COMPILE", desc))
                else:
                    verdict, code, out, report = entry("nocam-" + mid.lower())
                    if verdict == "TESTS_FAILED" and expected in report:
                        results.append((mid, "CAUGHT", desc))
                    elif verdict == "TESTS_FAILED":
                        results.append((mid, "FAILED_OTHERWISE", "failed, but not with '%s'" % expected))
                    elif verdict == "PASS":
                        results.append((mid, "SURVIVED", desc))
                    else:
                        results.append((mid, "INCOMPLETE", "entry verdict %s exit %d: %s" % (verdict, code, out.strip().splitlines()[-3:])))
            finally:
                with open(path, "wb") as f:
                    f.write(original)
                if sha(open(path, "rb").read()) != sha(original):
                    restore_ok = False
                    raise RuntimeError("restore failed")
    finally:
        with open(path, "wb") as f:
            f.write(original)
        restore_ok = restore_ok and sha(open(path, "rb").read()) == sha(original)
    if not restore_ok:
        print("RESTORE FAILED — original bytes kept at %s" % backup)
        return 2

    clean = None
    if not a.compile_only and all(r[1] == "CAUGHT" for r in results):
        verdict, code, out, report = entry("nocam-clean")
        clean = "PASS" if verdict == "PASS" else ("FAILED" if verdict == "TESTS_FAILED" else "INCOMPLETE (%s, exit %d)" % (verdict, code))
    for r in results:
        print("%s %-16s %s" % r)
    if clean:
        print("clean   %s" % clean)
    print("restored %s (sha256 verified)" % TARGET)
    if a.compile_only:
        return 0 if all(r[1] == "COMPILES" for r in results) else 1
    if any(r[1] == "INCOMPLETE" for r in results) or (clean and clean.startswith("INCOMPLETE")):
        return 3
    return 0 if all(r[1] == "CAUGHT" for r in results) and clean == "PASS" else 1


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
