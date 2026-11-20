# ==============================================================================
# Conexao Limpa do WhatsApp sem Loop Interrompido - Kora AI
# ==============================================================================

param (
    [string]$InstanceName = "KoraAtendimento",
    [string]$ApiKey = "Kora_Token_Secreto_2026",
    [string]$BaseUrl = "http://localhost:8080",
    [string]$WebhookUrl = "http://kora_api:5001/api/whatsapp/webhook"
)

$headers = @{
    "apikey"       = $ApiKey
    "Content-Type" = "application/json"
}

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "[1/5] Atualizando container para Evolution API v2.3.7..." -ForegroundColor Yellow
Write-Host "==========================================================" -ForegroundColor Cyan
docker compose up -d evolution-api

Write-Host "Aguardando Evolution API inicializar..." -ForegroundColor Gray
$online = $false
$retries = 0
while (-not $online -and $retries -lt 30) {
    $retries++
    try {
        $res = Invoke-RestMethod -Uri "$BaseUrl/" -Method Get -TimeoutSec 2 -ErrorAction Stop
        $online = $true
        Write-Host "[OK] Evolution API online!" -ForegroundColor Green
    }
    catch {
        Start-Sleep -Seconds 2
    }
}

Write-Host "`n[2/5] Limpando cache do Redis..." -ForegroundColor Yellow
docker exec kora_redis redis-cli flushall | Out-Null
Write-Host "[OK] Cache do Redis limpo!" -ForegroundColor Green

Write-Host "`n[3/5] Removendo instancia anterior..." -ForegroundColor Yellow
try {
    Invoke-RestMethod -Uri "$BaseUrl/instance/delete/$InstanceName" -Method Delete -Headers $headers -ErrorAction SilentlyContinue | Out-Null
    Start-Sleep -Seconds 2
    Write-Host "[OK] Instancia antiga removida." -ForegroundColor Green
} catch {}

Write-Host "`n[4/5] Criando instancia com qrcode ativado..." -ForegroundColor Yellow
$createPayload = @{
    instanceName    = $InstanceName
    token           = $ApiKey
    qrcode          = $true
    integration     = "WHATSAPP-BAILEYS"
    readStatus      = $false
    syncFullHistory = $false
} | ConvertTo-Json

$qrBase64 = $null
$pairingCode = $null

try {
    $createRes = Invoke-RestMethod -Uri "$BaseUrl/instance/create" -Method Post -Headers $headers -Body $createPayload -ErrorAction Stop
    Write-Host "[OK] Instancia criada!" -ForegroundColor Green
    
    if ($createRes.qrcode.base64) {
        $qrBase64 = $createRes.qrcode.base64
    }
    elseif ($createRes.base64) {
        $qrBase64 = $createRes.base64
    }
    if ($createRes.qrcode.pairingCode) {
        $pairingCode = $createRes.qrcode.pairingCode
    }
}
catch {
    Write-Host "Erro na criacao: $($_.Exception.Message)" -ForegroundColor Red
}

# Configurar Webhook e Settings
try {
    $webhookPayload = @{
        webhook = @{
            enabled  = $true
            url      = $WebhookUrl
            byEvents = $false
            base64   = $false
            events   = @("MESSAGES_UPSERT")
        }
    } | ConvertTo-Json -Depth 5
    Invoke-RestMethod -Uri "$BaseUrl/webhook/set/$InstanceName" -Method Post -Headers $headers -Body $webhookPayload -ErrorAction SilentlyContinue | Out-Null
    
    $settingsPayload = @{
        rejectCall   = $true
        msgCall      = "Atendimento somente por texto!"
        groupsIgnore = $true
        alwaysOnline = $true
        readMessages = $true
    } | ConvertTo-Json
    Invoke-RestMethod -Uri "$BaseUrl/settings/set/$InstanceName" -Method Post -Headers $headers -Body $settingsPayload -ErrorAction SilentlyContinue | Out-Null
    Write-Host "[OK] Webhook e regras de privacidade configurados!" -ForegroundColor Green
} catch {}

Write-Host "`n[5/5] Aguardando o WhatsApp emitir o QR Code (sem interromper o socket)..." -ForegroundColor Yellow
Write-Host "Aguardando 10 segundos para o handshake com a Meta..." -ForegroundColor Gray
Start-Sleep -Seconds 10

if (-not $qrBase64) {
    try {
        $conn = Invoke-RestMethod -Uri "$BaseUrl/instance/connect/$InstanceName" -Method Get -Headers $headers -ErrorAction Stop
        if ($conn.base64) {
            $qrBase64 = $conn.base64
        }
        elseif ($conn.qrcode.base64) {
            $qrBase64 = $conn.qrcode.base64
        }
        if ($conn.pairingCode) {
            $pairingCode = $conn.pairingCode
        }
        elseif ($conn.code) {
            $pairingCode = $conn.code
        }
    }
    catch {
        Write-Host "Aviso ao consultar /connect: $($_.Exception.Message)" -ForegroundColor Gray
    }
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
  <title>QR Code WhatsApp - Kora AI</title>
  <style>
    body { font-family: 'Segoe UI', system-ui, sans-serif; display: flex; align-items: center; justify-content: center; min-height: 100vh; margin: 0; background: linear-gradient(135deg, #0f172a 0%, #1e1b4b 100%); color: #fff; }
    .card { background: #ffffff; color: #1e293b; padding: 2.2rem; border-radius: 20px; text-align: center; box-shadow: 0 20px 40px rgba(0,0,0,0.4); max-width: 440px; border-top: 5px solid #4f46e5; }
    .logo { font-size: 2.5rem; margin-bottom: 0.2rem; }
    h2 { margin: 0 0 0.5rem 0; color: #4f46e5; font-weight: 700; font-size: 1.5rem; }
    p { color: #64748b; font-size: 0.95rem; margin-bottom: 1.2rem; }
    .qr-container { display: inline-block; padding: 12px; background: #fff; border: 2px dashed #4f46e5; border-radius: 12px; margin-bottom: 1.2rem; }
    img { width: 260px; height: 260px; display: block; border-radius: 6px; }
    ol { text-align: left; padding-left: 1.2rem; font-size: 0.9rem; color: #334155; line-height: 1.6; }
    .footer { margin-top: 1.2rem; font-size: 0.8rem; color: #94a3b8; }
  </style>
</head>
<body>
  <div class="card">
    <div class="logo">⚡🤖</div>
    <h2>Kora AI</h2>
    <p>Aponte a câmera do WhatsApp para conectar:</p>
    <div class="qr-container">
      <img src="$qrSrc" alt="QR Code WhatsApp" />
    </div>
    <ol>
      <li>Abra o <b>WhatsApp</b> no celular</li>
      <li>Toque no <b>Menu</b> (ou Configurações)</li>
      <li>Selecione <b>Aparelhos conectados</b></li>
      <li>Toque em <b>Conectar um aparelho</b></li>
    </ol>
    <div class="footer">Instância: $InstanceName • Evolution API v2</div>
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
    Write-Host "`nResposta da Evolution API:" -ForegroundColor Gray
    try {
        $statusCheck = Invoke-RestMethod -Uri "$BaseUrl/instance/connect/$InstanceName" -Method Get -Headers $headers
        $statusCheck | ConvertTo-Json -Depth 5
    } catch {}
    
    Write-Host "`nLogs recentes do container:" -ForegroundColor Gray
    docker logs kora_evolution_api --tail 15
}
