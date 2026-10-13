"""Phase 2 (docs/57 §4, §5, §12): deterministic test selector — SHADOW MODE (it plans, it never runs tests).

    python -I docs/artifacts/test-orchestrator/plan_tests.py [--label L] [--context dev|gate|merge|release|architecture]
           [--probe [EditMode:|PlayMode:]Class[.Method]] ... [--out DIR] [--durations-record auto|none|DIR]
    python -I docs/artifacts/test-orchestrator/plan_tests.py --changeset FILE.json [...]     (synthetic input, no git)
    python -I docs/artifacts/test-orchestrator/plan_tests.py --check-map                      (rule-table completeness)

Run scopes (Q1: R0–R3, not docs/12's L0–L6):
    R0 no Unity tests · R1 EditMode full · R2 EditMode full + selected PlayMode · R3 EditMode full + PlayMode full
The change set is "last verified snapshot -> now" (never "previous commit -> now"); no trustworthy snapshot, an
unknown path or a rule that cannot be evaluated => R3. Explicit probes are never auto-selected; --probe adds one.
Exit: 0 plan written · 2 usage / plan error · 3 --check-map found problems.
"""
import argparse
import datetime
import difflib
import os
import re
import sys

sys.dont_write_bytecode = True   # never leave __pycache__ in docs/ (it would itself be a change)
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import orchestrator_lib as lib  # noqa: E402
import snapshot as snap_mod  # noqa: E402

TIERS = ("R0", "R1", "R2", "R3")
CONTEXTS = ("dev", "gate", "merge", "release", "architecture")
SAFE_ENTRY = "bash docs/artifacts/editor-memory/safe_unity_test.sh"
NEW_FIXTURE_SECONDS = 30.0


class PlanError(Exception):
    pass


def tier_max(a, b):
    return a if TIERS.index(a) >= TIERS.index(b) else b


# =====================================================================================================================
# repository facts
# =====================================================================================================================

_CLASS = re.compile(r"^\s*(?:public|internal)\s+(?:sealed\s+|static\s+|abstract\s+|partial\s+)*class\s+(\w+)", re.M)


def scan_fixtures(repo, folder):
    """{class: {"file": path, "probe": bool, "hasExplicit": bool}} for test classes in one test folder."""
    out = {}
    root = os.path.join(repo, folder)
    if not os.path.isdir(root):
        return out
    for name in sorted(os.listdir(root)):
        if not name.endswith(".cs"):
            continue
        rel = folder + name
        with open(os.path.join(root, name), encoding="utf-8", errors="replace") as f:
            text = f.read()
        for m in _CLASS.finditer(text):
            before = text[:m.start()].splitlines()[-8:]
            attrs = " ".join(l for l in before if l.strip().startswith("["))
            probe = "Explicit" in attrs or 'Category("Probe")' in attrs
            out.setdefault(m.group(1), {"file": rel, "probe": probe, "hasExplicit": "Explicit" in text})
    return out


def parse_writer_files(repo, source):
    """Authority files straight from ArchitectureRegressionTests.WriterRules (Test-as-Spec). None = unparseable."""
    try:
        with open(os.path.join(repo, source), encoding="utf-8", errors="replace") as f:
            text = f.read()
    except OSError:
        return None
    m = re.search(r"WriterRules\s*=\s*\{(.*?)\n\s*\};", text, re.S)
    if not m:
        return None
    files = set()
    for allowed in re.findall(r"AllowedFiles\s*=\s*new\s*\[\]\s*\{([^}]*)\}", m.group(1)):
        files.update(re.findall(r'"([^"]+\.cs)"', allowed))
    return sorted(files) or None


