@echo off
rem Starts the NLLB-200 server for ESPLanguage_EN (Settings -> NLLB-200, URL http://localhost:8000/v1).
rem Stop it: close this window, Ctrl+C, stop_nllb.bat, or Stop server in ESPLanguage_EN.
rem It stops by itself after 30 minutes without use:  start_nllb.bat --idle-minutes 60   (0 = never)
rem Other model:  start_nllb.bat --model facebook/nllb-200-distilled-1.3B
rem Without GPU:  start_nllb.bat --device cpu --model facebook/nllb-200-distilled-600M
cd /d "%~dp0"
rem keep the downloaded model next to this file (hf\), not in C:\Users\<you>\.cache
set HF_HOME=%~dp0hf
set HF_HUB_DISABLE_SYMLINKS_WARNING=1
title NLLB-200 server for ESPLanguage_EN
venv\Scripts\python.exe nllb_server.py %*
pause
