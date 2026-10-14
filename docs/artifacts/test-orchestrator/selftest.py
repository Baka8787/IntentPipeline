"""Self-tests for the test orchestrator (docs/57 §9, §12). No Unity, no network; temp git repos only.

    python -I docs/artifacts/test-orchestrator/selftest.py [-v]

Suites:  MAP  live rule table is complete (AC2)          PLAN  synthetic change sets (fixtures/planner-cases.json)
         DET  determinism (AC1)                          GIT   snapshot / change-set mechanics in throwaway repos
         CLS  known-failure classification (AC9)         SHD   shadow comparison (escapes, invalid samples)
         RPT  shadow-period report (benefit, R3 share, pending escape investigations)
         MIR  .claude / .agents skill mirror (S6)
Exit 0 = every case passed.
"""
import contextlib
import datetime
import io
import json
import os
import shutil
import subprocess
import sys
import tempfile

sys.dont_write_bytecode = True
HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import check_skill_mirror as mirror  # noqa: E402
import classify_results as classify  # noqa: E402
import orchestrator_lib as lib  # noqa: E402
import plan_tests as planner  # noqa: E402
import shadow_compare as shadow  # noqa: E402
import shadow_report as sreport  # noqa: E402
import snapshot as snap_mod  # noqa: E402

REPO = os.path.abspath(os.path.join(HERE, "..", "..", ".."))
MAP = os.path.join(HERE, "test-map.json")
VERBOSE = "-v" in sys.argv
RESULTS = {}

# Real Unity outputs (Logs/safe-test 20261013T095531Z / 20261013T100953Z), copied verbatim.
K1_NAME = "Project.Tests.EditMode.StairAuthoringContractTests.Scene_InstanceMasks_MatchSemanticTable"
K1_MSG = ("  scene/X Bot FootIKController.settings.GroundLayers=503，應為 -9（腳落在看得到的踏面）\r\n  Expected: <empty>\r\n"
          "  But was:  < \"scene/X Bot FootIKController.settings.GroundLayers=503，應為 -9（腳落在看得到的踏面）\" >\r\n")
C0_NAME = "Project.Tests.PlayMode.MotionDriverCommittedMovementPlayModeTests.C0_TraversalDoesNotReportGroundEdgesFromIncidentalContact"
C0_MSG = "  陽性對照失敗：非 traversal 的 false→true 邊沿本來就該回報落地\r\n  Expected: True\r\n  But was:  False\r\n"


def check(suite, name, cond, detail=""):
    RESULTS.setdefault(suite, []).append((name, bool(cond), detail))
    if VERBOSE or not cond:
        print("  %s %s %s %s" % ("PASS" if cond else "FAIL", suite, name, "" if cond else detail))


# =====================================================================================================================
# helpers
# =====================================================================================================================

def make_record(root, name, mode, exit_code, results=None, filt="", explicit=0, check_only=0, head="abc1234",
                verdict=None, status="completed"):
    d = os.path.join(root, name)
    os.makedirs(d)
    with open(os.path.join(d, "meta.txt"), "w", encoding="utf-8") as f:
        f.write("start_utc=2026-10-13T00:00:00Z\nmode=%s\nfilter=%s\ninclude_explicit=%d\ntimeout=default\n"
                "check_only=%d\nproject=X\ngit_head=%s\n" % (mode, filt, explicit, check_only, head))
    if exit_code is not None:
        with open(os.path.join(d, "summary.txt"), "w", encoding="utf-8") as f:
            f.write("verdict=%s\nexit=%d\nreason=r\nend_utc=x\n" % (verdict or {0: "PASS", 1: "TESTS_FAILED"}.get(exit_code, "SAFETY_STOP"), exit_code))
    if results is not None:
        inner = {"status": status, "duration": 1.0, "summary": {"total": len(results)}, "results": [
            {"FullName": n, "Status": s, "Duration": 0.1, "Message": m, "StackTrace": None} for n, s, m in results]}
        outer = {"success": True, "data": {"command": "test_status", "result": json.dumps(inner, ensure_ascii=False)}}
        with open(os.path.join(d, "%s1-test_status.json" % name), "w", encoding="utf-8") as f:
            json.dump(outer, f, ensure_ascii=False)
    return d


def known_list(root, entries, name="known.json"):
    p = os.path.join(root, name)
    with open(p, "w", encoding="utf-8") as f:
        json.dump({"schema": 1, "entries": entries}, f, ensure_ascii=False)
    return p


def entry(eid, mode, full, symptoms, **extra):
    e = {"id": eid, "mode": mode, "fullName": full, "symptoms": symptoms, "cause": "c", "ref": "r",
         "approvedBy": "user", "approvedOn": "2026-10-13", "approvalRef": "docs/57 §14.1", "source": ["record x"],
         "reinvestigateWhen": ["when y"], "reviewBy": "x"}
    e.update(extra)
    return e


def gitc(repo, *args):
    p = subprocess.run(["git", "-C", repo, "-c", "user.name=t", "-c", "user.email=t@t", "-c", "core.autocrlf=false",
                        "-c", "commit.gpgsign=false", *args], stdout=subprocess.PIPE, stderr=subprocess.PIPE)
    if p.returncode != 0:
        raise RuntimeError("git %s: %s" % (args, p.stderr.decode()))
    return p.stdout.decode().strip()


def write(repo, rel, text):
    p = os.path.join(repo, rel)
    os.makedirs(os.path.dirname(p), exist_ok=True)
    with open(p, "w", encoding="utf-8", newline="\n") as f:
        f.write(text)