def load_facts(repo, map_path, durations_record=None):
    tmap = lib.load_json(map_path)
    if tmap.get("schema") != 1:
        raise PlanError("test-map.json: unsupported schema")
    facts = {
        "map": tmap,
        "mapHash": lib.sha256_file(map_path),
        "writerFiles": parse_writer_files(repo, tmap["writerRulesSource"]),
        "playFixtures": scan_fixtures(repo, tmap["testFolders"]["playMode"]),
        "editFixtures": scan_fixtures(repo, tmap["testFolders"]["editMode"]),
        "universe": {},          # fixture -> [FullName] (from a full PlayMode record), for substring over-match
        "seconds": {k: float(v["seconds"]) for k, v in tmap["playModeFixtures"].items()},
        "durationsSource": "test-map.json defaults",
    }
    if durations_record is not None:
        if durations_record.results is None:
            raise PlanError("durations record has no results: %s" % durations_record.dir)
        secs = {}
        for r in durations_record.results:
            fx = lib.fixture_of(r["FullName"])
            facts["universe"].setdefault(fx, []).append(r["FullName"])
            secs[fx] = secs.get(fx, 0.0) + float(r.get("Duration") or 0.0)
        facts["seconds"].update({k: round(v, 1) for k, v in secs.items()})
        facts["durationsSource"] = lib.norm_path(os.path.relpath(durations_record.dir, repo))
    return facts


# =====================================================================================================================
# per-path rules
# =====================================================================================================================

def _layer(path):
    parts = path.split("/")
    if len(parts) < 4 or parts[0] != "Assets" or parts[1] != "Scripts":
        return None
    top = parts[2]
    if top in ("Core", "Presentation") and len(parts) > 4:
        return top + "." + parts[3]
    return top


_USING = re.compile(r"^\s*using\s+Project\.([A-Za-z]+)(?:\.([A-Za-z]+))?")


def _added_lines(old, new):
    out = []
    for line in difflib.unified_diff((old or "").splitlines(), (new or "").splitlines(), lineterm="", n=0):
        if line.startswith("+") and not line.startswith("+++"):
            out.append(line[1:])
    return out


def content_rules(change, facts, runtime):
    """E4 on added lines: new cross-layer `using`, new implementer of a core interface. Returns (tier, rule, detail)."""
    path, status = change["path"], change["status"]
    old, new = change.get("old"), change.get("new")
    if new is None or (status == "M" and old is None):
        return "R3", "CONTENT_UNEVALUABLE", "previous or current content of a .cs file could not be read"
    added = _added_lines(old if status == "M" else "", new)
    if runtime:
        own = _layer(path)
        for line in added:
            m = _USING.match(line)
            if not m:
                continue
            top, sub = m.group(1), m.group(2)
            target = top + "." + sub if top in ("Core", "Presentation") and sub else top
            if target != own:
                return "R3", "E4", "new dependency %s -> %s: `%s`" % (own, target, line.strip())
    names = "|".join(re.escape(n) for n in facts["map"]["coreInterfaces"])
    impl = re.compile(r"\b(class|struct)\s+\w+[^{;]*:\s*[^{;]*\b(" + names + r")\b")
    for line in added:
        if impl.search(line):
            return "R3", "E4", "new implementer of a core interface: `%s`" % line.strip()
    return None


