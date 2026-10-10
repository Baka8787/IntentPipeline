"""Unity Editor.log streaming census (docs/53).

Reads an arbitrarily large Editor.log line by line in constant memory and produces:

  <out>/reduced.log   -- the log with every run of >= RUN_MIN identical consecutive lines
                         collapsed into one marker line:
                         <<RUN x<count> bytes=<start>..<end> line=<first line no>>> <the line>
                         Everything else is copied verbatim, so the reduced file can be grepped
                         like the original (it is normally a few MB instead of tens of GB).
  <out>/census.json   -- top lines by count (exact match, bounded table), first offset of each,
                         per-1% byte bucket histogram of the top lines, run statistics,
                         and every ISO-8601 timestamp seen with its offset.

Usage:  python -I log_census.py <Editor.log> <out_dir> [--run-min 50]

Never loads the file into memory; never writes to the input. Safe on a live log (reads up to
the size observed at start).
"""
import json
import os
import re
import sys
import time
from collections import Counter

RUN_MIN = 50
TABLE_CAP = 2_000_000          # exact-line table cap; overflow lines are counted as "<other>"
TS_RE = re.compile(rb"20\d\d-\d\d-\d\d[T ]\d\d:\d\d:\d\d")
TS_CAP = 20_000


def main():
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    run_min = RUN_MIN
    if "--run-min" in sys.argv:
        run_min = int(sys.argv[sys.argv.index("--run-min") + 1])
    src, out = args[0], args[1]
    os.makedirs(out, exist_ok=True)
    size = os.path.getsize(src)
    bucket = max(1, size // 100)

    counts = Counter()
    first_off = {}
    overflow = 0
    hist = {}                   # line -> [count per bucket] (filled for lines that ever run)
    runs = []                   # (start_off, end_off, start_line, count, text)
    stamps = []

    t0 = time.time()
    prev = None
    run_cnt = 0
    run_start_off = 0
    run_start_line = 0
    buf = []                    # buffered lines of the current run while run_cnt < run_min
    off = 0
    lineno = 0

    with open(src, "rb") as f, open(os.path.join(out, "reduced.log"), "wb") as red:
        def flush_run(end_off):
            if prev is None:
                return
            if run_cnt >= run_min:
                red.write(b"<<RUN x%d bytes=%d..%d line=%d>> " % (run_cnt, run_start_off, end_off, run_start_line) + prev.rstrip(b"\r\n") + b"\n")
                runs.append((run_start_off, end_off, run_start_line, run_cnt, prev.rstrip(b"\r\n").decode("utf-8", "replace")[:200]))
            else:
                red.writelines(buf)

        for line in f:
            if off >= size:
                break
            lineno += 1
            if line == prev:
                run_cnt += 1
                if run_cnt < run_min:
                    buf.append(line)
                elif run_cnt == run_min:
                    buf = []
            else:
                flush_run(off)
                prev = line
                run_cnt = 1
                run_start_off = off
                run_start_line = lineno
                buf = [line]
                if TS_RE.search(line) and len(stamps) < TS_CAP:
                    stamps.append((off, line.rstrip(b"\r\n").decode("utf-8", "replace")[:200]))

            if line in counts or len(counts) < TABLE_CAP:
                if line not in counts:
                    first_off[line] = off
                counts[line] += 1
                if run_cnt >= run_min:
                    h = hist.get(line)
                    if h is None:
                        h = hist[line] = [0] * 101
                    h[off // bucket] += 1
            else:
                overflow += 1

            off += len(line)
            if lineno % 50_000_000 == 0:
                el = time.time() - t0
                print(f"{off/1e9:6.2f} GB  {lineno/1e6:7.0f} M lines  {el:6.0f}s", file=sys.stderr, flush=True)
        flush_run(off)

    top = counts.most_common(40)
    result = {
        "source": src,
        "bytes": size,
        "lines": lineno,
        "elapsed_s": round(time.time() - t0, 1),
        "run_min": run_min,
        "distinct_lines": len(counts),
        "overflow_lines": overflow,
        "top": [
            {"count": c, "bytes": c * len(l), "first_offset": first_off[l],
             "text": l.rstrip(b"\r\n").decode("utf-8", "replace")[:200]}
            for l, c in top
        ],
        "run_histogram_per_1pct": {
            l.rstrip(b"\r\n").decode("utf-8", "replace")[:120]: h for l, h in hist.items() if sum(h) > 10_000
        },
        "run_count": len(runs),
        "runs_first_200": runs[:200],
        "runs_largest_50": sorted(runs, key=lambda r: -r[3])[:50],
        "timestamps": stamps,
    }
    with open(os.path.join(out, "census.json"), "w", encoding="utf-8") as j:
        json.dump(result, j, ensure_ascii=False, indent=1)
    print(json.dumps({k: result[k] for k in ("bytes", "lines", "elapsed_s", "distinct_lines", "run_count")}))


if __name__ == "__main__":
    main()