def temp_repo():
    d = tempfile.mkdtemp(prefix="orch-git-")
    gitc(d, "init", "-q")
    gitc(d, "config", "core.autocrlf", "false")
    write(d, "Assets/Scripts/Core/Movement/EnemyNavigator.cs", "// v1\n")
    write(d, "Assets/Scripts/Presentation/Motion/MotionDriver.cs", "// md v1\n")
    write(d, "Assets/Scenes/S.unity", "scene v1\n")
    write(d, "docs/a.md", "doc\n")
    gitc(d, "add", "-A")
    gitc(d, "commit", "-q", "-m", "c1")
    return d


def paths(changes):
    return {c["path"]: c["status"] for c in changes}


# =====================================================================================================================
# MAP / PLAN / DET
# =====================================================================================================================

def suite_map():
    facts = planner.load_facts(REPO, MAP)
    problems = planner.check_map(REPO, facts)
    check("MAP", "live repo rule table complete", not problems, "; ".join(problems[:5]))
    check("MAP", "WriterRules parsed (10 authority files)", facts["writerFiles"] and len(facts["writerFiles"]) >= 10,
          str(facts["writerFiles"]))
    for w in ("MotionDriver.cs", "CharacterPipelineRunner.cs", "AIMovementSource.cs", "LocomotionModel.cs"):
        check("MAP", "authority %s derived from WriterRules" % w, w in (facts["writerFiles"] or []))
    broken = tempfile.mkdtemp(prefix="orch-wr-")
    try:
        os.makedirs(os.path.join(broken, "x"))
        with open(os.path.join(broken, "x", "A.cs"), "w") as f:
            f.write("class A { static readonly int[] NotWriterRules = { }; }")
        check("MAP", "WriterRules missing -> unparseable (None)", planner.parse_writer_files(broken, "x/A.cs") is None)
    finally:
        shutil.rmtree(broken, ignore_errors=True)


def run_case(case, facts_base):
    facts = dict(facts_base)
    facts.update(case.get("facts", {}))
    snap = case.get("snapshot", {"valid": True})
    reason = None if snap.get("valid") else snap.get("reason")
    return planner.build_plan(case["changes"], facts, reason, case.get("context", "dev"), case.get("probes", []),
                              "selftest", snapshot_info={"status": "synthetic", "reason": reason})


def suite_plan():
    facts = planner.load_facts(REPO, MAP)
    cases = lib.load_json(os.path.join(HERE, "fixtures", "planner-cases.json"))["cases"]
    for c in cases:
        exp, cid = c["expect"], "%s %s" % (c["id"], c["description"])
        try:
            plan = run_case(c, facts)
        except planner.PlanError as e:
            check("PLAN", cid, exp.get("error") is True, "unexpected PlanError: %s" % e)
            continue
        problems = []
        if exp.get("error"):
            problems.append("expected an error")
        if "tier" in exp and plan["tier"] != exp["tier"]:
            problems.append("tier %s != %s" % (plan["tier"], exp["tier"]))
        rules = {r["rule"] for r in plan["reasons"]} | {ch["rule"] for ch in plan["changes"]}
        for r in exp.get("rules", []):
            if r not in rules:
                problems.append("rule %s missing (got %s)" % (r, sorted(rules)))
        pm = plan["playMode"]
        if "selection" in exp and pm["selection"] != exp["selection"]:
            problems.append("selection %s != %s" % (pm["selection"], exp["selection"]))
        if "selectedExact" in exp and sorted(pm["selectedFixtures"]) != sorted(exp["selectedExact"]):
            problems.append("selected %s != %s" % (pm["selectedFixtures"], exp["selectedExact"]))
        for f in exp.get("selectedInclude", []):
            if f not in pm["selectedFixtures"]:
                problems.append("selected lacks %s" % f)
        if "filtersExact" in exp and pm["filters"] != exp["filtersExact"]:
            problems.append("filters %s != %s" % (pm["filters"], exp["filtersExact"]))
        not_run = {n["fixture"] for n in pm["notRun"]}
        for f in exp.get("notRunInclude", []):
            if f not in not_run:
                problems.append("notRun lacks %s" % f)
        for f in exp.get("notRunExclude", []):
            if f in not_run:
                problems.append("notRun wrongly has %s" % f)
        if "editMode" in exp and plan["editMode"]["run"] != exp["editMode"]:
            problems.append("editMode.run %s" % plan["editMode"]["run"])
        tier_inv = [i for i in plan["invocations"] if i["kind"] == "tier"]
        if "invocationCount" in exp and len(tier_inv) != exp["invocationCount"]:
            problems.append("tier invocations %d != %d" % (len(tier_inv), exp["invocationCount"]))
        probes = [i for i in plan["invocations"] if i["kind"] == "probe"]
        if "probeInvocations" in exp and len(probes) != exp["probeInvocations"]:
            problems.append("probe invocations %d" % len(probes))
        if "probeFilter" in exp and (not probes or probes[0]["filter"] != exp["probeFilter"] or not probes[0]["includeExplicit"]):
            problems.append("probe invocation wrong: %s" % probes)
        for s in exp.get("staticChecksContain", []):
            if not any(s in c2 for c2 in plan["staticChecks"]):
                problems.append("static check %s missing" % s)
        # invariants that must hold for EVERY plan
        for inv in plan["invocations"]:
            if not inv["command"].startswith(planner.SAFE_ENTRY) or "run_tests" in inv["command"]:
                problems.append("invocation bypasses the safe entry: %s" % inv["command"])
            if inv["kind"] == "tier" and inv["includeExplicit"]:
                problems.append("tier invocation includes explicit probes")
        if plan["tier"] != "R0" and not any(i["mode"] == "EditMode" and i["filter"] == "" for i in tier_inv):
            problems.append("EditMode is not run in full (Q3)")
        if plan["tier"] == "R3" and not any(i["mode"] == "PlayMode" and i["filter"] == "" for i in tier_inv):
            problems.append("R3 without a full PlayMode run")
        listed = set(pm["matchedFixtures"]) | not_run
        missing = set(facts["map"]["playModeFixtures"]) - listed
        if missing:
            problems.append("fixtures neither run nor listed as not-run: %s" % sorted(missing))
        check("PLAN", cid, not problems, "; ".join(problems))