def classify_change(change, facts):
    """-> dict(tier, rule, detail, fixtures[], staticChecks[], runtime: bool)."""
    tmap = facts["map"]
    path = lib.norm_path(change["path"])
    base = path[:-5] if path.endswith(".meta") else path
    status = change["status"]
    res = {"path": path, "status": status, "tier": "R3", "rule": "UNMAPPED", "detail": "", "fixtures": [],
           "staticChecks": [], "runtime": False}

    def done(tier, rule, detail="", fixtures=(), checks=(), runtime=False):
        res.update(tier=tier, rule=rule, detail=detail, fixtures=sorted(set(fixtures)), staticChecks=list(checks),
                   runtime=runtime)
        return res

    for infra in tmap["infrastructure"]:
        if lib.matches(path, infra["pattern"]):
            return done("R3", "E6", "test infrastructure: " + infra["reason"], checks=infra["staticChecks"])
    if lib.first_match(path, tmap["skillMirrorRoots"]):
        return done("R0", "SKILL", "agent skill (outside Unity)",
                    checks=["python -I docs/artifacts/test-orchestrator/check_skill_mirror.py"])
    if not lib.first_match(path, tmap["unityVisibleRoots"]):
        return done("R0", "UNITY_INVISIBLE", "outside Assets/, Packages/, ProjectSettings/: the Editor never loads it")

    hr = next((h for h in tmap["highRisk"] if lib.matches(base, h["pattern"])), None)
    if hr:
        return done("R3", hr["rule"], hr["reason"])

    is_cs = base.endswith(".cs")
    deleted = status == "D" and path == base     # a deleted .cs (its .meta follows the same rule)
    edit_folder, play_folder = tmap["testFolders"]["editMode"], tmap["testFolders"]["playMode"]

    # ---- tests ----------------------------------------------------------------------------------------------------
    if base.startswith(edit_folder) or base.startswith(play_folder):
        if not is_cs:
            return done("R3", "E6", "non-code file in a test folder")
        if status == "D":
            return done("R3", "E9", "deleted test file")
        if base.startswith(edit_folder):
            return done("R1", "EDITMODE_TEST", "EditMode runs in full anyway")
        stem = os.path.splitext(os.path.basename(base))[0]
        classes = [m.group(1) for m in _CLASS.finditer(change.get("new") or "")] or [stem]
        fixtures, probes = [], []
        for cls in classes:
            if cls in tmap["playModeFixtures"]:
                fixtures.append(cls)
            elif cls in tmap["probeFixtures"]:
                probes.append(cls)
            else:
                return done("R3", "UNMAPPED_FIXTURE", "PlayMode class %s is not in test-map.json" % cls)
        if fixtures:
            return done("R2", "PLAYMODE_TEST", "changed PlayMode fixture", fixtures=fixtures)
        return done("R1", "PROBE_CHANGED", "explicit probe %s: never auto-run (use --probe)" % ",".join(probes))

    # ---- editor code ----------------------------------------------------------------------------------------------
    if base.startswith("Assets/Scripts/Editor/"):
        if not is_cs:
            return done("R3", "UNMAPPED", "non-code file under Editor/")
        if deleted:
            return done("R3", "E9", "deleted editor script")
        entry = next((e for e in tmap["editorFiles"] if e["path"] == base), None)
        if entry is None:
            return done("R3", "UNMAPPED_EDITOR", "editor script not classified in test-map.json (§12.4)")
        text = change.get("new")
        if text is None:
            return done("R3", "CONTENT_UNEVALUABLE", "editor script content unreadable")
        auto = [m for m in tmap["editorAutoRunMarkers"] if m in text]
        if auto:
            return done("R3", "EDITOR_AUTORUN", "runs without user action (%s)" % ",".join(auto))
        writes = [m for m in tmap["editorWriteMarkers"] if m in text]
        if entry["class"] == "readOnly" and writes:
            return done("R3", "MAP_INCONSISTENT", "declared readOnly but writes (%s)" % ",".join(writes))
        if entry["class"] == "readOnly":
            return done("R1", "EDITOR_READONLY", "inspector / analysis / debug window")
        if entry["class"] == "writerScoped":
            return done("R2", "EDITOR_WRITER", "products %s" % ",".join(entry["products"]), fixtures=entry["fixtures"])
        return done("R3", "EDITOR_WRITER_UNIVERSAL", entry.get("reason", "universal products"))

    # ---- runtime code ---------------------------------------------------------------------------------------------
    if base.startswith("Assets/Scripts/"):
        if not is_cs:
            return done("R3", "UNMAPPED", "non-code file under Assets/Scripts")
        if deleted:
            return done("R3", "E9", "deleted runtime script")
        writers = facts["writerFiles"]
        if writers is None:
            return done("R3", "WRITER_RULES_UNPARSEABLE", "cannot read WriterRules from %s" % tmap["writerRulesSource"])
        if os.path.basename(base) in writers:
            return done("R3", "E2", "blackboard single-writer authority (WriterRules)")
        module = next((m for m in tmap["runtimeModules"] if lib.first_match(base, m["patterns"])), None)
        if module is None:
            return done("R3", "UNMAPPED", "runtime script outside every module in test-map.json")
        if path == base:
            hit = content_rules(change, facts, runtime=True)
            if hit:
                return done(hit[0], hit[1], hit[2])
        return done("R2", "MODULE", module["name"], fixtures=module["fixtures"], runtime=True)

    return done("R3", "UNMAPPED_ASSET", "Unity-visible file outside every rule")


# =====================================================================================================================
# PlayMode selection under the CURRENT entry (one substring filter per call; Phase 4 not approved)
# =====================================================================================================================

