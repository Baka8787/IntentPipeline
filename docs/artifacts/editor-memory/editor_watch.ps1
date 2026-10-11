<#
  Unity Editor memory / log growth sampler (docs/53). READ-ONLY: never touches Unity, the log, or the project.

  Samples every -IntervalSec:
    - each Unity.exe (main Editor and AssetImportWorkers): PrivateBytes (= commit charge, what Windows'
      Resource-Exhaustion-Detector reports), WorkingSet, PagedMemory, Handles, Threads, CPU seconds
    - Editor.log size (opened with FileShare.ReadWrite so the true size is read, not the stale
      directory entry), and the count of a flood marker in the newly appended bytes
    - system commit charge / limit

  FAIL-CLOSED REPORTING (docs/53 §15): every sample writes at least one row, and every row carries a `status`:
    OK                               nothing abnormal observed AND every reading succeeded
    ALERT:marker-detected            the flood marker appeared in the new bytes
    ALERT:delta-exceeds-scan-cap     more new bytes than -ScanCapBytes: only the first cap bytes were scanned,
                                     so a marker count of 0 would NOT prove absence -> reported, never "OK"
    ALERT:log-rotated                a NEW log file (Editor restarted): detected by size shrinking OR by the first
                                     512 bytes changing (each session's log starts with a unique timestamp), so a new
                                     log that is already larger than the old one is caught too; scanned from byte 0
    ALERT:log-reappeared             the log was unreadable earlier in this sampler's life and is readable again;
                                     scanned from byte 0
    ERROR:log-unreadable             the log could not be opened (its size is unknown)
    ERROR:marker-scan-failed         the scan threw
    ERROR:no-editor-process          no process classified as the Editor exists (row written with empty pid)
    ERROR:sample-exception:<msg>     anything else failed while sampling
  The FIRST readable sample scans the whole current log (no silent baseline): markers already in the current
  Editor session's log are reported.
  Several conditions are joined with '|'. A consumer must treat anything other than exactly "OK" as unsafe,
  and must also treat a missing / stale / "# stopped" CSV as unsafe (the sampler itself may have died).

  Stop conditions (whichever comes first) -- the sampler only OBSERVES; it never kills Unity:
    -MaxMinutes, main-Editor PrivateBytes > -StopPrivateGB, Editor.log > -StopLogGB. The last line is then
    "# stopped: <reason>".

  Usage:
    powershell -File editor_watch.ps1 -Out C:\path\watch.csv [-IntervalSec 30] [-MaxMinutes 60]
  Test hooks: -Log <synthetic log>, -ProcessName <image name treated as the Editor>, -ScanCapBytes <n>.
#>
param(
    [Parameter(Mandatory = $true)][string]$Out,
    [string]$Log = '',
    [int]$IntervalSec = 30,
    [double]$MaxMinutes = 60,
    [double]$StopPrivateGB = 24,
    [double]$StopLogGB = 2,
    [string]$Marker = 'Access version should be odd',
    [string]$ProcessName = 'Unity.exe',
    [long]$ScanCapBytes = 64MB
)

function Get-TrueLength([string]$path) {
    try { $fs = [IO.File]::Open($path, 'Open', 'Read', 'ReadWrite,Delete'); $len = $fs.Length; $fs.Close(); return $len }
    catch { return -1 }
}

# Scans [from, min(to, from+cap)) forward in 4 MB chunks (with marker-length overlap so a marker spanning a chunk
# boundary is still found). Returns @{ Hits = n; Partial = bool } or $null when the read fails.
function Count-MarkerSince([string]$path, [long]$from, [long]$to, [string]$needle, [long]$cap) {
    $result = @{ Hits = 0; Partial = $false }
    if ($to -le $from) { return $result }
    $end = $to
    if ($to - $from -gt $cap) { $end = $from + $cap; $result.Partial = $true }
    # Latin-1 maps every byte to exactly one char, so a match index IS a byte offset. The needle is ASCII, and UTF-8
    # multi-byte sequences never contain ASCII bytes, so matching on Latin-1 text is exact for ASCII needles.
    $latin1 = [Text.Encoding]::GetEncoding(28591)
    $overlap = $needle.Length - 1
    $chunk = 4MB
    try {
        $fs = [IO.File]::Open($path, 'Open', 'Read', 'ReadWrite,Delete')
        try {
            $pos = $from
            $rx = [regex]::new([regex]::Escape($needle))
            while ($pos -lt $end) {
                $len = [int][Math]::Min($chunk + $overlap, $end - $pos)
                $buf = New-Object byte[] $len
                $null = $fs.Seek($pos, 'Begin')
                $n = $fs.Read($buf, 0, $len)
                $text = $latin1.GetString($buf, 0, $n)
                $last = ($pos + $chunk) -ge $end     # no further chunk will re-read the overlap region
                foreach ($m in $rx.Matches($text)) {
                    # defensive: a full match cannot start inside the overlap (overlap = needle length - 1), so this
                    # filter never drops anything today; it keeps the 'count once' guarantee if the overlap is ever widened
                    if ($m.Index -lt $chunk -or $last) { $result.Hits++ }
                }
                $pos += $chunk
            }
        } finally { $fs.Close() }
        return $result
    } catch { return $null }
}

function Get-Head([string]$path, [int]$n) {
    try {
        $fs = [IO.File]::Open($path, 'Open', 'Read', 'ReadWrite,Delete')
        try { $len = [int][Math]::Min($n, $fs.Length); $buf = New-Object byte[] $len; $null = $fs.Read($buf, 0, $len); return ,$buf }
        finally { $fs.Close() }
    } catch { return $null }
}
function Same-Prefix([byte[]]$a, [byte[]]$b) {
    $m = [Math]::Min($a.Length, $b.Length)
    for ($i = 0; $i -lt $m; $i++) { if ($a[$i] -ne $b[$i]) { return $false } }
    return $true
}

function Clean([string]$s) { return ($s -replace '[,\r\n]', ';') }

# Windows PowerShell 5.1 does not populate $PSScriptRoot inside param() defaults, so resolve here:
# <repo>/docs/artifacts/editor-memory -> <repo>/Logs/Editor.log
if (-not $Log) { $Log = Join-Path (Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $PSScriptRoot))) 'Logs\Editor.log' }