def suite_det():
    facts = planner.load_facts(REPO, MAP)
    cases = lib.load_json(os.path.join(HERE, "fixtures", "planner-cases.json"))["cases"]
    same = True
    for c in cases:
        if c["expect"].get("error"):
            continue
        a = lib.dump_json(run_case(c, facts))
        b = lib.dump_json(run_case(json.loads(json.dumps(c)), planner.load_facts(REPO, MAP)))
        same &= a == b
    check("DET", "every synthetic plan is byte-identical when recomputed", same)
    order = cases[43]          # P44: three changes; reversing the input order must not change the plan
    rev = dict(order, changes=list(reversed(order["changes"])))
    check("DET", "change order does not matter", lib.dump_json(run_case(order, facts)) == lib.dump_json(run_case(rev, facts)))


# =====================================================================================================================
# GIT: snapshot semantics
# =====================================================================================================================

def suite_git():
    repo = temp_repo()
    side = tempfile.mkdtemp(prefix="orch-side-")          # snapshots live outside the repo (Logs/ is gitignored)
    snap_path = os.path.join(side, "snap.json")
    try:
        # long-lived WIP present at snapshot time
        write(repo, "Assets/Scenes/S.unity", "scene WIP\n")
        write(repo, "Assets/Scripts/Core/Movement/EnemyNavigator.cs", "// v2 WIP\n")
        st = snap_mod.worktree_state(repo, write_text_blobs=True)
        snap_mod.write(repo, snap_path, st, "M", [], [])
        snap, why = snap_mod.load(repo, snap_path, "M", 7)
        check("GIT", "fresh snapshot is valid", snap is not None, str(why))
        changes, _ = snap_mod.change_set(repo, snap)
        check("GIT", "unchanged long-lived WIP is not a change", changes == [], str(changes))

        write(repo, "Assets/Scenes/S.unity", "scene WIP edited again\n")
        changes, _ = snap_mod.change_set(repo, snap)
        check("GIT", "WIP edited again is a change", paths(changes) == {"Assets/Scenes/S.unity": "M"}, str(paths(changes)))
        write(repo, "Assets/Scenes/S.unity", "scene WIP\n")

        gitc(repo, "add", "Assets/Scenes/S.unity")
        gitc(repo, "commit", "-q", "-m", "commit WIP unchanged")
        snap, why = snap_mod.load(repo, snap_path, "M", 7)
        changes, _ = snap_mod.change_set(repo, snap)
        check("GIT", "committing WIP unchanged is not a change", changes == [] and snap is not None, "%s %s" % (why, changes))

        write(repo, "Assets/Scripts/Core/Movement/EnemyNavigator.cs", "// v3\n")
        changes, _ = snap_mod.change_set(repo, snap)
        c = next((x for x in changes if x["path"].endswith("EnemyNavigator.cs")), None)
        check("GIT", "dirty .cs old content recovered from the snapshot blob (-w)",
              c is not None and c["old"] == "// v2 WIP\n" and c["new"] == "// v3\n", str(c))

        lost = dict(snap, worktree=dict(snap["worktree"], **{"Assets/Scripts/Core/Movement/EnemyNavigator.cs": "0" * 40}))
        changes, _ = snap_mod.change_set(repo, lost)
        c = next((x for x in changes if x["path"].endswith("EnemyNavigator.cs")), None)
        check("GIT", "missing snapshot blob -> old content None (unevaluable)", c is not None and c["old"] is None, str(c))
        plan = planner.build_plan(changes, planner.load_facts(REPO, MAP), None)
        check("GIT", "unevaluable content -> R3", plan["tier"] == "R3", plan["tier"])

        write(repo, "Assets/Scripts/Core/Movement/EnemyNavigator.cs", "// v2 WIP\n")
        write(repo, "Assets/Scripts/Core/Movement/NewThing.cs", "// new\n")
        os.remove(os.path.join(repo, "docs/a.md"))
        changes, _ = snap_mod.change_set(repo, snap)
        check("GIT", "untracked file = A, deleted tracked file = D",
              paths(changes) == {"Assets/Scripts/Core/Movement/NewThing.cs": "A", "docs/a.md": "D"}, str(paths(changes)))
        os.remove(os.path.join(repo, "Assets/Scripts/Core/Movement/NewThing.cs"))
        gitc(repo, "checkout", "-q", "--", "docs/a.md")

        # anti-evasion (Q4): high-risk change split into a separate small commit is still in the change set
        write(repo, "Assets/Scripts/Presentation/Motion/MotionDriver.cs", "// md v2\n")
        gitc(repo, "add", "Assets/Scripts/Presentation/Motion/MotionDriver.cs")
        gitc(repo, "commit", "-q", "-m", "tiny 1")
        write(repo, "docs/a.md", "doc 2\n")
        gitc(repo, "add", "docs/a.md")
        gitc(repo, "commit", "-q", "-m", "tiny 2")
        changes, _ = snap_mod.change_set(repo, snap)
        plan = planner.build_plan(changes, planner.load_facts(REPO, MAP), None)
        check("GIT", "split commits: base is the snapshot, MotionDriver still forces R3",
              "Assets/Scripts/Presentation/Motion/MotionDriver.cs" in paths(changes) and plan["tier"] == "R3",
              "%s %s" % (paths(changes), plan["tier"]))

        # invalidation
        _, why = snap_mod.load(repo, os.path.join(repo, "nope.json"), "M", 7)
        check("GIT", "missing snapshot -> SNAPSHOT_MISSING", why and why.startswith("SNAPSHOT_MISSING"), str(why))
        _, why = snap_mod.load(repo, snap_path, "OTHER", 7)
        check("GIT", "rules changed -> SNAPSHOT_RULES_CHANGED", why and why.startswith("SNAPSHOT_RULES_CHANGED"), str(why))
        later = datetime.datetime.now(datetime.timezone.utc) + datetime.timedelta(days=8)
        _, why = snap_mod.load(repo, snap_path, "M", 7, now=later)
        check("GIT", "older than the limit -> SNAPSHOT_STALE", why and why.startswith("SNAPSHOT_STALE"), str(why))
        bad = os.path.join(side, "bad.json")
        for label, mutate, prefix in (
                ("base commit missing", lambda s: s.update(baseCommit="1" * 40), "SNAPSHOT_BASE_MISSING"),
                ("planner version changed", lambda s: s.update(plannerVersion="0"), "SNAPSHOT_PLANNER_CHANGED"),
                ("schema broken", lambda s: s.pop("worktree"), "SNAPSHOT_SCHEMA")):
            s = lib.load_json(snap_path)
            mutate(s)
            with open(bad, "w", encoding="utf-8") as f:
                json.dump(s, f)
            _, why = snap_mod.load(repo, bad, "M", 7)
            check("GIT", "%s -> %s" % (label, prefix), why and why.startswith(prefix), str(why))
        with open(bad, "w") as f:
            f.write("{not json")
        _, why = snap_mod.load(repo, bad, "M", 7)
        check("GIT", "unreadable -> SNAPSHOT_UNREADABLE", why and why.startswith("SNAPSHOT_UNREADABLE"), str(why))
        tree = gitc(repo, "rev-parse", "HEAD^{tree}")
        orphan = gitc(repo, "commit-tree", tree, "-m", "orphan")
        s = lib.load_json(snap_path)
        s["baseCommit"] = orphan
        with open(bad, "w", encoding="utf-8") as f:
            json.dump(s, f)
        _, why = snap_mod.load(repo, bad, "M", 7)
        check("GIT", "base not an ancestor of HEAD -> SNAPSHOT_BASE_NOT_ANCESTOR",
              why and why.startswith("SNAPSHOT_BASE_NOT_ANCESTOR"), str(why))

        a = snap_mod.worktree_state(repo)
        write(repo, "docs/a.md", "doc 3\n")
        check("GIT", "state_equal detects a content change", not snap_mod.state_equal(a, snap_mod.worktree_state(repo)))
    finally:
        shutil.rmtree(repo, ignore_errors=True)
        shutil.rmtree(side, ignore_errors=True)


