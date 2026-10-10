"""Phase / memory timeline from a reduced Editor.log produced by log_census.py (docs/53).

Recovers the ORIGINAL byte offset of every line of reduced.log (verbatim lines are copied byte-for-byte;
<<RUN ...>> markers carry their own start..end offsets), then emits one CSV row per phase event:

  offset, est_utc, kind, flood_lines_since_prev_event, detail

kind: token (Licensing token refresh; used as the clock), mem (Unity 'Memory consumption went from A to B'),
      tests (test run finished), play (enter/exit play mode), reload (domain reload), refresh (asset refresh),
      pipeline (com.unity.pipeline /api/exec failures), oom, crash, flood-start (first run of the flood line)

est_utc is interpolated from Licensing access-token refreshes ("expires: T" is issued ~2 h before T, so
refresh time ~= T - 2 h). It is an ESTIMATE (±30 min) and stays blank outside the bracketed range.

Usage:  python -I log_timeline.py <reduced.log> <out.csv> [flood substring]
"""
import bisect
import csv
import re
import sys
from datetime import datetime, timedelta, timezone

FLOOD = "Access version should be odd"
RUN_RE = re.compile(rb"^<<RUN x(\d+) bytes=(\d+)\.\.(\d+) line=(\d+)>> ?(.*)$")
PATTERNS = [
    ("token", re.compile(rb"Successfully updated access token: .*expires: (20\d\d-\d\d-\d\dT\d\d:\d\d:\d\d)")),
    ("mem", re.compile(rb"Memory consumption went from (.*)")),
    ("tests", re.compile(rb"Run finished: (.*)")),
    ("play", re.compile(rb"(EnteredPlayMode|ExitingPlayMode|Entering Playmode|Exiting Playmode|EnterPlayMode|ExitPlayMode)")),
    ("reload", re.compile(rb"(Begin MonoManager ReloadAssembly|Reloading assemblies|Domain Reload Profiling)")),
    ("refresh", re.compile(rb"Asset Pipeline Refresh .*Initiated by (.*)")),
    ("pipeline", re.compile(rb"(Failed to handle /api/exec request: .*)")),
    ("oom", re.compile(rb"(Could not allocate memory.*)")),
    ("crash", re.compile(rb"(A crash has been intercepted.*|Received signal .*|Crash!!!.*)")),
]


def main():
    src, out = sys.argv[1], sys.argv[2]
    flood = (sys.argv[3] if len(sys.argv) > 3 else FLOOD).encode()
    events = []                 # [offset, kind, flood_since, detail]
    off = 0
    flood_since = 0
    flood_started = False
    with open(src, "rb") as f:
        for line in f:
            m = RUN_RE.match(line.rstrip(b"\r\n"))
            if m:
                cnt, start, end = int(m.group(1)), int(m.group(2)), int(m.group(3))
                if flood in m.group(5):
                    if not flood_started:
                        events.append([start, "flood-start", 0, "first run x%d" % cnt])
                        flood_started = True
                    flood_since += cnt
                off = end
                continue
            if flood in line:
                flood_since += 1
            else:
                for kind, rx in PATTERNS:
                    mm = rx.search(line)
                    if mm:
                        events.append([off, kind, flood_since, mm.group(1).decode("utf-8", "replace")[:160]])
                        flood_since = 0
                        break
            off += len(line)

    # clock: token refresh ~= expires - 2h
    clock = []
    for e in events:
        if e[1] == "token":
            t = datetime.strptime(e[3], "%Y-%m-%dT%H:%M:%S").replace(tzinfo=timezone.utc) - timedelta(hours=2)
            clock.append((e[0], t))
    offs = [c[0] for c in clock]

    def est(o):
        i = bisect.bisect_right(offs, o)
        if i == 0 or i == len(clock):
            return ""
        (o0, t0), (o1, t1) = clock[i - 1], clock[i]
        if o1 == o0:
            return t0.isoformat()
        return (t0 + (t1 - t0) * ((o - o0) / (o1 - o0))).strftime("%Y-%m-%dT%H:%MZ")

    with open(out, "w", newline="", encoding="utf-8") as g:
        w = csv.writer(g)
        w.writerow(["offset", "est_utc", "kind", "flood_lines_since_prev_event", "detail"])
        for e in events:
            w.writerow([e[0], est(e[0]), e[1], e[2], e[3]])
    print(f"{len(events)} events -> {out}")


if __name__ == "__main__":
    main()
