# ==============================================================================
# Reinicializacao Completa e Limpa da Sessao do WhatsApp
# ==============================================================================

$baseUrl = "http://localhost:8080"
$apiKey = "XerifeBG_Token_Secreto_2026"
$instanceName = "XerifeAtendimento"
$webhookUrl = "http://host.docker.internal:5000/api/whatsapp/webhook"

$headers = @{
    "apikey"       = $apiKey
    "Content-Type" = "application/json"
}

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "[1/5] Garantindo que os containers estao ativos..." -ForegroundColor Yellow
Write-Host "==========================================================" -ForegroundColor Cyan
docker compose up -d

Write-Host "`nAguardando Evolution API inicializar completamente..." -ForegroundColor Gray
$online = $false
$retries = 0
while (-not $online -and $retries -lt 30) {
    $retries++
    try {
        $res = Invoke-RestMethod -Uri "$baseUrl/" -Method Get -TimeoutSec 2 -ErrorAction Stop
        $online = $true
        Write-Host "[OK] Evolution API online e respondendo!" -ForegroundColor Green
    }
    catch {
        Write-Host "Inicializando NestJS (tentativa $retries)..." -ForegroundColor Gray
        Start-Sleep -Seconds 2
    }
}

if (-not $online) {
    Write-Host "[ERRO] Evolution API nao respondeu a tempo." -ForegroundColor Red
    Write-Host "`nLogs do container Docker (xerife_evolution_api):" -ForegroundColor Yellow
    docker logs xerife_evolution_api --tail 30
    exit 1
}

Write-Host "`n[2/5] Removendo sessao anterior presa..." -ForegroundColor Yellow
try {
    Invoke-RestMethod -Uri "$baseUrl/instance/delete/$instanceName" -Method Delete -Headers $headers -ErrorAction Stop | Out-Null
    Write-Host "[OK] Sessao antiga '$instanceName' deletada com sucesso." -ForegroundColor Green
    Start-Sleep -Seconds 2
}
catch {
    Write-Host "Instancia nao existia ou ja foi removida." -ForegroundColor Gray
}

Write-Host "`n[3/5] Criando instancia limpa com Baileys nativo..." -ForegroundColor Yellow
$createBody = @{
    instanceName    = $instanceName
    token           = $apiKey
    qrcode          = $true
    integration     = "WHATSAPP-BAILEYS"
    readStatus      = $false
    syncFullHistory = $false
} | ConvertTo-Json

$qrBase64 = $null

try {
    $createResp = Invoke-RestMethod -Uri "$baseUrl/instance/create" -Method Post -Headers $headers -Body $createBody -ErrorAction Stop
    Write-Host "[OK] Instancia '$instanceName' criada com sucesso!" -ForegroundColor Green

    if ($createResp.qrcode.base64) {
        $qrBase64 = $createResp.qrcode.base64
    }
    elseif ($createResp.base64) {
        $qrBase64 = $createResp.base64
    }

    # Aplicar configuracoes de rejeicao de chamada e privacidade
    $settingsPayload = @{
        rejectCall   = $true
        msgCall      = "Atendimento somente por texto!"
        groupsIgnore = $true
        alwaysOnline = $true
        readMessages = $true
    } | ConvertTo-Json
        alwaysOnline = $true
        readMessages = $true
    } | ConvertTo-Json

    try {
        Invoke-RestMethod -Uri "$baseUrl/settings/set/$instanceName" -Method Post -Headers $headers -Body $settingsPayload -ErrorAction SilentlyContinue | Out-Null
        Write-Host "[OK] Configuracoes de chamada e privacidade aplicadas!" -ForegroundColor Green
    }
    catch {}
}
catch {
    Write-Host "[Aviso] Falha ao criar instancia: $($_.Exception.Message)" -ForegroundColor Red
}

Write-Host "`n[4/5] Registrando Webhook..." -ForegroundColor Yellow
$webhookPayload = @{
    webhook = @{
        enabled  = $true
        url      = $webhookUrl
        byEvents = $false
        base64   = $false
        events   = @("MESSAGES_UPSERT")
    }
} | ConvertTo-Json -Depth 5

