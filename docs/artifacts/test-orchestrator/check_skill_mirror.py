"""Skill mirror check (docs/57 §12.2 S6): .claude/skills and .agents/skills must stay identical.

    python -I docs/artifacts/test-orchestrator/check_skill_mirror.py [--claude DIR] [--agents DIR]

The ONLY allowed difference is the name of the instruction file the copy refers to: `CLAUDE.md` in .claude/,
`AGENTS.md` in .agents/ (the existing convention). Anything else — a missing skill, a missing file, any other
changed byte — is drift. Exit: 0 in sync · 1 drift (listed) · 2 usage.
"""
import argparse
import os
import sys

sys.dont_write_bytecode = True


def files_under(root):
    out = set()
    for d, _, files in os.walk(root):
        for f in files:
            out.add(os.path.relpath(os.path.join(d, f), root).replace("\\", "/"))
    return out


def read(path):
    with open(path, "rb") as f:
        return f.read()


def compare(claude_root, agents_root):
    problems = []
    a, b = files_under(claude_root), files_under(agents_root)
    for p in sorted(a - b):
        problems.append("only in %s: %s" % (claude_root, p))
    for p in sorted(b - a):
        problems.append("only in %s: %s" % (agents_root, p))
    for p in sorted(a & b):
        x, y = read(os.path.join(claude_root, p)), read(os.path.join(agents_root, p))
        if x == y:
            continue
        if x.replace(b"CLAUDE.md", b"AGENTS.md") == y:
            continue
        xl = x.decode("utf-8", "replace").splitlines()
        yl = y.replace(b"AGENTS.md", b"CLAUDE.md").decode("utf-8", "replace").splitlines()
        line = next((i + 1 for i, (l1, l2) in enumerate(zip(xl, yl)) if l1 != l2), min(len(xl), len(yl)) + 1)
        problems.append("content differs: %s (first difference at line %d)" % (p, line))
    return problems


def main(argv):
    repo = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", ".."))
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--claude", default=os.path.join(repo, ".claude", "skills"))
    ap.add_argument("--agents", default=os.path.join(repo, ".agents", "skills"))
    a = ap.parse_args(argv)
    for d in (a.claude, a.agents):
        if not os.path.isdir(d):
            print("usage: not a directory: %s" % d, file=sys.stderr)
            return 2
    problems = compare(a.claude, a.agents)
    getattr(sys.stdout, "reconfigure", lambda **_: None)(encoding="utf-8")
    for p in problems:
        print("DRIFT: " + p)
    print("skill mirror: %s (%d file(s) compared)" % ("OK" if not problems else "DRIFT", len(files_under(a.claude) & files_under(a.agents))))
    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
