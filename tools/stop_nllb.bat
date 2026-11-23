@echo off
rem Stops the NLLB server (port 8000, or the port given: stop_nllb.bat 8001).
rem 1. asks it to stop (/shutdown)   2. if it does not answer, ends the python process that holds the port.
setlocal
set PORT=%~1
if "%PORT%"=="" set PORT=8000
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "$port = %PORT%;" ^
  "function Holder { Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue | Select-Object -First 1 };" ^
  "$c = Holder; if (-not $c) { Write-Host \"No NLLB server is running on port $port.\"; exit 0 };" ^
  "try { Invoke-RestMethod -Method Post -Uri \"http://127.0.0.1:$port/shutdown\" -TimeoutSec 5 | Out-Null; Write-Host 'Asked the server to stop ...' } catch { Write-Host 'The server did not answer.' };" ^
  "for ($i = 0; $i -lt 20 -and (Holder); $i++) { Start-Sleep -Milliseconds 500 };" ^
  "$c = Holder; if (-not $c) { Write-Host 'NLLB server stopped. The GPU memory is free.'; exit 0 };" ^
  "$p = Get-Process -Id $c.OwningProcess -ErrorAction SilentlyContinue;" ^
  "if ($p -and $p.ProcessName -eq 'python') { Stop-Process -Id $p.Id -Force; Write-Host \"NLLB server (python, pid $($p.Id)) ended.\" }" ^
  "else { Write-Host \"Port $port is used by $($p.ProcessName) (pid $($c.OwningProcess)), not by the NLLB server: left alone.\"; exit 1 }"
if "%~2"=="" pause
