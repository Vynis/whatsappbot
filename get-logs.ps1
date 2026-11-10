# Coleta de logs detalhados dos containers Docker
Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "Coletando logs da Evolution API..." -ForegroundColor Yellow
Write-Host "==========================================================" -ForegroundColor Cyan

docker logs xerife_evolution_api --tail 80
