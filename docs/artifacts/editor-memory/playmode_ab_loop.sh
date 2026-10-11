#!/bin/bash
# Run the PlayMode suite until N valid runs complete (max N+3 attempts). Stops immediately (no retry, no restart) on:
#   Editor not 'ready' (crash/hang), flood marker in Editor.log, log > 500 MB, Editor private bytes > 12 GB,
#   recompile not idle. A run whose request never starts within 60 s is logged INVALID and does not count.
# usage: playmode_ab_loop.sh <label> <first_index> <N_valid> <out>   env: LOGFILE, DRYRUN=1 (checks only)
export PATH="$PATH:$HOME/AppData/Local/Unity/bin"
L="${LOGFILE:-/d/Unity Project/CharacterController/Logs/Editor.log}"
label=$1; k=$2; need=$3; out=$4
MARK='Access version should be odd'
editor_pid() { unity status 2>/dev/null | awk -F'\t' '$2=="ready"{print $5}' | head -1; }
private_gb() { powershell -NoProfile -Command "[math]::Round((Get-Process -Id $1 -ErrorAction SilentlyContinue).PrivateMemorySize64/1GB,2)"; }
check() {
  local pid; pid=$(editor_pid)
  if [ -z "$pid" ]; then echo "$label $1 STOP editor-not-ready" >> "$out"; return 10; fi
  local fl; fl=$(grep -c "$MARK" "$L"); local sz; sz=$(stat -c %s "$L"); local pg; pg=$(private_gb "$pid")
  echo "$label $1 pid=$pid flood_total=$fl log_bytes=$sz private_gb=$pg" >> "$out"
  if [ "$fl" -gt 0 ]; then echo "$label $1 STOP flood-onset" >> "$out"; return 11; fi
  if [ "$sz" -gt 524288000 ]; then echo "$label $1 STOP log>500MB" >> "$out"; return 12; fi
  if awk "BEGIN{exit !($pg>12)}"; then echo "$label $1 STOP private>12GB" >> "$out"; return 13; fi
  if [ "${DRYRUN:-0}" != "1" ] && [ "$(unity cmd recompile_status 2>&1 | grep -c '"status":"idle"')" -eq 0 ]; then echo "$label $1 STOP recompile-not-idle" >> "$out"; return 14; fi
  return 0
}
valid=0; attempts=0
while [ $valid -lt $need ] && [ $attempts -lt $((need+3)) ]; do
  attempts=$((attempts+1))
  sleep 20                                   # let the previous run's play-mode exit / domain reload settle
  check "$k-pre" || exit $?
  if [ "${DRYRUN:-0}" = "1" ]; then valid=$((valid+1)); k=$((k+1)); continue; fi
  t0=$(date -u +%H:%M:%S); s0=$(date +%s)
  unity cmd run_tests --mode PlayMode --async_tests true >/dev/null 2>&1
  seen=0; st=""
  for i in $(seq 1 120); do
    st=$(unity cmd test_status 2>&1 | tail -1)
    echo "$st" | grep -q '"running"' && seen=1
    if [ $seen -eq 1 ] && echo "$st" | grep -q '"completed"'; then break; fi
    if [ $seen -eq 0 ] && [ $(( $(date +%s)-s0 )) -gt 60 ]; then break; fi
    [ -z "$(editor_pid)" ] && break
    sleep 5
  done
  sum=$(echo "$st" | tr -d ' ' | grep -o '"summary":{[^}]*}' | head -1)
  if [ $seen -eq 0 ] || [ -z "$sum" ]; then
    echo "$label$k start=${t0}Z INVALID (run did not start / no summary) dur=$(( $(date +%s)-s0 ))s" >> "$out"
  else
    echo "$label$k start=${t0}Z dur=$(( $(date +%s)-s0 ))s $sum" >> "$out"; valid=$((valid+1))
  fi
  check "$k-post" || exit $?
  k=$((k+1))
done
echo "$label done valid=$valid attempts=$attempts" >> "$out"
