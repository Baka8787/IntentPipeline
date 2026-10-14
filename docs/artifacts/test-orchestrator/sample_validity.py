"""Shadow Sample Validity (docs/57 §14.9): did the full run test exactly the state the plan was made for?

Kept apart from Snapshot Validity (was the trusted baseline usable at plan time / may it advance now) — the two answer
different questions and are recorded separately in sample.json, sample.md, shadow.tsv and the report.

A sample is valid only when EVERY check below holds (anything that cannot be proven is invalid):
  records   both safe-test records are complete, unfiltered full runs of their mode;
  head      HEAD is the planned HEAD, or a strict fast-forward of it (`git merge-base --is-ancestor`) in which every
            intervening commit is a non-merge commit whose changed paths ALL match `sampleValidity.docOnlyAllow`
            and none matches `sampleValidity.docOnlyDeny` (deny wins over allow; a path is never judged by its
            extension alone — the allowlist names exact places);
  recordHead each record ran at the planned HEAD or at one of those intervening doc-only commits;
  worktree  the uncommitted state (every dirty / untracked path -> blob) is identical to the plan-time state;
  inputs    the files the plan and the run depend on (`sampleValidity.planInputs` + WriterRules source) hash exactly as
            recorded in plan.json; a plan without recorded input hashes may only be valid with an unchanged HEAD;
  map       test-map.json is the one the plan was made with.

The tested state — what a snapshot may record — is always the PLANNED state (plan HEAD + plan-time work tree).
"""
import hashlib
import os
import sys

sys.dont_write_bytecode = True   # never leave __pycache__ in docs/ (it would itself be a change)
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import orchestrator_lib as lib  # noqa: E402

HEAD_UNCHANGED = "unchanged"
HEAD_DOC_ONLY_FF = "doc-only-fast-forward"


class PolicyError(Exception):
    pass


def policy(tmap):
    sv = tmap.get("sampleValidity")
    if not isinstance(sv, dict):
        raise PolicyError("test-map.json has no sampleValidity section")
    for k in ("docOnlyAllow", "docOnlyDeny", "planInputs"):
        if not isinstance(sv.get(k), list) or not sv[k] or not all(isinstance(p, str) and p for p in sv[k]):
            raise PolicyError("sampleValidity.%s must be a non-empty list of patterns" % k)
    return {"allow": list(sv["docOnlyAllow"]), "deny": list(sv["docOnlyDeny"]),
            "inputs": list(sv["planInputs"]) + [tmap["writerRulesSource"]]}


def classify_path(path, pol):
    """(True, allow-pattern) or (False, why). The deny list is consulted FIRST: a path both lists match is denied."""
    d = lib.first_match(path, pol["deny"])
    if d:
        return False, "denied by %s" % d
    a = lib.first_match(path, pol["allow"])
    if not a:
        return False, "not in the doc-only allowlist"
    return True, a


# ---------------------------------------------------------------------------------------------------------------------
# plan inputs
# ---------------------------------------------------------------------------------------------------------------------

def _static_prefix(pattern):
    """Directory part of a glob before its first wildcard ('' = repo root)."""
    if pattern.startswith("re:"):
        return None
    head = []
    for part in pattern.split("/")[:-1]:
        if any(c in part for c in "*?"):
            break
        head.append(part)
    return "/".join(head)


def input_hashes(repo, pol):
    """sha256 of every existing file matching a plan-input pattern (raw bytes, as on disk). Sorted dict."""
    out = {}
    for pattern in pol["inputs"]:
        prefix = _static_prefix(pattern)
        if prefix is None:
            raise PolicyError("planInputs must be globs, not regexes: %s" % pattern)
        if not any(c in pattern for c in "*?"):          # a literal file
            if os.path.isfile(os.path.join(repo, pattern)):
                out[pattern] = _sha256(os.path.join(repo, pattern))
            continue
        top = os.path.join(repo, prefix)
        for root, dirs, files in os.walk(top):
            dirs[:] = [d for d in dirs if d != "__pycache__"]
            for f in files:
                rel = lib.norm_path(os.path.relpath(os.path.join(root, f), repo))
                if lib.matches(rel, pattern):
                    out[rel] = _sha256(os.path.join(root, f))
    return dict(sorted(out.items()))


def _sha256(path):
    h = hashlib.sha256()
    with open(path, "rb") as f:
        h.update(f.read())
    return h.hexdigest()


# ---------------------------------------------------------------------------------------------------------------------
# HEAD progress
# ---------------------------------------------------------------------------------------------------------------------

