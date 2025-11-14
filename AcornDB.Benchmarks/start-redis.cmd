@echo off
REM Start Redis for AcornDB Benchmarks (Windows)
REM This script starts a Redis container for running cache comparison benchmarks

setlocal enabledelayedexpansion

set CONTAINER_NAME=redis-acorndb-benchmark
set REDIS_PORT=6379

echo.
echo 🌰 AcornDB Redis Benchmark Setup
echo =================================
echo.

REM Check if Docker is installed
docker --version >nul 2>&1
if errorlevel 1 (
    echo ❌ Error: Docker is not installed
    echo    Please install Docker Desktop: https://docs.docker.com/desktop/install/windows-install/
    exit /b 1
)

REM Check if container already exists
docker ps -a --format "{{.Names}}" | findstr /x "%CONTAINER_NAME%" >nul 2>&1
if errorlevel 1 (
    echo 🚀 Starting new Redis container...
    docker run -d --name %CONTAINER_NAME% -p %REDIS_PORT%:6379 redis:7-alpine
    if errorlevel 1 (
        echo ❌ Failed to start Redis container
        exit /b 1
    )
    echo ✅ Redis started on port %REDIS_PORT%
) else (
    echo 📦 Container '%CONTAINER_NAME%' already exists

    REM Check if it's running
    docker ps --format "{{.Names}}" | findstr /x "%CONTAINER_NAME%" >nul 2>&1
    if errorlevel 1 (
        echo 🔄 Starting existing container...
        docker start %CONTAINER_NAME%
        echo ✅ Redis started on port %REDIS_PORT%
    ) else (
        echo ✅ Redis is already running on port %REDIS_PORT%
    )
)

REM Wait for Redis to be ready
echo.
echo ⏳ Waiting for Redis to be ready...
timeout /t 2 /nobreak >nul

REM Test connection
docker exec %CONTAINER_NAME% redis-cli ping | findstr "PONG" >nul 2>&1
if errorlevel 1 (
    echo ❌ Redis is not responding. Check logs:
    echo    docker logs %CONTAINER_NAME%
    exit /b 1
)

echo ✅ Redis is ready and responding to PING

echo.
echo 🎉 Redis is ready for benchmarks!
echo.
echo 📊 Run benchmarks with:
echo    cd AcornDB.Benchmarks
echo    dotnet run redis
echo.
echo 🛑 Stop Redis with:
echo    docker stop %CONTAINER_NAME%
echo.
echo 🗑️  Remove container with:
echo    docker rm %CONTAINER_NAME%
echo.

endlocal
