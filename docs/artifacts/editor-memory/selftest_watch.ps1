<#
  Self-test for editor_watch.ps1 (docs/53 §15). Never touches Unity: synthetic logs + a stand-in "editor"
  process (PING.EXE). Exit code 0 = all cases pass. Prints one PASS/FAIL line per case.
  Usage: powershell -NoProfile -ExecutionPolicy Bypass -File selftest_watch.ps1
#>
$ErrorActionPreference = 'Stop'
$watch = Join-Path $PSScriptRoot 'editor_watch.ps1'
$tmp = Join-Path $env:TEMP ('watch-selftest-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force $tmp | Out-Null
$M = 'Access version should be odd when acquiring lock'
$fails = 0
$stand = Start-Process -FilePath 'ping.exe' -ArgumentList '-n 600 127.0.0.1' -WindowStyle Hidden -PassThru
try {

function Run-Watch($name, $log, [scriptblock]$writer, [hashtable]$extra) {
    $out = Join-Path $tmp "$name.csv"
    $job = $null
    if ($writer) { $job = Start-Job -ScriptBlock $writer -ArgumentList $log, $M }
    $args = @{ Out = $out; Log = $log; IntervalSec = 1; MaxMinutes = 0.12; ProcessName = 'PING.EXE' }
    foreach ($k in $extra.Keys) { $args[$k] = $extra[$k] }
    & $watch @args
    if ($job) { Wait-Job $job -Timeout 30 | Out-Null; Remove-Job $job -Force }
    return $out
}
function Rows($csv) { Get-Content $csv | Where-Object { $_ -notmatch '^#' } | ConvertFrom-Csv }
# one row per sample: log/marker/status columns are per-sample, repeated on every process row of that sample
function Samples($csv) { Rows $csv | Group-Object utc | ForEach-Object { $_.Group[0] } }
function Check($name, [bool]$ok, $detail) {
    if ($ok) { "PASS $name  $detail" } else { "FAIL $name  $detail"; $script:fails++ }
}

# W1 exact detection: 100 then 400 marker lines appended while sampling
$log = Join-Path $tmp 'w1.log'; Set-Content $log 'start' -Encoding ascii
$csv = Run-Watch 'w1' $log { param($l, $m) Start-Sleep 2; Add-Content $l (($m + "`r`n") * 100) -NoNewline; Start-Sleep 2; Add-Content $l (($m + "`r`n") * 400) -NoNewline } @{}
$r = Samples $csv
$sum = ($r | ForEach-Object { [int]$_.marker_in_delta } | Measure-Object -Sum).Sum
$alerts = @($r | Where-Object { $_.status -match 'ALERT:marker-detected' }).Count
$badOk = @($r | Where-Object { [int]$_.marker_in_delta -gt 0 -and $_.status -eq 'OK' }).Count
Check 'W1 marker count exact (500)' ($sum -eq 500 -and $alerts -ge 1 -and $badOk -eq 0) "sum=$sum alertRows=$alerts okRowsWithHits=$badOk"

# W2 chunk-boundary: marker #1 straddles the 4 MB chunk edge (must be found once, not 0 or 2 times); marker #2 is an
# ordinary marker in a later chunk. (A match cannot START in the overlap and still fit: overlap = marker length - 1.)
$log = Join-Path $tmp 'w2.log'; Set-Content $log 'start' -Encoding ascii
$csv = Run-Watch 'w2' $log { param($l, $m)
    Start-Sleep 2
    $fs = [IO.File]::Open($l, 'Append', 'Write', 'ReadWrite')
    $a = [byte[]](,[byte]46 * (4MB - 10)); $fs.Write($a, 0, $a.Length)                  # marker #1 straddles 4 MB
    $mb = [Text.Encoding]::ASCII.GetBytes($m); $fs.Write($mb, 0, $mb.Length)
    $b = [byte[]](,[byte]46 * (4MB - $mb.Length + 10 + 5)); $fs.Write($b, 0, $b.Length)   # marker #2 just past 8 MB
    $fs.Write($mb, 0, $mb.Length)
    $c = [byte[]](,[byte]46 * 1MB); $fs.Write($c, 0, $c.Length)
    $fs.Close() } @{}
$r = Samples $csv
$sum = ($r | ForEach-Object { if ($_.marker_in_delta) { [int]$_.marker_in_delta } else { 0 } } | Measure-Object -Sum).Sum
Check 'W2 chunk boundary, no double count (2)' ($sum -eq 2) "sum=$sum"

# W3 unreadable log is an ERROR, never OK with 0 hits
$csv = Run-Watch 'w3' (Join-Path $tmp 'does-not-exist.log') $null @{}
$r = Rows $csv
$allErr = @($r | Where-Object { $_.status -notmatch 'ERROR:log-unreadable' }).Count -eq 0
Check 'W3 missing log -> ERROR:log-unreadable on every row' ($allErr -and $r.Count -gt 0) "rows=$($r.Count)"

# W4 delta beyond the scan cap is reported, not silently under-counted
$log = Join-Path $tmp 'w4.log'; Set-Content $log 'start' -Encoding ascii
$csv = Run-Watch 'w4' $log { param($l, $m) Start-Sleep 2; Add-Content $l ('x' * 5000) } @{ ScanCapBytes = 1000 }
$hit = @(Rows $csv | Where-Object { $_.status -match 'ALERT:delta-exceeds-scan-cap' }).Count
Check 'W4 delta > scan cap -> ALERT' ($hit -ge 1) "alertRows=$hit"

# W5 log rotation (Editor restart): new log scanned from byte 0
$log = Join-Path $tmp 'w5.log'; Set-Content $log ('y' * 5000) -Encoding ascii
$csv = Run-Watch 'w5' $log { param($l, $m) Start-Sleep 2; Set-Content $l (($m + "`r`n") * 3) -NoNewline -Encoding ascii } @{}
$r = Rows $csv | Where-Object { $_.status -match 'ALERT:log-rotated' }
$hits = if ($r) { [int]@($r)[0].marker_in_delta } else { -1 }
Check 'W5 rotation -> ALERT:log-rotated, markers in new log counted (3)' ($r -and $hits -eq 3) "hits=$hits"

# W6 no Editor process is an ERROR row, not silence
$log = Join-Path $tmp 'w6.log'; Set-Content $log 'start' -Encoding ascii
$csv = Run-Watch 'w6' $log $null @{ ProcessName = 'no-such-editor-xyz.exe' }
$r = Rows $csv
$ok = $r.Count -gt 0 -and @($r | Where-Object { $_.status -notmatch 'ERROR:no-editor-process' }).Count -eq 0
Check 'W6 no editor process -> ERROR row every sample' $ok "rows=$($r.Count)"

# W7 stop threshold writes '# stopped:' as the last line
$log = Join-Path $tmp 'w7.log'; Set-Content $log 'start' -Encoding ascii
$csv = Run-Watch 'w7' $log { param($l, $m) Start-Sleep 2; Add-Content $l ('z' * 30000) } @{ StopLogGB = 0.00002; MaxMinutes = 1 }
$lastLine = Get-Content $csv -Tail 1
Check 'W7 log threshold -> "# stopped: log>"' ($lastLine -match '^# stopped: log>') "last='$lastLine'"

# W8 a quiet, healthy sample is exactly OK
$log = Join-Path $tmp 'w8.log'; Set-Content $log 'start' -Encoding ascii
$csv = Run-Watch 'w8' $log $null @{}
$r = Rows $csv
Check 'W8 healthy samples -> status OK' ($r.Count -gt 0 -and @($r | Where-Object { $_.status -ne 'OK' }).Count -eq 0) "rows=$($r.Count)"

# --- added after the independent review (docs/53 §15.7) ---
# W9 log missing at start, appears later with 3 markers -> ALERT:log-reappeared and all 3 counted (no silent baseline)
$log = Join-Path $tmp 'w9.log'
$csv = Run-Watch 'w9' $log { param($l, $m) Start-Sleep 3; Set-Content $l (($m + "`r`n") * 3) -NoNewline -Encoding ascii } @{}
$r = @(Samples $csv | Where-Object { $_.status -match 'ALERT:log-reappeared' })
$hits = if ($r.Count) { [int]$r[0].marker_in_delta } else { -1 }
Check 'W9 log appears later -> ALERT:log-reappeared, 3 counted' ($r.Count -ge 1 -and $hits -eq 3) "hits=$hits"

# W10 Editor restart where the NEW log is already LARGER than the old one (size never shrinks) -> still rotation
$log = Join-Path $tmp 'w10.log'; Set-Content $log ('OLD-SESSION-HEADER ' + ('a' * 200)) -Encoding ascii
$csv = Run-Watch 'w10' $log { param($l, $m) Start-Sleep 3; Set-Content $l ('NEW-SESSION-HEADER ' + ('b' * 400) + "`r`n" + (($m + "`r`n") * 3)) -NoNewline -Encoding ascii } @{}
$r = @(Samples $csv | Where-Object { $_.status -match 'ALERT:log-rotated' })
$hits = if ($r.Count) { [int]$r[0].marker_in_delta } else { -1 }
Check 'W10 larger new log -> ALERT:log-rotated, 3 counted' ($r.Count -ge 1 -and $hits -eq 3) "hits=$hits"

# W11 a failing CIM query is ERROR:sample-exception, not OK / not mislabelled
$log = Join-Path $tmp 'w11.log'; Set-Content $log 'start' -Encoding ascii
$csv = Run-Watch 'w11' $log $null @{ ProcessName = "bad'name" }
$r = Rows $csv
Check 'W11 CIM failure -> ERROR:sample-exception on every row' ($r.Count -gt 0 -and @($r | Where-Object { $_.status -notmatch 'ERROR:sample-exception' }).Count -eq 0) "rows=$($r.Count)"

# W12 markers already in the log when the sampler starts are reported on the first sample
$log = Join-Path $tmp 'w12.log'; Set-Content $log (($M + "`r`n") * 2) -NoNewline -Encoding ascii
$csv = Run-Watch 'w12' $log $null @{}
$first = @(Samples $csv)[0]
Check 'W12 pre-existing markers -> first sample ALERT, 2 counted' ($first.status -match 'ALERT:marker-detected' -and [int]$first.marker_in_delta -eq 2) "first=$($first.marker_in_delta) $($first.status)"

} finally {
    Stop-Process -Id $stand.Id -Force -ErrorAction SilentlyContinue
    Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
}
"watch selftest: $fails failure(s)"
exit $fails