try {
    Invoke-RestMethod -Uri "$baseUrl/webhook/set/$instanceName" -Method Post -Headers $headers -Body $webhookPayload -ErrorAction Stop | Out-Null
    Write-Host "[OK] Webhook registrado com sucesso!" -ForegroundColor Green
}
catch {
    Write-Host "Aviso no webhook: $($_.Exception.Message)" -ForegroundColor Gray
}

Write-Host "`n[5/5] Obtendo QR Code do WhatsApp..." -ForegroundColor Yellow
$pollRetries = 0
$maxPoll = 12

while (-not $qrBase64 -and $pollRetries -lt $maxPoll) {
    $pollRetries++
    Write-Host "Consultando emissao do QR Code (tentativa $pollRetries de $maxPoll)..." -ForegroundColor Gray
    Start-Sleep -Seconds 2

    try {
        $conn = Invoke-RestMethod -Uri "$baseUrl/instance/connect/$instanceName" -Method Get -Headers $headers -ErrorAction Stop
        if ($conn.base64) {
            $qrBase64 = $conn.base64
            break
        }
        elseif ($conn.qrcode.base64) {
            $qrBase64 = $conn.qrcode.base64
            break
        }
    }
    catch {}
}

if ($qrBase64) {
    $qrSrc = $qrBase64
    if (-not $qrSrc.StartsWith("data:image")) {
        $qrSrc = "data:image/png;base64,$qrBase64"
    }

    $htmlFile = Join-Path $PSScriptRoot "qrcode.html"
    $html = @"
<!DOCTYPE html>
<html lang="pt-BR">
<head>
  <meta charset="UTF-8">
  <title>QR Code WhatsApp - Xerife Board Games</title>
  <style>
    body { font-family: 'Segoe UI', system-ui, sans-serif; display: flex; align-items: center; justify-content: center; min-height: 100vh; margin: 0; background: linear-gradient(135deg, #2b1d0c 0%, #150a04 100%); color: #fff; }
    .card { background: #ffffff; color: #212529; padding: 2.2rem; border-radius: 20px; text-align: center; box-shadow: 0 15px 35px rgba(0,0,0,0.5); max-width: 440px; border-top: 5px solid #8b4513; }
    .logo { font-size: 2.5rem; margin-bottom: 0.2rem; }
    h2 { margin: 0 0 0.5rem 0; color: #8b4513; font-weight: 700; font-size: 1.5rem; }
    p { color: #6c757d; font-size: 0.95rem; margin-bottom: 1.2rem; }
    .qr-container { display: inline-block; padding: 12px; background: #fff; border: 2px dashed #8b4513; border-radius: 12px; margin-bottom: 1.2rem; }
    img { width: 260px; height: 260px; display: block; border-radius: 6px; }
    ol { text-align: left; padding-left: 1.2rem; font-size: 0.9rem; color: #495057; line-height: 1.6; margin-bottom: 0; }
    .footer { margin-top: 1.2rem; font-size: 0.8rem; color: #adb5bd; }
  </style>
</head>
<body>
  <div class="card">
    <div class="logo">🤠🎲</div>
    <h2>Xerife Board Games</h2>
    <p>Aponte a câmera do WhatsApp para conectar:</p>
    <div class="qr-container">
      <img src="$qrSrc" alt="QR Code WhatsApp" />
    </div>
    <ol>
      <li>Abra o <b>WhatsApp</b> no celular da loja</li>
      <li>Toque no <b>Menu</b> (ou Configurações)</li>
      <li>Selecione <b>Aparelhos conectados</b></li>
      <li>Toque em <b>Conectar um aparelho</b></li>
    </ol>
    <div class="footer">Instância: XerifeAtendimento • Evolution API v2</div>
  </div>
</body>
</html>
"@
    [System.IO.File]::WriteAllText($htmlFile, $html, [System.Text.Encoding]::UTF8)
    Write-Host "`n==========================================================" -ForegroundColor Green
    Write-Host "[OK] QR Code obtido com sucesso!" -ForegroundColor Green
    Write-Host "Abrindo visualizacao do QR Code no seu navegador..." -ForegroundColor Cyan
    Write-Host "==========================================================" -ForegroundColor Green
    Start-Process $htmlFile
}
else {
    Write-Host "`n[!] O QR Code ainda nao foi emitido." -ForegroundColor Yellow
    Write-Host "Logs recentes:" -ForegroundColor Gray
    docker logs xerife_evolution_api --tail 15
}
