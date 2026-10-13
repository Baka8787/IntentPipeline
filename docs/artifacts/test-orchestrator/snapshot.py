"""Snapshot of the last fully verified state (docs/57 §12.3) and the content-based change set since it.

Definitions
- worktree state: HEAD (full SHA) + every dirty / untracked path (`git status`, ignored files excluded) -> blob hash of
  its current content (`git hash-object`, same newline normalisation as git) or "ABSENT".
- content of P at snapshot time: snapshot.worktree[P] if P was dirty then, else the blob of baseCommit:P, else ABSENT.
- content of P now: blob hash of the work-tree file, else ABSENT.
- changed: the two differ. Paths examined = (baseCommit..HEAD) ∪ dirty now ∪ dirty at snapshot time.
  => long-lived, unchanged WIP (e.g. an uncommitted scene) is not a change, and committing it unchanged is not either.

The snapshot is only written by shadow_compare.py after a complete full EditMode + PlayMode run whose work-tree state
was identical before and after the run. Writing uses `git hash-object -w` for dirty .cs/.asmdef files so that the
next plan can diff their old content (loose objects only: no ref, index or work-tree change).
"""
import datetime
import os
import sys

sys.dont_write_bytecode = True   # never leave __pycache__ in docs/ (it would itself be a change)
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import orchestrator_lib as lib  # noqa: E402

ABSENT = "ABSENT"
SCHEMA = 1
TEXT_FOR_RULES = (".cs", ".asmdef")


def _status_paths(repo):
    """Every dirty or untracked path (renames contribute both names). Ignored files are excluded by git."""
    raw = lib.git(repo, "status", "--porcelain=v1", "-z", "--untracked-files=all").stdout.decode("utf-8", "replace")
    parts = raw.split("\0")
    paths, i = set(), 0
    while i < len(parts):
        entry = parts[i]
        i += 1
        if len(entry) < 4:
            continue
        xy, path = entry[:2], entry[3:]
        paths.add(lib.norm_path(path))
        if "R" in xy or "C" in xy:          # -z: the source path follows as its own field
            if i < len(parts) and parts[i]:
                paths.add(lib.norm_path(parts[i]))
            i += 1
    return paths


def hash_files(repo, paths, write=False):
    """Blob hashes of existing work-tree files (ABSENT for missing ones), in one git call."""
    out = {}
    existing = sorted(p for p in paths if os.path.isfile(os.path.join(repo, p)))
    for p in paths:
        if p not in existing:
            out[p] = ABSENT
    if existing:
        args = ["hash-object", "--stdin-paths"] + (["-w"] if write else [])
        res = lib.git(repo, *args, input_bytes=("\n".join(existing) + "\n").encode("utf-8"))
        hashes = res.stdout.decode().split()
        if len(hashes) != len(existing):
            raise lib.GitError("hash-object returned %d hashes for %d paths" % (len(hashes), len(existing)))
        out.update(zip(existing, hashes))
    return out


def worktree_state(repo, write_text_blobs=False):
    head = lib.git_text(repo, "rev-parse", "HEAD").strip()
    paths = _status_paths(repo)
    state = hash_files(repo, paths)
    if write_text_blobs:
        text = [p for p in paths if p.endswith(TEXT_FOR_RULES) and state[p] != ABSENT]
        if text:
            hash_files(repo, text, write=True)
    return {"head": head, "worktree": dict(sorted(state.items()))}


def write(repo, path, state, map_hash, failures, records, now=None):
    snap = {"schema": SCHEMA, "plannerVersion": lib.PLANNER_VERSION, "baseCommit": state["head"],
            "worktree": state["worktree"], "mapHash": map_hash,
            "createdUtc": (now or datetime.datetime.now(datetime.timezone.utc)).strftime("%Y-%m-%dT%H:%M:%SZ"),
            "failures": sorted(failures), "records": records}
    os.makedirs(os.path.dirname(os.path.abspath(path)), exist_ok=True)
    tmp = path + ".tmp"
    with open(tmp, "w", encoding="utf-8") as f:
        f.write(lib.dump_json(snap))
    os.replace(tmp, path)
    return snap


