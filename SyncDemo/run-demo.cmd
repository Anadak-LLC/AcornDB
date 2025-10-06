@echo off
echo ========================================
echo    AcornDB P2P Sync Demo Launcher
echo    File System-Based Synchronization
echo ========================================
echo.

if "%1"=="1" goto process1
if "%1"=="2" goto process2
if "%1"=="clean" goto clean

echo Usage:
echo   run-demo.cmd 1      - Start Process 1 (Desktop)
echo   run-demo.cmd 2      - Start Process 2 (Mobile)
echo   run-demo.cmd clean  - Clean all data directories
echo.
echo Open 2 terminals and run each process in separate windows
echo (no server required - syncs via shared file system!)
goto end

:process1
cd /d "%~dp0"
echo Starting Process 1 (Desktop)...
echo Local storage: data\process1
echo Sync hub: data\sync-hub
echo.
dotnet run -- 1
goto end

:process2
cd /d "%~dp0"
echo Starting Process 2 (Mobile)...
echo Local storage: data\process2
echo Sync hub: data\sync-hub
echo.
dotnet run -- 2
goto end

:clean
cd /d "%~dp0"
echo Cleaning data directories...
if exist data rmdir /s /q data
echo Done! All data cleared.
goto end

:end
