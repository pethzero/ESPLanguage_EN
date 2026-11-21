@echo off
rem Starts the NLLB-200 server for ESPLanguage_EN (Engine: Local AI, URL http://localhost:8000/v1).
rem Other model:  start_nllb.bat --model facebook/nllb-200-distilled-1.3B
rem Without GPU:  start_nllb.bat --device cpu --model facebook/nllb-200-distilled-600M
cd /d "%~dp0"
title NLLB-200 server for ESPLanguage_EN
venv\Scripts\python.exe nllb_server.py %*
pause
