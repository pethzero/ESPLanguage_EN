@echo off
rem Builds ESPLanguage_EN.exe (needs the .NET 8 SDK) and a release zip to share.
rem The exe is self-contained: the people you give it to do NOT need to install .NET.
cd /d "%~dp0"
set VER=1.1.0
dotnet publish src\ESPLanguage_EN.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:Version=%VER%
if errorlevel 1 (echo. & echo BUILD FAILED & pause & exit /b 1)
copy /y "src\bin\Release\net8.0-windows\win-x64\publish\ESPLanguage_EN.exe" "%~dp0ESPLanguage_EN.exe" >nul

rem release\ESPLanguage_EN_v<ver>.zip = exe + README + glossary (no ini: it can hold your API key)
if exist release\pkg rmdir /s /q release\pkg
mkdir release\pkg\ESPLanguage_EN
copy /y ESPLanguage_EN.exe release\pkg\ESPLanguage_EN\ >nul
copy /y README.md release\pkg\ESPLanguage_EN\ >nul
copy /y glossary.txt release\pkg\ESPLanguage_EN\ >nul
rem retry: antivirus often locks a new exe for a few seconds
powershell -NoProfile -Command "$z='release\ESPLanguage_EN_v%VER%.zip'; for($i=0;$i -lt 10;$i++){ try { Remove-Item $z -ErrorAction SilentlyContinue; Compress-Archive -Path 'release\pkg\ESPLanguage_EN' -DestinationPath $z -Force -ErrorAction Stop; exit 0 } catch { Start-Sleep 2 } }; Write-Host 'ZIP FAILED'; exit 1"
if errorlevel 1 (echo ZIP FAILED & if "%1"=="" pause & exit /b 1)
rmdir /s /q release\pkg
echo.
echo Built: %~dp0ESPLanguage_EN.exe
echo Release: %~dp0release\ESPLanguage_EN_v%VER%.zip
if "%1"=="" pause
