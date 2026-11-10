# ==============================================================================
# Correcao automatica do schema da Evolution API v2 (Prisma / MySQL)
# ==============================================================================

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "[1/4] Adicionando coluna 'headers' na tabela Webhook..." -ForegroundColor Yellow
Write-Host "==========================================================" -ForegroundColor Cyan

# 1. Adicionar diretamente no MySQL (sintaxe padrao MySQL 8.0)
$sql = "ALTER TABLE Webhook ADD COLUMN headers JSON NULL;"
docker exec -i xerife_mysql mysql -uroot -prootpassword evolution_db -e $sql 2>$null
if ($LASTEXITCODE -eq 0) {
    Write-Host "[OK] Coluna 'headers' adicionada com sucesso no MySQL!" -ForegroundColor Green
} else {
    Write-Host "Coluna 'headers' ja existia ou foi atualizada." -ForegroundColor Gray
}

# 2. Executar db push para garantir que nenhuma outra coluna esteja faltando
Write-Host "`n[2/4] Sincronizando modelo completo do Prisma com o MySQL..." -ForegroundColor Yellow
docker exec xerife_evolution_api npx prisma db push --schema ./prisma/mysql-schema.prisma --accept-data-loss

# 3. Conferir estrutura da tabela Webhook
Write-Host "`n[3/4] Validando colunas da tabela Webhook:" -ForegroundColor Gray
docker exec -i xerife_mysql mysql -uroot -prootpassword evolution_db -e "DESCRIBE Webhook;"

# 4. Reiniciar a Evolution API
Write-Host "`n[4/4] Reiniciando Evolution API..." -ForegroundColor Yellow
docker restart xerife_evolution_api | Out-Null
Write-Host "[OK] Container reiniciado!" -ForegroundColor Green

Write-Host "`nAguardando 8 segundos para a Evolution API inicializar..." -ForegroundColor Gray
Start-Sleep -Seconds 8

Write-Host "`n==========================================================" -ForegroundColor Cyan
Write-Host "Executando setup da instancia e gerando QR Code..." -ForegroundColor Yellow
Write-Host "==========================================================" -ForegroundColor Cyan

& "$PSScriptRoot\setup-instance.ps1"
