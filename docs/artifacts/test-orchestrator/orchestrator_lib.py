"""Shared helpers for the test orchestrator (docs/57 §12). Standard library only.

Every script in this folder is run as `python -I <script>`; -I drops the script directory from sys.path, so each
script inserts its own folder explicitly before importing this module (the folder is versioned, not user input).
"""
import hashlib
import json
import os
import re
import subprocess
import sys

PLANNER_VERSION = "1"
MODES = ("EditMode", "PlayMode")


# ---------------------------------------------------------------------------------------------------------------
# paths and globs
# ---------------------------------------------------------------------------------------------------------------

def norm_path(path):
    path = path.replace("\\", "/")
    return path[2:] if path.startswith("./") else path


_GLOB_CACHE = {}


def glob_to_regex(glob):
    """`**` = any characters including '/', `*` = any characters except '/', `?` = one character except '/'."""
    if glob in _GLOB_CACHE:
        return _GLOB_CACHE[glob]
    out, i = [], 0
    while i < len(glob):
        c = glob[i]
        if glob.startswith("**/", i):
            out.append("(?:.*/)?")
            i += 3
        elif glob.startswith("**", i):
            out.append(".*")
            i += 2
        elif c == "*":
            out.append("[^/]*")
            i += 1
        elif c == "?":
            out.append("[^/]")
            i += 1
        else:
            out.append(re.escape(c))
            i += 1
    rx = re.compile("^" + "".join(out) + "$")
    _GLOB_CACHE[glob] = rx
    return rx


def matches(path, pattern):
    """A pattern is a glob, or `re:<regex>` for the few rules a glob cannot express."""
    if pattern.startswith("re:"):
        return re.search(pattern[3:], path) is not None
    return glob_to_regex(pattern).match(path) is not None


def first_match(path, patterns):
    for p in patterns:
        if matches(path, p):
            return p
    return None


def sha256_file(path):
    h = hashlib.sha256()
    with open(path, "rb") as f:
        h.update(f.read())
    return h.hexdigest()


def utf8_stdout():
    """Console output is UTF-8 (Chinese test messages); a captured stream without reconfigure() is left alone."""
    getattr(sys.stdout, "reconfigure", lambda **_: None)(encoding="utf-8")


def load_json(path):
    with open(path, encoding="utf-8") as f:
        return json.load(f)


def dump_json(obj):
    return json.dumps(obj, ensure_ascii=False, indent=2, sort_keys=True) + "\n"


# ---------------------------------------------------------------------------------------------------------------
# messages
# ---------------------------------------------------------------------------------------------------------------

_WS = re.compile(r"\s+")


def normalize_message(text):
    """CRLF and every whitespace run collapse to one space: NUnit pads `But was:  False` inconsistently."""
    return _WS.sub(" ", (text or "").replace("\r\n", "\n")).strip()


def fixture_of(full_name):
    """Project.Tests.PlayMode.Class.Method(args) -> Class."""
    head = full_name.split("(", 1)[0]
    parts = head.split(".")
    return parts[-2] if len(parts) >= 2 else head


# ---------------------------------------------------------------------------------------------------------------
# safe_unity_test.sh records (Logs/safe-test/<UTC>-<label>.XXXX/)
# ---------------------------------------------------------------------------------------------------------------

def _kv_file(path):
    out = {}
    if not os.path.isfile(path):
        return None
    with open(path, encoding="utf-8", errors="replace") as f:
        for line in f:
            line = line.rstrip("\r\n")
            if "=" in line and not line.startswith(" "):
                k, v = line.split("=", 1)
                out.setdefault(k.strip(), v)
    return out


class Record:
    """One safe_unity_test.sh invocation, read-only. Never trusts a field it could not parse."""

    def __init__(self, directory):
        self.dir = directory
        self.meta = _kv_file(os.path.join(directory, "meta.txt")) or {}
        self.summary = _kv_file(os.path.join(directory, "summary.txt"))
        self.mode = self.meta.get("mode")
        self.filter = self.meta.get("filter", None)
        self.include_explicit = self.meta.get("include_explicit") == "1"
        self.check_only = self.meta.get("check_only") == "1"
        self.git_head = self.meta.get("git_head", "")
        self.verdict = self.summary.get("verdict") if self.summary else None
        self.exit_code = None
        if self.summary and re.fullmatch(r"-?\d+", self.summary.get("exit", "") or ""):
            self.exit_code = int(self.summary["exit"])
        self.reason = self.summary.get("reason", "") if self.summary else ""
        self.results = None          # list of {FullName, Status, Duration, Message}
        self.runner_duration = None
        self.status_error = None
        self._load_status()

    @property
    def is_full_run(self):
        return self.filter == "" and not self.include_explicit and not self.check_only

    def _load_status(self):
        candidates = sorted(f for f in os.listdir(self.dir) if f.endswith("-test_status.json")) if os.path.isdir(self.dir) else []
        if not candidates:
            self.status_error = "no test_status JSON"
            return
        try:
            outer = load_json(os.path.join(self.dir, candidates[-1]))
            inner = outer["data"]["result"]
            if isinstance(inner, str):
                inner = json.loads(inner)
            if inner.get("status") != "completed":
                self.status_error = "test_status is '%s', not completed" % inner.get("status")
                return
            results = inner.get("results")
            if not isinstance(results, list):
                self.status_error = "results missing"
                return
            for r in results:
                if not isinstance(r, dict) or not isinstance(r.get("FullName"), str) or not isinstance(r.get("Status"), str):
                    self.status_error = "malformed result entry"
                    return
            self.results = results
            self.runner_duration = inner.get("duration")
        except (OSError, ValueError, KeyError, TypeError) as e:
            self.status_error = "unreadable test_status JSON: %s" % e.__class__.__name__


def find_latest_full_record(root, mode):
    """Newest Logs/safe-test record that is a complete, unfiltered run of `mode` (used for durations only)."""
    if not os.path.isdir(root):
        return None
    for name in sorted(os.listdir(root), reverse=True):
        d = os.path.join(root, name)
        if not os.path.isdir(d):
            continue
        r = Record(d)
        if r.mode == mode and r.is_full_run and r.results is not None and r.exit_code in (0, 1):
            return r
    return None


# ---------------------------------------------------------------------------------------------------------------
# git (read-only except `hash-object -w`, which only adds loose objects: no ref, index or work-tree change)
# ---------------------------------------------------------------------------------------------------------------

class GitError(Exception):
    pass


def git(repo, *args, input_bytes=None, check=True):
    p = subprocess.run(["git", "-C", repo, *args], input=input_bytes, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
    if check and p.returncode != 0:
        raise GitError("git %s failed: %s" % (" ".join(args[:3]), p.stderr.decode("utf-8", "replace").strip()[:300]))
    return p


def git_text(repo, *args, check=True):
    return git(repo, *args, check=check).stdout.decode("utf-8", "replace")
