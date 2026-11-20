# Coleta de logs detalhados dos containers Docker - Kora AI
param(
    [string]$Service = "kora_evolution_api"
)

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "Coletando logs de $Service..." -ForegroundColor Yellow
Write-Host "==========================================================" -ForegroundColor Cyan

docker logs $Service --tail 80