def _names(facts, fixture, ns):
    return facts["universe"].get(fixture) or [ns + fixture]


def select_filters(selected, facts):
    tmap = facts["map"]
    ns = tmap["playModeNamespace"]
    overhead = float(tmap["costModel"]["invocationOverheadSeconds"])
    runnable = sorted(tmap["playModeFixtures"])
    secs = lambda f: facts["seconds"].get(f, NEW_FIXTURE_SECONDS)  # noqa: E731

    cands = {ns + f for f in selected}
    sel = sorted(selected)
    for i in range(len(sel)):
        for j in range(i + 1, len(sel)):
            a, b = ns + sel[i], ns + sel[j]
            k = 0
            while k < min(len(a), len(b)) and a[k] == b[k]:
                k += 1
            if k >= len(ns) + 4:
                cands.add(a[:k])

    def matched(c):
        lc = c.lower()
        return {f for f in runnable if any(lc in n.lower() for n in _names(facts, f, ns))}

    info = {c: matched(c) for c in sorted(cands)}
    cost = {c: overhead + sum(secs(f) for f in info[c]) for c in info}
    uncovered, chosen = set(selected), []
    while uncovered:
        best = min(info, key=lambda c: (-(len(info[c] & uncovered) / cost[c]), cost[c], c))
        if not info[best] & uncovered:
            raise PlanError("no filter covers %s" % sorted(uncovered))
        chosen.append(best)
        uncovered -= info[best]
    matched_all = set().union(*(info[c] for c in chosen)) if chosen else set()
    total = sum(cost[c] for c in chosen)
    full = overhead + sum(secs(f) for f in runnable)
    return chosen, matched_all, round(total, 1), round(full, 1)


# =====================================================================================================================
# plan
# =====================================================================================================================

def resolve_probe(spec, facts):
    mode = "PlayMode"
    if ":" in spec:
        mode, spec = spec.split(":", 1)
    if mode not in lib.MODES:
        raise PlanError("--probe mode must be EditMode or PlayMode")
    ns = "Project.Tests.%s." % mode
    name = spec[len(ns):] if spec.startswith(ns) else spec
    if not re.fullmatch(r"\w+(\.\w+)?", name):
        raise PlanError("--probe must be Class or Class.Method (got %r)" % spec)
    cls = name.split(".")[0]
    scanned = facts["playFixtures" if mode == "PlayMode" else "editFixtures"].get(cls)
    if scanned is None:
        raise PlanError("--probe %s: class %s not found in the %s test folder" % (spec, cls, mode))
    if not (scanned["probe"] or scanned["hasExplicit"]):
        raise PlanError("--probe %s: %s has no [Explicit] tests (run it through a tier instead)" % (spec, cls))
    return {"mode": mode, "filter": ns + name, "includeExplicit": True, "kind": "probe"}


