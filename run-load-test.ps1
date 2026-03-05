# 1. Ensure API + gateway are running (docker-compose)
# 2. Run NBomber load test against the gateway

$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

Write-Host "Starting API and gateway (docker compose)..." -ForegroundColor Cyan
docker compose up -d --build
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "Waiting for gateway to be ready..." -ForegroundColor Cyan
Start-Sleep -Seconds 5

Write-Host "Running NBomber load test (profile: low)..." -ForegroundColor Cyan
$env:LOAD_BASE_URL = "http://localhost:8080"
$env:APIM_SUBSCRIPTION_KEY = "dev-key"
$env:LOAD_PROFILE = "low"
Set-Location "tests\LoadDemoApi.LoadTests"
dotnet run -c Release
Set-Location $PSScriptRoot

Write-Host "Done. Reports in tests\LoadDemoApi.LoadTests\nbomber_report\" -ForegroundColor Green
