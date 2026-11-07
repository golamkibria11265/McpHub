@echo off
setlocal
title McpHub - Telegram logout
cd /d "%~dp0"
set DOTNET_ENVIRONMENT=Development
set DOTNET_NOLOGO=1
set DOTNET_CLI_TELEMETRY_OPTOUT=1

cls
echo ============================================
echo   McpHub - sign out of Telegram
echo ============================================
echo.
echo Stop the McpHub server first (Ctrl+C in its window, or Stop in Visual Studio).
echo This ends the session on Telegram and deletes the local session file.
echo.
pause

dotnet run --project tools\McpHub.Telegram.Login --verbosity quiet -- logout src\McpHub.Host
set "result=%errorlevel%"

echo.
if "%result%"=="0" (
    echo Done.
) else (
    echo Logout failed with code %result%. Read the message above.
)
echo.
pause
exit /b %result%