def build_plan(changes, facts, snapshot_reason, context="dev", probes=(), label="plan", pre_state=None,
               snapshot_info=None):
    tmap = facts["map"]
    if context not in CONTEXTS:
        raise PlanError("--context must be one of %s" % ",".join(CONTEXTS))
    probe_invocations = [resolve_probe(p, facts) for p in probes]

    items = [classify_change(c, facts) for c in sorted(changes, key=lambda c: lib.norm_path(c["path"]))]
    reasons = []
    tier = "R0"
    for it in items:
        tier = tier_max(tier, it["tier"])
        if it["tier"] in ("R3",) or it["rule"] not in ("MODULE", "UNITY_INVISIBLE"):
            reasons.append({"tier": it["tier"], "rule": it["rule"], "path": it["path"], "detail": it["detail"]})
    if snapshot_reason:
        tier = "R3"
        reasons.append({"tier": "R3", "rule": "S3", "path": "", "detail": snapshot_reason})
    if context != "dev":
        tier = "R3"
        reasons.append({"tier": "R3", "rule": "Q4", "path": "", "detail": "context '%s' requires R3" % context})
    if not items and not snapshot_reason and context == "dev":
        reasons.append({"tier": "R0", "rule": "NO_CHANGE", "path": "", "detail": "nothing changed since the last verified snapshot"})

    checks = sorted({c for it in items for c in it["staticChecks"]})
    cs_paths = [it["path"] for it in items if it["path"].endswith(".cs") and it["status"] != "D"]
    if cs_paths:
        checks.append("dotnet build Project.Runtime.csproj -v q --nologo -p:ResolveAssemblyReferenceIgnoreTargetFrameworkAttributeVersionMismatch=true  (compile-without-unity; only proves compilation)")

    runnable = sorted(tmap["playModeFixtures"])
    overhead = float(tmap["costModel"]["invocationOverheadSeconds"])
    full_estimate = round(overhead + sum(facts["seconds"].get(f, NEW_FIXTURE_SECONDS) for f in runnable), 1)
    play = {"selection": "none", "filters": [], "selectedFixtures": [], "matchedFixtures": [], "notRun": [],
            "estimateSeconds": 0.0, "fullEstimateSeconds": full_estimate}
    invocations = []
    if tier != "R0":
        invocations.append({"mode": "EditMode", "filter": "", "includeExplicit": False, "kind": "tier"})
    if tier == "R2":
        selected = {f for it in items if it["tier"] == "R2" for f in it["fixtures"]}
        if any(it["runtime"] and it["tier"] == "R2" for it in items):
            selected |= set(tmap["smokeFixtures"])
        filters, matched, est, full = select_filters(selected, facts)
        play.update(selectedFixtures=sorted(selected), fullEstimateSeconds=full)
        if est >= full:
            play.update(selection="full", estimateSeconds=full, matchedFixtures=runnable,
                        costNote="selected filters (%.0f s) are not cheaper than a full run (%.0f s)" % (est, full))
        else:
            play.update(selection="filters", filters=filters, matchedFixtures=sorted(matched), estimateSeconds=est)
            for f in runnable:
                if f not in matched:
                    play["notRun"].append({"fixture": f, "reason": "R2: no changed path maps to it (deferred to the next R3)"})
    if tier == "R3" or play["selection"] == "full":
        play.update(selection="full", matchedFixtures=runnable, estimateSeconds=full_estimate)
    if play["selection"] == "full":
        invocations.append({"mode": "PlayMode", "filter": "", "includeExplicit": False, "kind": "tier"})
    elif play["selection"] == "filters":
        for flt in play["filters"]:
            invocations.append({"mode": "PlayMode", "filter": flt, "includeExplicit": False, "kind": "tier"})
    if tier in ("R0", "R1"):
        why = "R0: no Unity-visible change" if tier == "R0" else "R1: no change reaches PlayMode"
        play["notRun"] = [{"fixture": f, "reason": why} for f in runnable]
    edit = {"run": tier != "R0", "reason": "R0: no Unity-visible change" if tier == "R0" else "EditMode always runs in full (Q3)"}
    for p in sorted(tmap["probeFixtures"]):
        play["notRun"].append({"fixture": p, "reason": "explicit probe: never auto-selected (S4)"})
    invocations += probe_invocations
    for i, inv in enumerate(invocations):
        parts = [SAFE_ENTRY, "--mode", inv["mode"]]
        if inv["filter"]:
            parts += ["--filter", "'%s'" % inv["filter"]]
        if inv["includeExplicit"]:
            parts.append("--include-explicit")
        parts += ["--label", "%s-%d-%s" % (label, i + 1, inv["mode"][:4].lower())]
        inv["command"] = " ".join(parts)

    return {
        "schema": 1,
        "plannerVersion": lib.PLANNER_VERSION,
        "context": context,
        "mapSha256": facts["mapHash"],
        "writerFiles": facts["writerFiles"],
        "durationsSource": facts["durationsSource"],
        "snapshot": snapshot_info or {"status": "invalid" if snapshot_reason else "valid", "reason": snapshot_reason},
        "tier": tier,
        "reasons": sorted(reasons, key=lambda r: (-TIERS.index(r["tier"]), r["rule"], r["path"])),
        "changes": [{k: it[k] for k in ("path", "status", "tier", "rule", "detail", "fixtures")} for it in items],
        "staticChecks": checks,
        "editMode": edit,
        "playMode": play,
        "invocations": invocations,
        "preState": pre_state,
    }