def head_progress(repo, plan_head, current_head, pol):
    """Is current_head the planned HEAD or a doc-only fast-forward of it?
    Returns (ok, policy-or-None, reason-or-None, commits[{commit, paths, rejected}])."""
    if current_head == plan_head:
        return True, HEAD_UNCHANGED, None, []
    if lib.git(repo, "merge-base", "--is-ancestor", plan_head, current_head, check=False).returncode != 0:
        return False, None, "HEAD %s is not a fast-forward of the planned HEAD %s" % (current_head[:12], plan_head[:12]), []
    merges = lib.git_text(repo, "rev-list", "--min-parents=2", "%s..%s" % (plan_head, current_head)).split()
    if merges:
        return False, None, "merge commit %s between plan and comparison" % merges[0][:12], []
    commits, first_bad = [], None
    for c in lib.git_text(repo, "rev-list", "--reverse", "%s..%s" % (plan_head, current_head)).split():
        raw = lib.git(repo, "diff-tree", "-r", "--no-commit-id", "--name-only", "-z", "--no-renames", c).stdout
        paths = sorted({lib.norm_path(p) for p in raw.decode("utf-8", "replace").split("\0") if p})
        rejected = []
        for p in paths:
            ok, why = classify_path(p, pol)
            if not ok:
                rejected.append({"path": p, "why": why})
        commits.append({"commit": c, "paths": paths, "rejected": rejected})
        if rejected and first_bad is None:
            first_bad = "commit %s changes %s (%s)" % (c[:12], rejected[0]["path"], rejected[0]["why"])
        if not paths and first_bad is None:
            first_bad = "commit %s has no changed paths to prove safe" % c[:12]
    if first_bad:
        return False, None, first_bad, commits
    return True, HEAD_DOC_ONLY_FF, None, commits


# ---------------------------------------------------------------------------------------------------------------------
# the verdict
# ---------------------------------------------------------------------------------------------------------------------

def validate(plan, edit, play, repo, current_state, tmap, map_hash):
    """Shadow Sample Validity. Every check is evaluated and recorded; the sample is valid only if all pass.
    `reason` is the first failing check (records first: without complete records nothing else matters)."""
    checks = []

    def add(name, ok, detail=None):
        checks.append({"check": name, "ok": bool(ok), "detail": detail})

    pre = plan.get("preState") or {}
    plan_head = pre.get("head")
    out = {"valid": False, "reason": None, "headPolicy": None, "planHead": plan_head,
           "currentHead": current_state["head"], "interveningCommits": [], "checks": checks}
    if not plan_head:
        add("plan", False, "plan has no preState (synthetic plan?)")
        out["reason"] = "plan has no preState (synthetic plan?)"
        return out
    try:
        pol = policy(tmap)
    except (PolicyError, KeyError) as e:
        add("policy", False, str(e))
        out["reason"] = "sample-validity policy unreadable: %s" % e
        return out

    rec_problem = None
    for rec, mode in ((edit, "EditMode"), (play, "PlayMode")):
        if rec.mode != mode:
            rec_problem = rec_problem or "%s record is %s" % (mode, rec.mode)
        elif not rec.is_full_run:
            rec_problem = rec_problem or "%s record is not a full run (filter=%r explicit=%s check_only=%s)" % (
                mode, rec.filter, rec.include_explicit, rec.check_only)
        elif rec.exit_code not in (0, 1) or rec.results is None:
            rec_problem = rec_problem or "%s record did not complete (exit %s, %s)" % (
                mode, rec.exit_code, rec.status_error or rec.verdict)
    add("records", rec_problem is None, rec_problem)

    ok, head_policy, why, commits = head_progress(repo, plan_head, current_state["head"], pol)
    out["headPolicy"], out["interveningCommits"] = head_policy, commits
    add("head", ok, why)

    tested_heads = [plan_head] + [c["commit"] for c in commits] if ok else [plan_head]
    bad = [(m, r.git_head) for r, m in ((edit, "EditMode"), (play, "PlayMode"))
           if not r.git_head or not any(h.startswith(r.git_head) for h in tested_heads)]
    add("recordHead", not bad, "; ".join("%s record ran at %s, plan was made at %s" % (m, g or "?", plan_head[:12])
                                          for m, g in bad) or None)

    drift = sorted(p for p in set(pre.get("worktree", {})) | set(current_state["worktree"])
                   if pre.get("worktree", {}).get(p) != current_state["worktree"].get(p))
    add("worktree", not drift, ("uncommitted state differs from plan time: " + ", ".join(drift[:10])) if drift else None)

    recorded = plan.get("inputHashes")
    if recorded is None:
        add("inputs", head_policy == HEAD_UNCHANGED,
            None if head_policy == HEAD_UNCHANGED else "plan has no inputHashes: only an unchanged HEAD can be proven safe")
    else:
        now = input_hashes(repo, pol)
        changed = sorted(p for p in set(recorded) | set(now) if recorded.get(p) != now.get(p))
        add("inputs", not changed, ("plan inputs changed: " + ", ".join(changed[:10])) if changed else None)

    add("map", plan.get("mapSha256") == map_hash, None if plan.get("mapSha256") == map_hash
        else "test-map.json changed after planning")

    failed = [c for c in checks if not c["ok"]]
    out["valid"] = not failed
    out["reason"] = None if not failed else "%s: %s" % (failed[0]["check"], failed[0]["detail"])
    return out
