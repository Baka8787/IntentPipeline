"""Before/after comparison of the P5 combat-positioning probe (docs/56 §11–§12), as DISTRIBUTIONS, not single runs.

    python -I docs/artifacts/p5-metrics/compare_metrics.py --before COMMIT[,COMMIT...] --after COMMIT[,COMMIT...]
           [--metrics Logs/p5-metrics] [--markdown OUT.md]

Reads Logs/p5-metrics/metrics.tsv and every rotated metrics.tsv.old-N (columns are looked up by header name, so old
files without the newer columns still contribute the columns they have). Rows are grouped by the `sources` column (hash of Assets/Scripts/**/*.cs, uncommitted changes included) when present,
else by `commit`. Pass group ids (first 7–10 chars).

For each scenario × metric it prints min / median / max and the run count of both groups, and a verdict:
    BETTER / WORSE   the after-range lies entirely outside the before-range (in the metric's good direction)
    SAME             both ranges are a single identical value
    OVERLAP          the ranges overlap — NOT evidence of a change, whatever the medians say
    n/a              a side has no data for this metric
The good direction of each metric is declared below; metrics without one are reported without a verdict.
Exit 0 always (it reports, it does not judge the release).
"""
import argparse
import glob
import os
import statistics
import sys

sys.dont_write_bytecode = True

# metric -> (higher is better?)  None = report only (no direction)
DIRECTION = {
    "attackValidRatio": True,
    "inBandFrames": True,
    "inBandBlockedFrames": False,
    "firstValidFrame": False,
    "meanDistance": None,
    "travelled": None,
    "metersPerValidSecond": False,
    "attacks": None,
    "attacksNotValid": False,
    "noProgressFrames": False,
    "allowedMoveFrames": None,
    "recoverFrames": False,
    "strafeBlockedFrames": False,
    "modeSwitches": None,
    "strafeFlips": None,
    "pathRequests": False,
    "stuckFrames": None,
    "stuckInActionHurt": False,
    "actionFrames": None,
    "hurtFrames": None,
    "strafeFrames": None,
    "retreatFrames": None,
    "retreatNoProgressFrames": False,
    "stuckAfterNonExecuted": False,
    "slotFrames": None,
    "slotHoldFrames": None,
    "slotChanges": None,
}
NOT_A_VALUE = {"utc", "commit", "sources", "scenario", "variant"}


def read_rows(directory):
    rows = []
    paths = sorted(glob.glob(os.path.join(directory, "metrics.tsv*")))
    for path in paths:
        with open(path, encoding="utf-8") as f:
            lines = [l.rstrip("\r\n") for l in f if l.strip()]
        if not lines:
            continue
        header = lines[0].split("\t")
        for line in lines[1:]:
            cells = line.split("\t")
            if len(cells) != len(header):
                continue
            rows.append(dict(zip(header, cells)))
    return rows


def number(value):
    try:
        v = float(value)
    except ValueError:
        return None
    return v if v == v and v not in (float("inf"), float("-inf")) else None


def summary(values):
    return (min(values), statistics.median(values), max(values), len(values)) if values else None


def verdict(before, after, higher_is_better):
    if before is None or after is None:
        return "n/a"
    bmin, _, bmax, _ = before
    amin, _, amax, _ = after
    if bmin == bmax == amin == amax:
        return "SAME"
    if higher_is_better is None:
        return ""
    if amin > bmax:
        return "BETTER" if higher_is_better else "WORSE"
    if amax < bmin:
        return "WORSE" if higher_is_better else "BETTER"
    return "OVERLAP"


def fmt(s):
    if s is None:
        return "—"
    lo, med, hi, n = s
    f = (lambda x: "%g" % round(x, 3))
    return "%s / %s / %s (n=%d)" % (f(lo), f(med), f(hi), n)


def group_of(row):
    """Newer rows carry a hash of the runtime sources (uncommitted changes included); older rows only the commit."""
    return row.get("sources") or row["commit"]


def in_group(row, ids, variant):
    """ids: group ids (sources hash or commit prefix). variant: None = any; otherwise the row's variant must match."""
    return group_of(row)[:10] in ids and (variant is None or row.get("variant") == variant)


def compare(rows, before, after, before_variant=None, after_variant=None):
    scenarios = sorted({r["scenario"] for r in rows})
    metrics = [m for m in DIRECTION]
    out = []
    for sc in scenarios:
        b_rows = [r for r in rows if r["scenario"] == sc and in_group(r, before, before_variant)]
        a_rows = [r for r in rows if r["scenario"] == sc and in_group(r, after, after_variant)]
        for m in metrics:
            bv = [v for v in (number(r.get(m, "")) for r in b_rows) if v is not None]
            av = [v for v in (number(r.get(m, "")) for r in a_rows) if v is not None]
            if not bv and not av:
                continue
            bs, as_ = summary(bv), summary(av)
            out.append((sc, m, bs, as_, verdict(bs, as_, DIRECTION[m])))
    return out


def to_markdown(result, before, after):
    L = ["# P5 probe comparison", "", "before = %s · after = %s" % (",".join(sorted(before)), ",".join(sorted(after))),
         "", "Values: min / median / max (n). OVERLAP = ranges overlap ⇒ not evidence of a change.", ""]
    current = None
    for sc, m, b, a, v in result:
        if sc != current:
            L += ["", "## %s" % sc, "", "| metric | before | after | verdict |", "|---|---|---|---|"]
            current = sc
        L.append("| %s | %s | %s | %s |" % (m, fmt(b), fmt(a), v))
    return "\n".join(L) + "\n"


def main(argv):
    repo = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", ".."))
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--before", required=True)
    ap.add_argument("--after", required=True)
    ap.add_argument("--metrics", default=os.path.join(repo, "Logs", "p5-metrics"))
    ap.add_argument("--markdown")
    ap.add_argument("--before-variant", default=None, help="only rows with this variant label (same build A/B)")
    ap.add_argument("--after-variant", default=None)
    a = ap.parse_args(argv)
    getattr(sys.stdout, "reconfigure", lambda **_: None)(encoding="utf-8")
    before = {c.strip()[:10] for c in a.before.split(",") if c.strip()}
    after = {c.strip()[:10] for c in a.after.split(",") if c.strip()}
    rows = read_rows(a.metrics)
    md = to_markdown(compare(rows, before, after, a.before_variant, a.after_variant), before, after)
    if a.before_variant or a.after_variant:
        md = md.replace("before = ", "variants %s → %s · before = " % (a.before_variant, a.after_variant), 1)
    if a.markdown:
        with open(a.markdown, "w", encoding="utf-8") as f:
            f.write(md)
    print(md, end="")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