def to_markdown(plan):
    L = ["# Test plan — %s (shadow mode: plan only)" % plan["tier"], ""]
    top = [r for r in plan["reasons"] if r["tier"] == plan["tier"]][:8]
    L.append("**Why %s:** " % plan["tier"] + ("; ".join("%s %s %s" % (r["rule"], r["path"], r["detail"]) for r in top) or "—"))
    L += ["", "- context: %s · snapshot: %s %s" % (plan["context"], plan["snapshot"]["status"], plan["snapshot"].get("reason") or ""),
          "- EditMode: %s" % ("full" if plan["editMode"]["run"] else "not run — " + plan["editMode"]["reason"]),
          "- PlayMode: %s (estimate %s s, full %s s)" % (plan["playMode"]["selection"], plan["playMode"]["estimateSeconds"],
                                                        plan["playMode"]["fullEstimateSeconds"]), ""]
    L.append("## Invocations (all through safe_unity_test.sh)")
    for inv in plan["invocations"]:
        L.append("- `%s`" % inv["command"])
    if not plan["invocations"]:
        L.append("- none")
    L += ["", "## Not run"]
    for n in plan["playMode"]["notRun"]:
        L.append("- %s — %s" % (n["fixture"], n["reason"]))
    if not plan["editMode"]["run"]:
        L.append("- EditMode (all) — %s" % plan["editMode"]["reason"])
    L += ["", "## Static checks (outside Unity)"] + ["- `%s`" % c for c in plan["staticChecks"]] + ["", "## Changes"]
    for c in plan["changes"]:
        L.append("- %s %s → %s %s %s" % (c["status"], c["path"], c["tier"], c["rule"], c["detail"]))
    return "\n".join(L) + "\n"


# =====================================================================================================================
# map completeness (AC2)
# =====================================================================================================================

def check_map(repo, facts):
    tmap, problems = facts["map"], []
    if facts["writerFiles"] is None:
        problems.append("WriterRules could not be parsed from %s" % tmap["writerRulesSource"])
    scripts = []
    for root, _, files in os.walk(os.path.join(repo, "Assets", "Scripts")):
        for f in files:
            if f.endswith(".cs"):
                scripts.append(lib.norm_path(os.path.relpath(os.path.join(root, f), repo)))
    declared_editor = {e["path"]: e for e in tmap["editorFiles"]}
    for p in sorted(scripts):
        if p.startswith("Assets/Scripts/Editor/"):
            e = declared_editor.get(p)
            if e is None:
                problems.append("editor script not classified: %s" % p)
                continue
            with open(os.path.join(repo, p), encoding="utf-8", errors="replace") as f:
                text = f.read()
            writes = [m for m in tmap["editorWriteMarkers"] if m in text]
            if e["class"] == "readOnly" and writes:
                problems.append("%s declared readOnly but contains %s" % (p, ",".join(writes)))
            continue
        if any(lib.matches(p, h["pattern"]) for h in tmap["highRisk"]):
            continue
        if facts["writerFiles"] and os.path.basename(p) in facts["writerFiles"]:
            continue
        hits = [m["name"] for m in tmap["runtimeModules"] if lib.first_match(p, m["patterns"])]
        if not hits:
            problems.append("runtime script in no module: %s" % p)
        elif len(hits) > 1:
            problems.append("runtime script in several modules %s: %s" % (hits, p))
    for p in declared_editor:
        if p not in scripts:
            problems.append("declared editor script does not exist: %s" % p)
    for w in facts["writerFiles"] or []:
        if not any(os.path.basename(s) == w for s in scripts):
            problems.append("WriterRules file not found under Assets/Scripts: %s" % w)
    scanned = facts["playFixtures"]
    declared = set(tmap["playModeFixtures"]) | set(tmap["probeFixtures"])
    for cls, info in sorted(scanned.items()):
        if cls not in declared:
            problems.append("PlayMode class not in test-map.json: %s" % cls)
        elif info["probe"] != (cls in tmap["probeFixtures"]):
            problems.append("probe flag mismatch for %s (source says probe=%s)" % (cls, info["probe"]))
    for cls in sorted(declared - set(scanned)):
        problems.append("test-map.json lists a PlayMode class that does not exist: %s" % cls)
    refs = set(tmap["smokeFixtures"])
    for m in tmap["runtimeModules"]:
        refs |= set(m["fixtures"])
    for e in tmap["editorFiles"]:
        refs |= set(e.get("fixtures", []))
    for f in sorted(refs - set(tmap["playModeFixtures"])):
        problems.append("rule references unknown fixture: %s" % f)
    return problems