# =====================================================================================================================
# CLS: classifier
# =====================================================================================================================

def suite_cls():
    root = tempfile.mkdtemp(prefix="orch-cls-")
    try:
        k1 = entry("K-1", "EditMode", K1_NAME, ["FootIKController.settings.GroundLayers=503"])
        c0 = entry("K-2", "PlayMode", C0_NAME, ["Expected: True", "But was: False"])
        known = known_list(root, [k1, c0])
        empty = known_list(root, [], "empty.json")
        p_ok = make_record(root, "p-pass", "PlayMode", 0, [("Project.Tests.PlayMode.A.B", "Passed", None)])
        e_k1 = make_record(root, "e-k1", "EditMode", 1, [(K1_NAME, "Failed", K1_MSG), ("Project.Tests.EditMode.X.Y", "Passed", None)])
        p_c0 = make_record(root, "p-c0", "PlayMode", 1, [(C0_NAME, "Failed", C0_MSG)])

        def run(recs, kp):
            return classify.classify(recs, kp)

        r = run([p_ok], empty)
        check("CLS", "C01 clean pass -> VERIFIED", r["verdict"] == "VERIFIED", r["verdict"])
        r = run([e_k1, p_c0], known)
        kinds = [f["kind"] for i in r["records"] for f in i["failures"]]
        check("CLS", "C02 real failures with approved symptoms -> VERIFIED_WITH_KNOWN_FAILURES",
              r["verdict"] == "VERIFIED_WITH_KNOWN_FAILURES" and kinds == ["KNOWN_MATCH", "KNOWN_MATCH"], "%s %s" % (r["verdict"], kinds))
        check("CLS", "C03 whitespace-normalised symptom matches NUnit's double space ('But was:  False')", kinds[-1:] == ["KNOWN_MATCH"])
        k1b = known_list(root, [entry("K-1", "EditMode", K1_NAME, ["GroundLayers=511"])], "k1b.json")
        r = run([e_k1], k1b)
        f0 = r["records"][0]["failures"][0]
        check("CLS", "C04 same test, different symptom -> NEW_FAILURE (symptomChangedFrom K-1) -> FAILED",
              r["verdict"] == "FAILED" and f0["kind"] == "NEW_FAILURE" and f0["symptomChangedFrom"] == "K-1"
              and f0["knownId"] is None, "%s %s" % (r["verdict"], f0))
        r = run([e_k1, p_c0], empty)
        check("CLS", "C05 unlisted failures (current state: list empty) -> NEW_FAILURE -> FAILED",
              r["verdict"] == "FAILED" and all(f["kind"] == "NEW_FAILURE" for i in r["records"] for f in i["failures"]), r["verdict"])
        e_k1_pass = make_record(root, "e-k1-pass", "EditMode", 0, [(K1_NAME, "Passed", None)])
        r = run([e_k1_pass], known)
        ki = r["records"][0]["known_items"]
        check("CLS", "C06 known test now passes -> KNOWN_NOT_OBSERVED (reported, verdict VERIFIED)",
              r["verdict"] == "VERIFIED" and ki and ki[0]["kind"] == "KNOWN_NOT_OBSERVED", str(ki))
        e_filtered = make_record(root, "e-filt", "EditMode", 0, [("Project.Tests.EditMode.X.Y", "Passed", None)], filt="X")
        r = run([e_filtered], known)
        ki = r["records"][0]["known_items"]
        check("CLS", "C07 filtered run without the known test -> KNOWN_NOT_RUN", ki and ki[0]["kind"] == "KNOWN_NOT_RUN", str(ki))
        e_full_missing = make_record(root, "e-full-missing", "EditMode", 0, [("Project.Tests.EditMode.X.Y", "Passed", None)])
        r = run([e_full_missing], known)
        ki = r["records"][0]["known_items"]
        check("CLS", "C08 full run lacks the known test -> KNOWN_MISSING_FROM_FULL_RUN (stale entry)",
              ki and ki[0]["kind"] == "KNOWN_MISSING_FROM_FULL_RUN", str(ki))
        r = run([e_k1], known)
        extra = r["knownNotRunModes"]
        check("CLS", "C09 known PlayMode entry while only EditMode ran -> KNOWN_NOT_RUN", extra and extra[0]["id"] == "K-2", str(extra))
        dirty = make_record(root, "dirty", "PlayMode", 15, None, filt="EnemyCombatPositioningMetrics", verdict="SAFETY_STOP")
        r = run([dirty], known)
        check("CLS", "C10 dirty-scene SAFETY_STOP (exit 15) -> BLOCKED", r["verdict"] == "BLOCKED", r["verdict"])
        for code in (2, 3, 4, 5):
            d = make_record(root, "inc%d" % code, "EditMode", code, None, verdict="X")
            r = run([d], known)
            check("CLS", "C11 entry exit %d -> INCOMPLETE" % code, r["verdict"] == "INCOMPLETE", r["verdict"])
        chk = make_record(root, "chk", "PlayMode", 0, None, check_only=1, verdict="CHECK_OK")
        r = run([chk], known)
        check("CLS", "C12 --check-only record -> INCOMPLETE (nothing submitted)", r["verdict"] == "INCOMPLETE", r["verdict"])
        nojson = make_record(root, "nojson", "EditMode", 1, None)
        r = run([nojson], known)
        check("CLS", "C13 exit 1 without result JSON -> INCOMPLETE (fail closed)", r["verdict"] == "INCOMPLETE", r["verdict"])
        running = make_record(root, "running", "EditMode", None, None)
        r = run([running], known)
        check("CLS", "C14 no summary (still running / killed) -> INCOMPLETE", r["verdict"] == "INCOMPLETE", r["verdict"])
        new_p = make_record(root, "p-new", "PlayMode", 1, [("Project.Tests.PlayMode.Z.Q", "Failed", "boom")])
        r = run([dirty, new_p], known)
        check("CLS", "C15 a new failure is never hidden by another record's BLOCKED", r["verdict"] == "FAILED", r["verdict"])
        r = run([dirty, p_ok], known)
        check("CLS", "C16 BLOCKED + pass -> BLOCKED", r["verdict"] == "BLOCKED", r["verdict"])
        alert = make_record(root, "alert", "PlayMode", 31, [(C0_NAME, "Failed", C0_MSG)], verdict="SAMPLER_ALERT")
        r = run([alert], known)
        check("CLS", "C17 sampler alert after a completed run -> BLOCKED, failures still listed",
              r["verdict"] == "BLOCKED" and r["records"][0]["failures"], r["verdict"])
        inc = make_record(root, "inconcl", "EditMode", 0, [("Project.Tests.EditMode.I.J", "Inconclusive", None)])
        r = run([inc], known)
        check("CLS", "C18 Inconclusive -> INCOMPLETE", r["verdict"] == "INCOMPLETE", r["verdict"])
        skip = make_record(root, "skip", "EditMode", 0, [("Project.Tests.EditMode.I.K", "Skipped", None), ("Project.Tests.EditMode.I.L", "Passed", None)])
        r = run([skip], known)
        check("CLS", "C19 skipped tests listed as not executed", r["records"][0]["not_executed"], str(r["records"][0]))
        md = classify.to_markdown(run([e_k1, p_c0], known))
        check("CLS", "C20 Chinese messages survive into the report", "腳落在看得到的踏面" in md and "陽性對照失敗" in md)
        for label, bad in (("missing approvedBy", [dict(k1, approvedBy="")]), ("empty symptoms", [dict(k1, symptoms=[])]),
                           ("blank symptom", [dict(k1, symptoms=["  "])]), ("duplicate id", [k1, dict(c0, id="K-1")]),
                           ("duplicate test", [k1, dict(k1, id="K-9")]), ("bad mode", [dict(k1, mode="All")]),
                           ("partial name", [dict(k1, fullName="Scene_InstanceMasks")]),
                           ("missing approvalRef", [dict(k1, approvalRef="")]), ("empty source", [dict(k1, source=[])]),
                           ("no reinvestigation condition", [dict(k1, reinvestigateWhen=[])]),
                           ("source not a list", [dict(k1, source="record")])):
            p = known_list(root, bad, "bad-%s.json" % label.replace(" ", "-"))
            try:
                classify.load_known(p)
                ok = False
            except classify.KnownListError:
                ok = True
            check("CLS", "C21 invalid list (%s) is refused" % label, ok)
        with contextlib.redirect_stdout(io.StringIO()), contextlib.redirect_stderr(io.StringIO()):
            code = classify.main([e_k1, "--known", known_list(root, [dict(k1, approvedBy="")], "bad-cli.json")])
        check("CLS", "C22 CLI exits 2 on an invalid list", code == 2, str(code))
        prod = {e["id"]: e for e in classify.load_known(os.path.join(HERE, "known-failures.json"))}
        check("CLS", "C23 shipped list holds exactly the approved K-1 and K-2 (D-1)", sorted(prod) == ["K-1", "K-2"], str(sorted(prod)))
        check("CLS", "C23 K-1 name exact and symptoms match the real Unity message",
              prod.get("K-1", {}).get("fullName") == K1_NAME and classify.symptom_matches(prod["K-1"], K1_MSG))
        check("CLS", "C23 K-2 name exact and symptoms match the real Unity message",
              prod.get("K-2", {}).get("fullName") == C0_NAME and classify.symptom_matches(prod["K-2"], C0_MSG))
        check("CLS", "C23 a changed GroundLayers value is NOT matched by K-1",
              not classify.symptom_matches(prod["K-1"], K1_MSG.replace("=503", "=511")))
        v = {x["id"]: x["state"] for x in classify.verify_entries([e_k1, p_c0], known)}
        check("CLS", "C24 --verify-entries: both confirmed by baseline records", v == {"K-1": "CONFIRMED", "K-2": "CONFIRMED"}, str(v))
        v = {x["id"]: x["state"] for x in classify.verify_entries([e_k1, p_c0], k1b)}
        check("CLS", "C24 --verify-entries: wrong symptom -> SYMPTOM_MISMATCH", v == {"K-1": "SYMPTOM_MISMATCH"}, str(v))
        v = {x["id"]: x["state"] for x in classify.verify_entries([p_ok], known)}
        check("CLS", "C24 --verify-entries: absent from the records -> NOT_OBSERVED",
              v == {"K-1": "NOT_OBSERVED", "K-2": "NOT_OBSERVED"}, str(v))
    finally:
        shutil.rmtree(root, ignore_errors=True)


