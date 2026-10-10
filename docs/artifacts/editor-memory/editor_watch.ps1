<#
  Unity Editor memory / log growth sampler (docs/53). READ-ONLY: never touches Unity, the log, or the project.

  Samples every -IntervalSec:
    - each Unity.exe (main Editor and AssetImportWorkers): PrivateBytes (= commit charge, what Windows'
      Resource-Exhaustion-Detector reports), WorkingSet, PagedMemory, Handles, Threads, CPU seconds
    - Editor.log size (opened with FileShare.ReadWrite so the true size is read, not the stale
      directory entry), and the count of a flood marker in the newly appended bytes
    - system commit charge / limit

  Stop conditions (whichever comes first) -- the sampler only OBSERVES; it never kills Unity:
    -MaxMinutes, main-Editor PrivateBytes > -StopPrivateGB, Editor.log > -StopLogGB.

  Usage:
    powershell -File editor_watch.ps1 -Out C:\path\watch.csv [-IntervalSec 30] [-MaxMinutes 60]
#>
param(
    [Parameter(Mandatory = $true)][string]$Out,
    [string]$Log = '',
    [int]$IntervalSec = 30,
    [double]$MaxMinutes = 60,
    [double]$StopPrivateGB = 24,
    [double]$StopLogGB = 2,
    [string]$Marker = 'Access version should be odd'
)

function Get-TrueLength([string]$path) {
    try { $fs = [IO.File]::Open($path, 'Open', 'Read', 'ReadWrite,Delete'); $len = $fs.Length; $fs.Close(); return $len }
    catch { return -1 }
}

function Count-MarkerSince([string]$path, [long]$from, [long]$to, [string]$marker) {
    # Streams only the appended range; caps the scan at 64 MB per sample so the sampler itself stays cheap.
    if ($to -le $from) { return 0 }
    $cap = [Math]::Min($to - $from, 64MB)
    try {
        $fs = [IO.File]::Open($path, 'Open', 'Read', 'ReadWrite,Delete')
        $null = $fs.Seek($to - $cap, 'Begin')
        $buf = New-Object byte[] $cap
        $n = $fs.Read($buf, 0, $cap); $fs.Close()
        $text = [Text.Encoding]::UTF8.GetString($buf, 0, $n)
        return ([regex]::Matches($text, [regex]::Escape($marker))).Count
    } catch { return -1 }
}

# Windows PowerShell 5.1 does not populate $PSScriptRoot inside param() defaults, so resolve here:
# <repo>/docs/artifacts/editor-memory -> <repo>/Logs/Editor.log
if (-not $Log) { $Log = Join-Path (Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $PSScriptRoot))) 'Logs\Editor.log' }

$start = Get-Date
$prevLen = Get-TrueLength $Log
"utc,elapsed_s,pid,role,private_gb,working_set_gb,paged_gb,handles,threads,cpu_s,log_bytes,log_delta_bytes,marker_in_delta,sys_commit_gb,sys_commit_limit_gb,stop_reason" |
    Set-Content -Path $Out -Encoding utf8

while ($true) {
    $now = Get-Date
    $elapsed = [int]($now - $start).TotalSeconds
    $len = Get-TrueLength $Log
    $delta = if ($prevLen -ge 0 -and $len -ge $prevLen) { $len - $prevLen } else { 0 }
    $marker = Count-MarkerSince $Log $prevLen $len $Marker
    $os = Get-CimInstance Win32_OperatingSystem
    $commitGB = [Math]::Round(($os.TotalVirtualMemorySize - $os.FreeVirtualMemory) / 1MB, 2)
    $limitGB = [Math]::Round($os.TotalVirtualMemorySize / 1MB, 2)

    $procs = Get-CimInstance Win32_Process -Filter "Name='Unity.exe'"
    $stop = ''
    foreach ($p in $procs) {
        $gp = Get-Process -Id $p.ProcessId -ErrorAction SilentlyContinue
        if (-not $gp) { continue }
        $role = if ($p.CommandLine -match 'AssetImportWorker') { 'import-worker' } elseif ($p.CommandLine -match '-batchMode') { 'batch' } else { 'editor' }
        $priv = [Math]::Round($gp.PrivateMemorySize64 / 1GB, 3)
        if ($role -eq 'editor' -and $priv -gt $StopPrivateGB) { $stop = "private>$StopPrivateGB" }
        "{0},{1},{2},{3},{4},{5},{6},{7},{8},{9},{10},{11},{12},{13},{14},{15}" -f `
            $now.ToUniversalTime().ToString('o'), $elapsed, $p.ProcessId, $role, $priv,
            [Math]::Round($gp.WorkingSet64 / 1GB, 3), [Math]::Round($gp.PagedMemorySize64 / 1GB, 3),
            $gp.HandleCount, $gp.Threads.Count, [Math]::Round($gp.CPU, 1),
            $len, $delta, $marker, $commitGB, $limitGB, $stop | Add-Content -Path $Out -Encoding utf8
    }
    if ($len -gt $StopLogGB * 1GB) { $stop = "log>$StopLogGB" }
    if (($now - $start).TotalMinutes -ge $MaxMinutes) { $stop = 'max-minutes' }
    if ($stop) { "# stopped: $stop" | Add-Content -Path $Out -Encoding utf8; break }
    $prevLen = $len
    Start-Sleep -Seconds $IntervalSec
}
