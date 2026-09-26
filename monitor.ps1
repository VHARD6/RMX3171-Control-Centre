Remove-Item -Force memory_log.txt -ErrorAction SilentlyContinue
while ($true) {
    $procs = Get-Process RMX3171ControlCentre -ErrorAction SilentlyContinue
    if ($procs) {
        $p = $procs[0]
        $adbCount = (Get-Process adb -ErrorAction SilentlyContinue).Count
        $ws = [math]::Round($p.WorkingSet64 / 1MB, 2)
        $priv = [math]::Round($p.PrivateMemorySize64 / 1MB, 2)
        $line = "$(Get-Date -Format 'HH:mm:ss') | WS: $ws MB | Priv: $priv MB | Threads: $($p.Threads.Count) | Handles: $($p.HandleCount) | adb: $adbCount"
        Add-Content -Path memory_log.txt -Value $line
    }
    Start-Sleep -Seconds 1
}
