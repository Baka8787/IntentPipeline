#!/bin/bash
# Fake sampler for selftest_safe_entry.sh (docs/53 §17): same CLI shape safe_unity_test.sh uses for SAMPLER_CMD
#   <csv> <pidfile> <interval_s> <max_minutes>
# Writes rows in editor_watch.ps1's CSV format. FAKE_SAMPLER_MODE: ok | never | alert_first | alert_after:N | die_after:N
# FAKE_SAMPLER_PID: editor pid written in the rows (default 4242).
csv=$1; pidf=$2; iv=$3
echo $$ > "$pidf"
mode=${FAKE_SAMPLER_MODE:-ok}; epid=${FAKE_SAMPLER_PID:-4242}
[ "$mode" = never ] && { sleep 999; exit 0; }
echo "utc,elapsed_s,pid,role,private_gb,working_set_gb,paged_gb,handles,threads,cpu_s,log_bytes,log_delta_bytes,marker_in_delta,sys_commit_gb,sys_commit_limit_gb,stop_reason,status" > "$csv"
i=0
while :; do
  i=$((i+1)); st=OK
  case "$mode" in
    alert_first) st=ALERT:marker-detected ;;
    alert_after:*) [ $i -gt "${mode#alert_after:}" ] && st=ALERT:marker-detected ;;
    die_after:*) [ $i -gt "${mode#die_after:}" ] && exit 0 ;;
  esac
  echo "$(date -u +%Y-%m-%dT%H:%M:%S.0000000Z),$i,$epid,editor,3.5,1,3.5,1,1,1,10,0,0,40,58,,$st" >> "$csv"
  sleep "$iv"
done
