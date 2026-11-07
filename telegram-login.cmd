@echo off
cd /d "%~dp0"
set DOTNET_ENVIRONMENT=Development
echo Signing in to Telegram for McpHub. Type the phone number and the login code here.
dotnet run --project tools\McpHub.Telegram.Login -- login src\McpHub.Host
echo.
pause
