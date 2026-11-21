@echo off
rem Installs everything for nllb_server.py into a separate Python venv (default E:\nllb, or the folder given).
rem Needs Python 3.10-3.12 on PATH. NVIDIA GPU: CUDA build of PyTorch. No NVIDIA GPU: run with  install_nllb.bat <folder> cpu
setlocal
set DIR=%~1
if "%DIR%"=="" set DIR=E:\nllb
set TORCH_INDEX=https://download.pytorch.org/whl/cu128
if /i "%~2"=="cpu" set TORCH_INDEX=https://download.pytorch.org/whl/cpu

if not exist "%DIR%" mkdir "%DIR%"
copy /y "%~dp0nllb_server.py" "%DIR%\" >nul
copy /y "%~dp0start_nllb.bat" "%DIR%\" >nul
cd /d "%DIR%"
if not exist venv\Scripts\python.exe python -m venv venv || (echo Python not found & pause & exit /b 1)
venv\Scripts\python.exe -m pip install --upgrade pip
venv\Scripts\python.exe -m pip install torch --index-url %TORCH_INDEX% || (echo PyTorch install failed & pause & exit /b 1)
venv\Scripts\python.exe -m pip install transformers sentencepiece accelerate huggingface_hub || (echo install failed & pause & exit /b 1)
venv\Scripts\python.exe -c "import torch; print('GPU:', torch.cuda.get_device_name(0) if torch.cuda.is_available() else 'none (CPU)')"
echo.
echo Installed in %DIR%. Start the server with %DIR%\start_nllb.bat
echo (the first start downloads the model: 3.3B = 17.6 GB, distilled-1.3B = 5.5 GB, distilled-600M = 2.5 GB)
pause
