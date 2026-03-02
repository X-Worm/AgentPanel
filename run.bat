@echo off
SETLOCAL EnableDelayedExpansion

echo Checking Docker status...
docker ps >nul 2>&1
if %ERRORLEVEL% neq 0 (
    echo Docker is not running. Please start Docker Desktop and try again.
    pause
    exit /b 1
)

echo Starting PostgreSQL via Docker Compose...
docker-compose up -d

echo Waiting for PostgreSQL to be ready...
:check_db
docker exec agent-control-panel-db pg_isready -U postgres >nul 2>&1
if %ERRORLEVEL% neq 0 (
    timeout /t 1 >nul
    goto check_db
)
echo PostgreSQL is ready.

echo Checking if application is already running...
tasklist /FI "IMAGENAME eq AgentControlPanel.exe" 2>NUL | find /I /N "AgentControlPanel.exe">NUL
if %ERRORLEVEL% equ 0 (
    echo Application is already running.
) else (
    echo Starting the application...
    start "Agent Control Panel" dotnet run --project .
)

echo Done.
pause
