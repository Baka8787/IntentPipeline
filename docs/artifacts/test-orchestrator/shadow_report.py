"""Shadow-period report (docs/57 §14): what the selector would actually have bought, and what it would have missed.

    python -I docs/artifacts/test-orchestrator/shadow_report.py [--samples Logs/test-orchestrator/shadow]
           [--investigations docs/artifacts/test-orchestrator/escape-investigations.json] [--markdown OUT.md]

Reports — never decides. Phase 3 needs a separate user decision (§14, supplement 4) whatever these numbers say.
- Q5 progress: valid samples planned against a trustworthy snapshot (first valid sample only creates the snapshot).
  Shadow Sample Validity and Snapshot Validity are reported separately (§14.9); Q5 reads both, never "advanced".
- Benefit, not just "no escapes": tier distribution, how often R3 was forced and by which rules, estimated and
  measured PlayMode seconds the plan would have saved.
- Every ESCAPE must have an investigation entry (mapping gap / rule gap / flaky test) with evidence and action;
  uninvestigated escapes are listed (D-2: widening rules just to zero the count is not an investigation).
Exit: 0 report written · 1 investigation file invalid · 2 usage.
"""
import argparse
import os
import statistics
import sys

sys.dont_write_bytecode = True
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import orchestrator_lib as lib  # noqa: E402

CATEGORIES = ("mapping-gap", "rule-gap", "flaky-test")
Q5_TARGET = 10


class InvestigationError(Exception):
    pass


def load_investigations(path):
    if not os.path.isfile(path):
        return []
    data = lib.load_json(path)
    if data.get("schema") != 1 or not isinstance(data.get("entries"), list):
        raise InvestigationError("expected {\"schema\": 1, \"entries\": [...]}")
    for i, e in enumerate(data["entries"]):
        for k in ("sample", "mode", "fullName", "category", "evidence", "action", "decidedOn"):
            if not isinstance(e.get(k), str) or not e[k].strip():
                raise InvestigationError("entry %d: '%s' is required" % (i, k))
        if e["category"] not in CATEGORIES:
            raise InvestigationError("entry %d: category must be one of %s" % (i, ", ".join(CATEGORIES)))
    return data["entries"]


def load_samples(root):
    out = []
    if not os.path.isdir(root):
        return out
    for name in sorted(os.listdir(root)):
        p = os.path.join(root, name, "sample.json")
        if os.path.isfile(p):
            try:
                out.append(lib.load_json(p))
            except (OSError, ValueError):
                out.append({"id": name, "unreadable": True})
    return out


def sample_validity(s):
    """(valid, reason) — Shadow Sample Validity. Schema 1 (before §14.9) stored it as top-level valid/invalidReason."""
    if isinstance(s.get("sampleValidity"), dict):
        return bool(s["sampleValidity"].get("valid")), s["sampleValidity"].get("reason")
    return bool(s.get("valid")), s.get("invalidReason")


def snapshot_validity(s):
    """(status at plan time, advanced) — Snapshot Validity, kept apart from sample validity."""
    if isinstance(s.get("snapshotValidity"), dict):
        return (s["snapshotValidity"].get("atPlan") or {}).get("status"), bool(s["snapshotValidity"].get("advanced"))
    old = s.get("snapshot") or {}
    return (old.get("atPlan") or {}).get("status"), bool(old.get("written"))


def build(samples, investigations):
    inv_by = {(e["sample"], e["mode"], e["fullName"]): e for e in investigations}
    readable = [s for s in samples if not s.get("unreadable")]
    valid = [s for s in readable if sample_validity(s)[0]]
    # Q5 is decided from the two validities themselves (never from a stored flag, never from whether the snapshot
    # advanced): a valid sample planned against a trustworthy snapshot.
    counting = [s for s in valid if snapshot_validity(s)[0] == "valid"]
    tiers, r3_rules = {}, {}
    for s in counting:
        t = s["plannedScope"]["tier"]
        tiers[t] = tiers.get(t, 0) + 1
        if t == "R3":
            for r in s["plannedScope"]["reasons"]:
                if r["tier"] == "R3":
                    r3_rules[r["rule"]] = r3_rules.get(r["rule"], 0) + 1
    est = [s["savings"]["estimated"]["savedSeconds"] for s in counting]
    meas = [s["savings"]["measured"]["savedSeconds"] for s in counting if s["savings"].get("measured")]
    full = [s["savings"]["measured"]["fullSeconds"] for s in counting if s["savings"].get("measured")]
    escapes, pending = [], []
    for s in valid:
        for e in s.get("escapes", []):
            inv = inv_by.get((s["id"], e["mode"], e["fullName"]))
            item = dict(e, sample=s["id"], investigation=inv["category"] if inv else "PENDING")
            escapes.append(item)
            if not inv:
                pending.append(item)
    return {
        "samples": len(samples), "unreadable": len(samples) - len(readable), "valid": len(valid),
        "invalid": [(s["id"], sample_validity(s)[1]) for s in readable if not sample_validity(s)[0]],
        "validNoTrustedSnapshot": len(valid) - len(counting),
        "snapshotAdvanced": sum(1 for s in readable if snapshot_validity(s)[1]),
        "docOnlyFastForward": sum(1 for s in valid if (s.get("sampleValidity") or {}).get("headPolicy") == "doc-only-fast-forward"),
        "counting": len(counting), "q5Target": Q5_TARGET, "tiers": tiers, "r3Rules": r3_rules,
        "r3Share": (tiers.get("R3", 0) / len(counting)) if counting else None,
        "estimatedSaved": {"total": round(sum(est), 1), "median": round(statistics.median(est), 1) if est else None},
        "measuredSaved": {"total": round(sum(meas), 1), "median": round(statistics.median(meas), 1) if meas else None,
                          "shareOfFullPlayTime": round(sum(meas) / sum(full), 3) if full and sum(full) else None},
        "samplesThatSaved": sum(1 for x in meas if x > 0),
        "escapes": escapes, "pendingInvestigations": pending,
    }


