# Starts McpHub for development and restarts it automatically whenever a source file changes.
# Usage (from the repo root):  .\dev.ps1
# Afterwards in Claude Desktop: start a NEW chat (or toggle the connector off/on) to pick up changed tools.
Get-Process -Name McpHub.Host -ErrorAction SilentlyContinue | Stop-Process -Force
$env:ASPNETCORE_ENVIRONMENT = "Development"
dotnet watch run --project src\McpHub.Host --no-hot-reload
