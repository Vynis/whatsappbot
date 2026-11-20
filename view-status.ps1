# ==============================================================================
# Atualizacao do container e Recriacao Limpa da Instancia - Kora AI
# ==============================================================================

param(
    [string]$InstanceName = "KoraAtendimento",
    [string]$ApiKey = "Kora_Token_Secreto_2026",
    [string]$BaseUrl = "http://localhost:8080"
)

$headers = @{
    "apikey"       = $ApiKey
    "Content-Type" = "application/json"
}

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "[1/4] Atualizando container da Evolution API..." -ForegroundColor Yellow
Write-Host "==========================================================" -ForegroundColor Cyan
docker compose up -d --force-recreate evolution-api

Write-Host "`n[2/4] Aguardando Evolution API inicializar..." -ForegroundColor Gray
$online = $false
$retries = 0
while (-not $online -and $retries -lt 30) {
    $retries++
    try {
        $res = Invoke-RestMethod -Uri "$BaseUrl/" -Method Get -TimeoutSec 2 -ErrorAction Stop
        $online = $true
        Write-Host "[OK] Evolution API pronta!" -ForegroundColor Green
    }
    catch {
        Start-Sleep -Seconds 2
    }
}

Write-Host "`n[3/4] Removendo sessao anterior e criando instancia limpa..." -ForegroundColor Yellow
try {
    Invoke-RestMethod -Uri "$BaseUrl/instance/delete/$InstanceName" -Method Delete -Headers $headers -ErrorAction SilentlyContinue | Out-Null
    Start-Sleep -Seconds 2
} catch {}

$createBody = @{
    instanceName = $InstanceName
    token        = $ApiKey
    qrcode       = $true
    integration  = "WHATSAPP-BAILEYS"
} | ConvertTo-Json

$qrBase64 = $null

try {
    $create = Invoke-RestMethod -Uri "$BaseUrl/instance/create" -Method Post -Headers $headers -Body $createBody -ErrorAction Stop
    Write-Host "[OK] Nova instancia criada com sucesso!" -ForegroundColor Green

    if ($create.qrcode.base64) {
        $qrBase64 = $create.qrcode.base64
    }
    elseif ($create.base64) {
        $qrBase64 = $create.base64
    }
}
catch {
    Write-Host "Erro na criacao: $($_.Exception.Message)" -ForegroundColor Red
}

Write-Host "`n[4/4] Verificando conexao e logs..." -ForegroundColor Yellow
$poll = 0
while (-not $qrBase64 -and $poll -lt 6) {
    $poll++
    Start-Sleep -Seconds 2
    try {
        $conn = Invoke-RestMethod -Uri "$BaseUrl/instance/connect/$InstanceName" -Method Get -Headers $headers -ErrorAction Stop
        if ($conn.base64) {
            $qrBase64 = $conn.base64
            break
        }
        elseif ($conn.qrcode.base64) {
            $qrBase64 = $conn.qrcode.base64
            break
        }
    } catch {}
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
    Write-Host "`nLogs do container Docker:" -ForegroundColor Gray
    docker logs kora_evolution_api --tail 30
}