def to_markdown(r):
    L = ["# Shadow period report", "",
         "**Phase 3 is NOT enabled by this report** — it needs a separate user decision (docs/57 §14).", "",
         "## Q5 progress",
         "- counting toward Q5 (sample valid AND planned against a trustworthy snapshot): **%d / %d**" % (r["counting"], r["q5Target"]),
         "- synthetic coverage: fixtures/planner-cases.json, run selftest.py (required alongside, not instead)",
         "", "## Shadow Sample Validity",
         "- samples recorded: %d · valid %d (of which HEAD advanced by doc-only commits: %d) · invalid %d · unreadable %d"
         % (r["samples"], r["valid"], r["docOnlyFastForward"], len(r["invalid"]), r["unreadable"])]
    for sid, why in r["invalid"]:
        L.append("- invalid sample %s — %s" % (sid, why))
    L += ["", "## Snapshot Validity",
          "- valid samples planned WITHOUT a trustworthy snapshot (do not count toward Q5): %d" % r["validNoTrustedSnapshot"],
          "- samples that advanced the trusted snapshot: %d" % r["snapshotAdvanced"]]
    L += ["", "## Benefit (counting samples)"]
    if not r["counting"]:
        L.append("- no counting samples yet — nothing can be said about the benefit")
    else:
        L.append("- planned tiers: " + ", ".join("%s %d" % kv for kv in sorted(r["tiers"].items())))
        L.append("- R3 share: **%.0f%%**" % (100 * r["r3Share"]))
        if r["r3Rules"]:
            L.append("- why R3: " + ", ".join("%s ×%d" % kv for kv in sorted(r["r3Rules"].items(), key=lambda kv: -kv[1])))
        L.append("- estimated PlayMode seconds saved: total %s, median %s" % (r["estimatedSaved"]["total"], r["estimatedSaved"]["median"]))
        L.append("- measured PlayMode seconds saved: total %s, median %s, share of full PlayMode time %s; samples that saved anything: %d"
                 % (r["measuredSaved"]["total"], r["measuredSaved"]["median"], r["measuredSaved"]["shareOfFullPlayTime"], r["samplesThatSaved"]))
        if r["r3Share"] is not None and r["r3Share"] > 0.5:
            L.append("- ⚠ most samples were forced to R3: the selector's practical benefit is small even if it never escapes")
    L += ["", "## Escapes (%d, %d not investigated)" % (len(r["escapes"]), len(r["pendingInvestigations"]))]
    L += ["- %s %s %s `%s` — investigation: %s" % (e["sample"], e["kind"], e["mode"], e["fullName"], e["investigation"])
          for e in r["escapes"]] or ["- none"]
    return "\n".join(L) + "\n"


def main(argv):
    here = os.path.dirname(os.path.abspath(__file__))
    repo = os.path.abspath(os.path.join(here, "..", "..", ".."))
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--samples", default=os.path.join(repo, "Logs", "test-orchestrator", "shadow"))
    ap.add_argument("--investigations", default=os.path.join(here, "escape-investigations.json"))
    ap.add_argument("--markdown")
    a = ap.parse_args(argv)
    lib.utf8_stdout()
    try:
        inv = load_investigations(a.investigations)
    except (InvestigationError, OSError, ValueError) as e:
        print("INVESTIGATIONS_INVALID: %s" % e, file=sys.stderr)
        return 1
    md = to_markdown(build(load_samples(a.samples), inv))
    if a.markdown:
        with open(a.markdown, "w", encoding="utf-8") as f:
            f.write(md)
    print(md, end="")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