def load(repo, path, map_hash, max_age_days, now=None):
    """Returns (snapshot, None) when trustworthy, else (None, reason). Every reason means: plan R3."""
    if not os.path.isfile(path):
        return None, "SNAPSHOT_MISSING: no last-verified snapshot (%s)" % path
    try:
        snap = lib.load_json(path)
    except (OSError, ValueError) as e:
        return None, "SNAPSHOT_UNREADABLE: %s" % e.__class__.__name__
    if not isinstance(snap, dict) or snap.get("schema") != SCHEMA:
        return None, "SNAPSHOT_SCHEMA: unexpected schema"
    for k in ("baseCommit", "worktree", "mapHash", "createdUtc", "plannerVersion", "failures"):
        if k not in snap:
            return None, "SNAPSHOT_SCHEMA: missing '%s'" % k
    if not isinstance(snap["worktree"], dict) or not isinstance(snap["failures"], list):
        return None, "SNAPSHOT_SCHEMA: malformed worktree / failures"
    if snap["plannerVersion"] != lib.PLANNER_VERSION:
        return None, "SNAPSHOT_PLANNER_CHANGED: written by planner %s, this is %s" % (snap["plannerVersion"], lib.PLANNER_VERSION)
    if snap["mapHash"] != map_hash:
        return None, "SNAPSHOT_RULES_CHANGED: test-map.json differs from the one the snapshot was verified under"
    base = snap["baseCommit"]
    if lib.git(repo, "cat-file", "-e", base + "^{commit}", check=False).returncode != 0:
        return None, "SNAPSHOT_BASE_MISSING: base commit %s does not exist" % base[:12]
    if lib.git(repo, "merge-base", "--is-ancestor", base, "HEAD", check=False).returncode != 0:
        return None, "SNAPSHOT_BASE_NOT_ANCESTOR: %s is not an ancestor of HEAD (history rewritten or another line)" % base[:12]
    try:
        created = datetime.datetime.strptime(snap["createdUtc"], "%Y-%m-%dT%H:%M:%SZ").replace(tzinfo=datetime.timezone.utc)
    except ValueError:
        return None, "SNAPSHOT_SCHEMA: bad createdUtc"
    age = (now or datetime.datetime.now(datetime.timezone.utc)) - created
    if age.total_seconds() < -300:
        return None, "SNAPSHOT_FROM_FUTURE: createdUtc is ahead of the clock"
    if age.days >= max_age_days:
        return None, "SNAPSHOT_STALE: %d days old (limit %d; Library / package cache are not in git)" % (age.days, max_age_days)
    return snap, None


def _blob_at(repo, commit, path):
    p = lib.git(repo, "rev-parse", "--verify", "-q", "%s:%s" % (commit, path), check=False)
    return p.stdout.decode().strip() if p.returncode == 0 else ABSENT


def _blob_text(repo, blob):
    if blob == ABSENT:
        return ""
    p = lib.git(repo, "cat-file", "blob", blob, check=False)
    return p.stdout.decode("utf-8", "replace") if p.returncode == 0 else None


def _read_text(repo, path):
    try:
        with open(os.path.join(repo, path), encoding="utf-8", errors="replace") as f:
            return f.read()
    except OSError:
        return None


def change_set(repo, snap):
    """Content-based changes since the snapshot. Each change: path, status A/M/D, old/new text for rule files
    (old is None when its content can no longer be recovered -> content rules unevaluable -> R3)."""
    base = snap["baseCommit"]
    committed = lib.git(repo, "diff", "--name-only", "-z", "--no-renames", base, "HEAD").stdout.decode("utf-8", "replace")
    candidates = {lib.norm_path(p) for p in committed.split("\0") if p}
    now_state = worktree_state(repo)
    candidates |= set(now_state["worktree"]) | set(snap["worktree"])
    now_hash = hash_files(repo, candidates)
    changes = []
    for p in sorted(candidates):
        then = snap["worktree"].get(p) or _blob_at(repo, base, p)
        now = now_hash[p]
        if then == now:
            continue
        status = "A" if then == ABSENT else ("D" if now == ABSENT else "M")
        c = {"path": p, "status": status}
        if p.endswith(TEXT_FOR_RULES):
            c["old"] = _blob_text(repo, then)
            c["new"] = _read_text(repo, p) if now != ABSENT else ""
        changes.append(c)
    return changes, now_state


def state_equal(a, b):
    return a["head"] == b["head"] and a["worktree"] == b["worktree"]
