@echo off
SETLOCAL EnableDelayedExpansion

REM Compose project name — keep this fixed so we always reuse the same
REM Postgres data volume and container (agent-control-panel-db).
set COMPOSE_PROJECT=sharableaiskillbuilder_ag

echo ============================================
echo   Agent Control Panel - run ^& test
echo ============================================
echo.

REM --- 0. Check .env exists ---------------------------------------------------
if not exist ".env" (
    echo [!] No .env file found.
    echo     Copy the template and add your keys first:
    echo         copy .env.example .env
    echo     then set ANTHROPIC_API_KEY and VOYAGE_API_KEY in .env
    echo.
    pause
    exit /b 1
)

REM --- 1. Check Docker is running --------------------------------------------
echo Checking Docker status...
docker ps >nul 2>&1
if %ERRORLEVEL% neq 0 (
    echo [!] Docker is not running. Please start Docker Desktop and try again.
    pause
    exit /b 1
)

REM --- 2. Start Postgres (pgvector) ------------------------------------------
echo Starting PostgreSQL (pgvector) via Docker Compose...
docker compose -p %COMPOSE_PROJECT% up -d
if %ERRORLEVEL% neq 0 (
    echo [!] docker compose failed. See the error above.
    pause
    exit /b 1
)

echo Waiting for PostgreSQL to be ready...
:check_db
docker exec agent-control-panel-db pg_isready -U postgres >nul 2>&1
if %ERRORLEVEL% neq 0 (
    timeout /t 1 >nul
    goto check_db
)
echo PostgreSQL is ready.
echo.

REM --- 3. Start the app -------------------------------------------------------
echo Checking if the application is already running...
tasklist /FI "IMAGENAME eq AgentControlPanel.exe" 2>NUL | find /I /N "AgentControlPanel.exe">NUL
if %ERRORLEVEL% equ 0 (
    echo Application is already running.
) else (
    echo Starting the application in a new window...
    start "Agent Control Panel" dotnet run --project .
)

echo.
echo ============================================
echo   App starting. Open in your browser:
echo     http://localhost:5067
echo     https://localhost:7186
echo.
echo   First run applies DB migrations automatically.
echo   Try: Knowledge Base -^> Create  (verifies Voyage key)
echo        Agents -^> Create  (tick "Enable knowledge base")
echo        Agents -^> Test Agent  (chat + activity log)
echo ============================================
echo.
echo Press any key to close this window (the app keeps running)...
pause >nul