# =====================================================================================================================
# SHD: shadow comparison
# =====================================================================================================================

def suite_shd():
    repo = temp_repo()
    root = tempfile.mkdtemp(prefix="orch-shd-")
    try:
        head = gitc(repo, "rev-parse", "HEAD")
        state = snap_mod.worktree_state(repo)
        map_hash = lib.sha256_file(MAP)

        def plan(invocations, tier="R2"):
            return {"tier": tier, "invocations": invocations, "preState": state, "mapSha256": map_hash, "context": "dev",
                    "snapshot": {"status": "invalid", "reason": "SNAPSHOT_MISSING"},
                    "changes": [{"path": "Assets/Scripts/Core/Combat/EnemyLeash.cs", "status": "M", "tier": "R2",
                                 "rule": "MODULE", "detail": "Core.Combat", "fixtures": ["EnemyChasePlayModeTests"]}],
                    "reasons": [{"tier": "R2", "rule": "MODULE", "path": "x", "detail": "y"}],
                    "playMode": {"selection": "filters", "selectedFixtures": ["EnemyChasePlayModeTests"],
                                 "filters": ["Project.Tests.PlayMode.EnemyChasePlayModeTests"], "matchedFixtures": [],
                                 "notRun": [{"fixture": "StairTraversalPlayModeTests", "reason": "R2"}],
                                 "estimateSeconds": 62.0, "fullEstimateSeconds": 306.0}}

        r2 = plan([{"mode": "EditMode", "filter": "", "kind": "tier"},
                   {"mode": "PlayMode", "filter": "Project.Tests.PlayMode.EnemyChasePlayModeTests", "kind": "tier"}])
        r1 = plan([{"mode": "EditMode", "filter": "", "kind": "tier"}], tier="R1")

        def run(p, edit, play, prev_failures=None, tag="x"):
            pp = os.path.join(root, "plan-%s.json" % tag)
            with open(pp, "w", encoding="utf-8") as f:
                json.dump(p, f)
            sp = os.path.join(root, "snap-%s.json" % tag)
            if prev_failures is not None:
                with open(sp, "w", encoding="utf-8") as f:
                    json.dump({"failures": prev_failures}, f)
            logp = os.path.join(root, "shadow-%s.tsv" % tag)
            out = io.StringIO()
            with contextlib.redirect_stdout(out), contextlib.redirect_stderr(io.StringIO()):
                code = shadow.main(["--plan", pp, "--edit-record", edit, "--play-record", play, "--repo", repo,
                                    "--snapshot", sp, "--log", logp, "--known", known_list(root, [], "empty-%s.json" % tag),
                                    "--samples", os.path.join(root, "samples-%s" % tag)])
            return code, out.getvalue(), sp, logp

        def sample_of(tag):
            d = os.path.join(root, "samples-%s" % tag)
            names = os.listdir(d) if os.path.isdir(d) else []
            return lib.load_json(os.path.join(d, names[0], "sample.json")) if len(names) == 1 else None

        short = head[:7]
        e_ok = make_record(root, "e-ok", "EditMode", 0, [("Project.Tests.EditMode.A.B", "Passed", None)], head=short)
        stair = "Project.Tests.PlayMode.StairTraversalPlayModeTests.Climb"
        p_escape = make_record(root, "p-esc", "PlayMode", 1, [(stair, "Failed", "boom"),
                                                              ("Project.Tests.PlayMode.EnemyChasePlayModeTests.Run", "Passed", None)], head=short)
        code, out, sp, logp = run(r2, e_ok, p_escape, tag="s1")
        check("SHD", "S1 new failure in an unselected fixture -> ESCAPE (exit 1)", code == 1 and "ESCAPE" in out, out)
        smp = sample_of("s1")
        required = ("changeSet", "plannedScope", "selectedTests", "fullResults", "escapes", "snapshot", "savings")
        check("SHD", "S1 sample record: change set, planned scope, selected tests, full results, escapes, snapshot validity, savings",
              smp is not None and all(k in smp for k in required) and smp["changeSet"]["changes"]
              and smp["selectedTests"]["filters"] and smp["fullResults"]["records"] and smp["escapes"]
              and smp["snapshot"]["written"] is True and smp["snapshot"]["atPlan"]["status"] == "invalid", str(smp)[:300])
        check("SHD", "S1 escape is recorded as PENDING investigation", smp and smp["escapes"][0]["investigation"] == "PENDING")
        meas = (smp or {}).get("savings", {}).get("measured") or {}
        check("SHD", "S1 measured savings from real durations: full 60+1.0 s vs planned 60+0.1 s = 0.9 s",
              meas.get("fullSeconds") == 61.0 and meas.get("plannedSeconds") == 60.1 and meas.get("savedSeconds") == 0.9, str(meas))
        check("SHD", "S1 estimated savings from the plan's cost model = 306-62 = 244 s",
              smp and smp["savings"]["estimated"]["savedSeconds"] == 244.0, str((smp or {}).get("savings")))
        check("SHD", "S1 valid sample writes the snapshot with the failure fingerprint",
              os.path.isfile(sp) and any(stair in f for f in lib.load_json(sp)["failures"]), out)
        lines = open(logp, encoding="utf-8").read().splitlines() if os.path.isfile(logp) else []
        check("SHD", "S1 shadow.tsv row appended, same column count as the header",
              len(lines) == 2 and len(lines[0].split("	")) == len(lines[1].split("	")), str(lines))
        check("SHD", "S1 sample planned without a valid snapshot does not count toward Q5",
              len(lines) == 2 and lines[1].split("	")[2] == "no", str(lines[1:]))
        code, out, _, _ = run(r2, e_ok, p_escape, prev_failures=[shadow.fingerprint("PlayMode", stair, "boom")], tag="s2")
        check("SHD", "S2 same failure already in the previous full run -> not an escape", code == 0, out)
        code, out, _, _ = run(r2, e_ok, p_escape, prev_failures=[shadow.fingerprint("PlayMode", stair, "other")], tag="s3")
        check("SHD", "S3 same test, different message -> ESCAPE_SYMPTOM", code == 1 and "ESCAPE_SYMPTOM" in out, out)
        p_sel = make_record(root, "p-sel", "PlayMode", 1, [("Project.Tests.PlayMode.EnemyChasePlayModeTests.Run", "Failed", "x")], head=short)
        counted = dict(r2, snapshot={"status": "valid"})
        code, out, _, logp = run(counted, e_ok, p_sel, tag="s4b")
        row = open(logp, encoding="utf-8").read().splitlines()[1].split("	")
        check("SHD", "S4b valid sample against a valid snapshot counts toward Q5", row[2] == "yes", str(row))
        code, out, _, _ = run(r2, e_ok, p_sel, tag="s4")
        check("SHD", "S4 failure inside the selection -> not an escape", code == 0, out)
        p_other_head = make_record(root, "p-head", "PlayMode", 0, [("Project.Tests.PlayMode.A.B", "Passed", None)], head="0000000")
        code, out, sp, _ = run(r2, e_ok, p_other_head, tag="s5")
        check("SHD", "S5 record from another HEAD -> invalid sample (exit 3), no snapshot", code == 3 and not os.path.isfile(sp), out)
        smp = sample_of("s5")
        check("SHD", "S5 invalid sample is still recorded, with its reason and snapshot not written",
              smp is not None and not smp["valid"] and smp["invalidReason"] and not smp["snapshot"]["written"]
              and not smp["countsTowardQ5"], str(smp)[:300])
        p_filtered = make_record(root, "p-filt", "PlayMode", 0, [("Project.Tests.PlayMode.A.B", "Passed", None)], head=short, filt="A")
        code, out, _, _ = run(r2, e_ok, p_filtered, tag="s6")
        check("SHD", "S6 filtered record -> invalid sample", code == 3, out)
        e_blocked = make_record(root, "e-blk", "EditMode", 15, None, head=short)
        code, out, _, _ = run(r2, e_blocked, p_escape, tag="s7")
        check("SHD", "S7 blocked EditMode record -> invalid sample", code == 3, out)
        e_failed = make_record(root, "e-fail", "EditMode", 1, [("Project.Tests.EditMode.A.B", "Failed", "e")], head=short)
        code, out, _, _ = run(r1, e_failed, p_escape, tag="s8")
        check("SHD", "S8 R1 plan: every PlayMode failure is unselected -> ESCAPE; EditMode failure is not",
              code == 1 and out.count("ESCAPE ") == 1, out)
        write(repo, "docs/a.md", "changed during the run\n")
        code, out, sp, _ = run(r2, e_ok, p_escape, tag="s9")
        check("SHD", "S9 work tree changed after planning -> invalid sample, no snapshot", code == 3 and not os.path.isfile(sp), out)
    finally:
        shutil.rmtree(repo, ignore_errors=True)
        shutil.rmtree(root, ignore_errors=True)