# Every failure inside a sample must reach the catch below (CIM errors are non-terminating by default in PS 5.1).
$ErrorActionPreference = 'Stop'
$start = Get-Date
$prevLen = -1; $prevHead = $null; $hadReading = $false; $sawUnreadable = $false
"utc,elapsed_s,pid,role,private_gb,working_set_gb,paged_gb,handles,threads,cpu_s,log_bytes,log_delta_bytes,marker_in_delta,sys_commit_gb,sys_commit_limit_gb,stop_reason,status" |
    Set-Content -Path $Out -Encoding utf8

while ($true) {
    $now = Get-Date
    $utc = $now.ToUniversalTime().ToString('o')
    $elapsed = [int]($now - $start).TotalSeconds
    $stop = ''
    $status = New-Object System.Collections.Generic.List[string]
    $len = -1; $delta = 0; $markerHits = ''
    try {
        $len = Get-TrueLength $Log
        if ($len -lt 0) {
            $status.Add('ERROR:log-unreadable'); $sawUnreadable = $true
        } else {
            $head = Get-Head $Log 512
            if ($null -eq $head) { throw "log head unreadable" }
            if (-not $hadReading) {
                $from = 0                                                 # first readable sample: scan the whole log
                if ($sawUnreadable) { $status.Add('ALERT:log-reappeared') }
            } elseif ($len -lt $prevLen -or -not (Same-Prefix $head $prevHead)) {
                $status.Add('ALERT:log-rotated'); $from = 0
            } else { $from = $prevLen }
            $delta = $len - $from
            $prevHead = $head; $hadReading = $true
            # ⚠️ PowerShell variable names are case-insensitive: never name this $marker (it would overwrite -Marker).
            $scan = Count-MarkerSince $Log $from $len $Marker $ScanCapBytes
            if ($null -eq $scan) { $status.Add('ERROR:marker-scan-failed') }
            else {
                $markerHits = $scan.Hits
                if ($scan.Hits -gt 0) { $status.Add('ALERT:marker-detected') }
                if ($scan.Partial) { $status.Add('ALERT:delta-exceeds-scan-cap') }
            }
        }
        $os = Get-CimInstance Win32_OperatingSystem -ErrorAction Stop
        $commitGB = [Math]::Round(($os.TotalVirtualMemorySize - $os.FreeVirtualMemory) / 1MB, 2)
        $limitGB = [Math]::Round($os.TotalVirtualMemorySize / 1MB, 2)

        $rows = New-Object System.Collections.Generic.List[string]
        $editorSeen = $false
        foreach ($p in @(Get-CimInstance Win32_Process -Filter "Name='$ProcessName'" -ErrorAction Stop)) {
            $gp = Get-Process -Id $p.ProcessId -ErrorAction SilentlyContinue
            if (-not $gp) { continue }
            # The `unity` CLI (~\AppData\Local\Unity\bin\unity.exe) is also named Unity.exe -- label it, don't count it as the Editor.
            $role = if ($p.ExecutablePath -match '\\Unity\\bin\\') { 'cli' } elseif ($p.CommandLine -match 'AssetImportWorker') { 'import-worker' } elseif ($p.CommandLine -match '-batchMode') { 'batch' } else { 'editor' }
            if ($role -eq 'editor') { $editorSeen = $true }
            $priv = [Math]::Round($gp.PrivateMemorySize64 / 1GB, 3)
            if ($role -eq 'editor' -and $priv -gt $StopPrivateGB) { $stop = "private>$StopPrivateGB" }
            $rows.Add(("{0},{1},{2},{3},{4},{5},{6},{7},{8},{9}" -f $utc, $elapsed, $p.ProcessId, $role, $priv,
                [Math]::Round($gp.WorkingSet64 / 1GB, 3), [Math]::Round($gp.PagedMemorySize64 / 1GB, 3),
                $gp.HandleCount, $gp.Threads.Count, [Math]::Round($gp.CPU, 1)))
        }
        if (-not $editorSeen) {
            $status.Add('ERROR:no-editor-process')
            $rows.Add(("{0},{1},,none,,,,,," -f $utc, $elapsed))
        }
    } catch {
        $status.Add('ERROR:sample-exception:' + (Clean $_.Exception.Message))
        $rows = New-Object System.Collections.Generic.List[string]
        $rows.Add(("{0},{1},,error,,,,,," -f $utc, $elapsed))
        $commitGB = ''; $limitGB = ''
    }
    if ($len -gt $StopLogGB * 1GB) { $stop = "log>$StopLogGB" }
    if (($now - $start).TotalMinutes -ge $MaxMinutes) { $stop = 'max-minutes' }
    $statusText = if ($status.Count -eq 0) { 'OK' } else { $status -join '|' }
    foreach ($r in $rows) {
        ("{0},{1},{2},{3},{4},{5},{6},{7}" -f $r, $len, $delta, $markerHits, $commitGB, $limitGB, $stop, $statusText) |
            Add-Content -Path $Out -Encoding utf8
    }
    if ($stop) { "# stopped: $stop" | Add-Content -Path $Out -Encoding utf8; break }
    if ($len -ge 0) { $prevLen = $len }
    Start-Sleep -Seconds $IntervalSec
}
