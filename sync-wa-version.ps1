# ==============================================================================
# Obtencao Dinamica da Versao Oficial do WhatsApp Web diretamente da Meta
# ==============================================================================

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "Consultando versao oficial atual do WhatsApp Web na Meta..." -ForegroundColor Yellow
Write-Host "==========================================================" -ForegroundColor Cyan

$currentVersion = $null

try {
    $res = Invoke-RestMethod -Uri "https://web.whatsapp.com/check-update?version=1&platform=web" -Method Get -TimeoutSec 5
    $currentVersion = $res.currentVersion
    Write-Host "[OK] Versao oficial retornada pela Meta: $currentVersion" -ForegroundColor Green
}
catch {
    Write-Host "Aviso na consulta direta: $($_.Exception.Message)" -ForegroundColor Gray
}

if (-not $currentVersion) {
    try {
        $res0 = Invoke-RestMethod -Uri "https://web.whatsapp.com/check-update?version=0&platform=web" -Method Get -TimeoutSec 5
        $currentVersion = $res0.currentVersion
        Write-Host "[OK] Versao oficial (v0): $currentVersion" -ForegroundColor Green
    }
    catch {}
}

if ($currentVersion) {
    Write-Host "`nAtualizando docker-compose.yml com CONFIG_SESSION_PHONE_VERSION=$currentVersion..." -ForegroundColor Cyan
    
    $composeFile = Join-Path $PSScriptRoot "docker-compose.yml"
    $composeContent = Get-Content $composeFile -Raw
    
    if ($composeContent -match 'CONFIG_SESSION_PHONE_VERSION=.*') {
        $composeContent = $composeContent -replace 'CONFIG_SESSION_PHONE_VERSION=.*', "CONFIG_SESSION_PHONE_VERSION=$currentVersion"
    }
    else {
        $composeContent = $composeContent -replace '- DEL_INSTANCE=true', "- DEL_INSTANCE=true`n      - CONFIG_SESSION_PHONE_VERSION=$currentVersion"
    }

    [System.IO.File]::WriteAllText($composeFile, $composeContent, [System.Text.Encoding]::UTF8)
    Write-Host "[OK] docker-compose.yml atualizado com a versao oficial!" -ForegroundColor Green

    Write-Host "`nRecriando container xerife_evolution_api com a versao da Meta..." -ForegroundColor Yellow
    docker compose up -d --force-recreate evolution-api

    Write-Host "`nAguardando inicializacao da Evolution API..." -ForegroundColor Gray
    $online = $false
    $retries = 0
    while (-not $online -and $retries -lt 25) {
        $retries++
        try {
            $test = Invoke-RestMethod -Uri "http://localhost:8080/" -Method Get -TimeoutSec 2 -ErrorAction Stop
            $online = $true
            Write-Host "[OK] Evolution API pronta!" -ForegroundColor Green
        }
        catch {
            Start-Sleep -Seconds 2
        }
    }

    Write-Host "`nRemovendo sessao antiga para aplicar a versao atualizada..." -ForegroundColor Gray
    try {
        Invoke-RestMethod -Uri "http://localhost:8080/instance/delete/XerifeAtendimento" -Method Delete -Headers @{ "apikey" = "XerifeBG_Token_Secreto_2026" } -ErrorAction SilentlyContinue | Out-Null
        Start-Sleep -Seconds 2
    } catch {}

    Write-Host "`nExecutando geracao limpa do QR Code..." -ForegroundColor Cyan
    & "$PSScriptRoot\setup-instance.ps1"
}
else {
    Write-Host "[!] Nao foi possivel consultar o endpoint da Meta automaticamente." -ForegroundColor Yellow
}