# =====================================================================================================================
# RPT: shadow report
# =====================================================================================================================

def suite_rpt():
    root = tempfile.mkdtemp(prefix="orch-rpt-")
    try:
        def sample(sid, valid=True, counts=True, tier="R2", saved_est=200.0, saved_meas=150.0, escapes=(), r3_rule="E2"):
            d = os.path.join(root, "samples", sid)
            os.makedirs(d)
            s = {"id": sid, "valid": valid, "invalidReason": None if valid else "work tree changed", "countsTowardQ5": counts,
                 "plannedScope": {"tier": tier, "reasons": [{"tier": tier, "rule": r3_rule if tier == "R3" else "MODULE",
                                                             "path": "p", "detail": "d"}]},
                 "savings": {"estimated": {"savedSeconds": saved_est},
                             "measured": {"savedSeconds": saved_meas, "fullSeconds": 300.0}},
                 "escapes": [dict(e) for e in escapes]}
            with open(os.path.join(d, "sample.json"), "w", encoding="utf-8") as f:
                json.dump(s, f)
        esc = {"kind": "ESCAPE", "mode": "PlayMode", "fullName": "Project.Tests.PlayMode.X.Y", "message": "m"}
        sample("01-first", counts=False, tier="R3", saved_est=0.0, saved_meas=0.0)
        sample("02-r3", tier="R3", saved_est=0.0, saved_meas=0.0, r3_rule="E7")
        sample("03-r3", tier="R3", saved_est=0.0, saved_meas=0.0, r3_rule="E7")
        sample("04-r2", escapes=[esc])
        sample("05-bad", valid=False, counts=False)
        inv_path = os.path.join(root, "inv.json")
        with open(inv_path, "w", encoding="utf-8") as f:
            json.dump({"schema": 1, "entries": []}, f)
        r = sreport.build(sreport.load_samples(os.path.join(root, "samples")), sreport.load_investigations(inv_path))
        check("RPT", "R1 counts only valid samples with a trustworthy snapshot", r["counting"] == 3 and r["valid"] == 4, str(r))
        check("RPT", "R2 R3 share and reasons reported (benefit, not just escapes)",
              abs(r["r3Share"] - 2 / 3) < 1e-9 and r["r3Rules"] == {"E7": 2}, str(r))
        check("RPT", "R3 measured savings and share of full PlayMode time",
              r["measuredSaved"]["total"] == 150.0 and r["measuredSaved"]["shareOfFullPlayTime"] == round(150 / 900, 3), str(r))
        check("RPT", "R4 uninvestigated escape listed as pending", len(r["pendingInvestigations"]) == 1, str(r))
        md = sreport.to_markdown(r)
        check("RPT", "R5 report says Phase 3 is not enabled and flags mostly-R3",
              "NOT enabled" in md and "most samples were forced to R3" in md, md)
        with open(inv_path, "w", encoding="utf-8") as f:
            json.dump({"schema": 1, "entries": [{"sample": "04-r2", "mode": "PlayMode", "fullName": "Project.Tests.PlayMode.X.Y",
                                                 "category": "mapping-gap", "evidence": "fixture X exercises module Z",
                                                 "action": "add X to module Z", "decidedOn": "2026-10-14"}]}, f)
        r = sreport.build(sreport.load_samples(os.path.join(root, "samples")), sreport.load_investigations(inv_path))
        check("RPT", "R6 investigated escape is no longer pending",
              not r["pendingInvestigations"] and r["escapes"][0]["investigation"] == "mapping-gap")
        for label, bad in (("unknown category", {"category": "widened-rule"}), ("no evidence", {"evidence": ""})):
            e = {"sample": "04-r2", "mode": "PlayMode", "fullName": "X", "category": "flaky-test", "evidence": "e",
                 "action": "a", "decidedOn": "d"}
            e.update(bad)
            with open(inv_path, "w", encoding="utf-8") as f:
                json.dump({"schema": 1, "entries": [e]}, f)
            try:
                sreport.load_investigations(inv_path)
                ok = False
            except sreport.InvestigationError:
                ok = True
            check("RPT", "R7 invalid investigation (%s) is refused" % label, ok)
        check("RPT", "R8 shipped escape-investigations.json is valid",
              sreport.load_investigations(os.path.join(HERE, "escape-investigations.json")) == [])
    finally:
        shutil.rmtree(root, ignore_errors=True)