# =====================================================================================================================
# entry point
# =====================================================================================================================

def repo_root():
    return os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", ".."))


def main(argv):
    here = os.path.dirname(os.path.abspath(__file__))
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--repo", default=repo_root())
    ap.add_argument("--map", default=os.path.join(here, "test-map.json"))
    ap.add_argument("--snapshot", default=None)
    ap.add_argument("--context", default="dev")
    ap.add_argument("--probe", action="append", default=[])
    ap.add_argument("--label", default="plan")
    ap.add_argument("--out", default=None)
    ap.add_argument("--changeset", default=None)
    ap.add_argument("--durations-record", default="auto")
    ap.add_argument("--check-map", action="store_true")
    ap.add_argument("--now", default=None, help=argparse.SUPPRESS)
    a = ap.parse_args(argv)
    lib.utf8_stdout()
    if not re.fullmatch(r"[A-Za-z0-9_-]{1,40}", a.label):
        print("usage: --label must match [A-Za-z0-9_-]{1,40}", file=sys.stderr)
        return 2
    repo = os.path.abspath(a.repo)
    snapshot_path = a.snapshot or os.path.join(repo, "Logs", "test-orchestrator", "last-verified.json")
    try:
        rec = None
        if a.durations_record == "auto" and not a.changeset:
            rec = lib.find_latest_full_record(os.path.join(repo, "Logs", "safe-test"), "PlayMode")
        elif a.durations_record not in ("auto", "none"):
            rec = lib.Record(a.durations_record)
        facts = load_facts(repo, a.map, rec)
        if a.check_map:
            problems = check_map(repo, facts)
            for p in problems:
                print("MAP: " + p)
            print("check-map: %s (%d problem(s))" % ("OK" if not problems else "FAILED", len(problems)))
            return 3 if problems else 0

        now = datetime.datetime.strptime(a.now, "%Y-%m-%dT%H:%M:%SZ").replace(tzinfo=datetime.timezone.utc) if a.now else None
        if a.changeset:
            cs = lib.load_json(a.changeset)
            if "facts" in cs:
                facts.update(cs["facts"])
            snap_reason = cs.get("snapshot", {}).get("reason") if not cs.get("snapshot", {}).get("valid", False) else None
            plan = build_plan(cs["changes"], facts, snap_reason, a.context, a.probe, a.label,
                              snapshot_info={"status": "synthetic", "reason": snap_reason})
        else:
            snap, why = snap_mod.load(repo, snapshot_path, facts["mapHash"], facts["map"]["snapshotMaxAgeDays"], now)
            if snap:
                changes, state = snap_mod.change_set(repo, snap)
                info = {"status": "valid", "reason": None, "baseCommit": snap["baseCommit"], "createdUtc": snap["createdUtc"]}
            else:
                state = snap_mod.worktree_state(repo)
                changes = [{"path": p, "status": "A" if h != snap_mod.ABSENT else "D"} for p, h in state["worktree"].items()]
                info = {"status": "invalid", "reason": why}
            plan = build_plan(changes, facts, why, a.context, a.probe, a.label, pre_state=state, snapshot_info=info)
    except (PlanError, lib.GitError, KeyError, ValueError, OSError) as e:
        print("PLAN_ERROR: %s: %s" % (e.__class__.__name__, e), file=sys.stderr)
        return 2

    out = a.out or os.path.join(repo, "Logs", "test-orchestrator",
                                datetime.datetime.now(datetime.timezone.utc).strftime("%Y%m%dT%H%M%SZ") + "-" + a.label)
    os.makedirs(out, exist_ok=True)
    with open(os.path.join(out, "plan.json"), "w", encoding="utf-8") as f:
        f.write(lib.dump_json(plan))
    md = to_markdown(plan)
    with open(os.path.join(out, "plan.md"), "w", encoding="utf-8") as f:
        f.write(md)
    print(md, end="")
    print("plan: %s" % os.path.join(out, "plan.json"))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