# =====================================================================================================================
# MIR: skill mirror
# =====================================================================================================================

def suite_mir():
    root = tempfile.mkdtemp(prefix="orch-mir-")
    try:
        c, a = os.path.join(root, "c"), os.path.join(root, "a")
        write(c, "s1/SKILL.md", "see CLAUDE.md for rules\nline\n")
        write(a, "s1/SKILL.md", "see AGENTS.md for rules\nline\n")
        check("MIR", "M1 only the CLAUDE.md/AGENTS.md name differs -> in sync", mirror.compare(c, a) == [])
        write(a, "s1/SKILL.md", "see AGENTS.md for rules\nline edited\n")
        check("MIR", "M2 any other edit -> drift", len(mirror.compare(c, a)) == 1)
        write(a, "s1/SKILL.md", "see AGENTS.md for rules\nline\n")
        write(c, "s2/SKILL.md", "new skill\n")
        check("MIR", "M3 skill missing from one side -> drift", any("only in" in p for p in mirror.compare(c, a)))
        write(a, "s2/SKILL.md", "new skill\n")
        write(a, "s2/extra.md", "x\n")
        check("MIR", "M4 extra file on one side -> drift", any("extra.md" in p for p in mirror.compare(c, a)))
        live = mirror.compare(os.path.join(REPO, ".claude", "skills"), os.path.join(REPO, ".agents", "skills"))
        check("MIR", "M5 the repo's real skills are in sync", live == [], "; ".join(live))
    finally:
        shutil.rmtree(root, ignore_errors=True)


def main():
    lib.utf8_stdout()
    for suite in (suite_map, suite_plan, suite_det, suite_git, suite_cls, suite_shd, suite_rpt, suite_mir):
        try:
            suite()
        except Exception as e:  # a crashing suite is a failure, never a silent skip
            check(suite.__name__[6:].upper(), "suite crashed", False, "%s: %s" % (e.__class__.__name__, e))
    total = failed = 0
    for suite, cases in RESULTS.items():
        f = sum(1 for _, ok, _ in cases if not ok)
        total += len(cases)
        failed += f
        print("%-4s %d/%d PASS" % (suite, len(cases) - f, len(cases)))
    print("selftest: %s (%d/%d)" % ("PASS" if failed == 0 else "FAIL", total - failed, total))
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
