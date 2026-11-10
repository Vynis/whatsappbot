# ==============================================================================
# Setup Instance: XerifeAtendimento (Evolution API v2)
# Xerife Board Games - Automacao WhatsApp
# ==============================================================================

$baseUrl = "http://localhost:8080"
$apiKey = "XerifeBG_Token_Secreto_2026"
$instanceName = "XerifeAtendimento"
$webhookUrl = "http://host.docker.internal:5000/api/whatsapp/webhook"

$headers = @{
    "apikey"       = $apiKey
    "Content-Type" = "application/json"
}

# Payload padrao compativel com Evolution API v2
$createPayload = @{
    instanceName    = $instanceName
    token           = $apiKey
    qrcode          = $true
    integration     = "WHATSAPP-BAILEYS"
    readStatus      = $false
    syncFullHistory = $false
} | ConvertTo-Json

$settingsPayload = @{
    rejectCall   = $true
    msgCall      = "Atendimento somente por texto!"
    groupsIgnore = $true
    alwaysOnline = $true
    readMessages = $true
} | ConvertTo-Json
    readMessages = $true
} | ConvertTo-Json

$webhookPayload = @{
    webhook = @{
        enabled  = $true
        url      = $webhookUrl
        byEvents = $false
        base64   = $false
        events   = @("MESSAGES_UPSERT")
    }
} | ConvertTo-Json -Depth 5

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "[Xerife Board Games] Inicializacao de Instancia WhatsApp" -ForegroundColor Yellow
Write-Host "==========================================================" -ForegroundColor Cyan

# 1. Aguardar Evolution API ficar online
Write-Host "`n[1/4] Verificando se a Evolution API esta online em $baseUrl..." -ForegroundColor Gray
$retryCount = 0
$maxRetries = 20
$apiOnline = $false

while (-not $apiOnline -and $retryCount -lt $maxRetries) {
    try {
        $response = Invoke-RestMethod -Uri "$baseUrl/" -Method Get -TimeoutSec 3 -ErrorAction Stop
        $apiOnline = $true
        Write-Host "[OK] Evolution API esta online e respondendo!" -ForegroundColor Green
    }
    catch {
        $retryCount++
        Write-Host "Aguardando Evolution API inicializar (tentativa $retryCount de $maxRetries)..." -ForegroundColor Yellow
        Start-Sleep -Seconds 3
    }
}

if (-not $apiOnline) {
    Write-Host "[ERRO] Nao foi possivel conectar na Evolution API em $baseUrl." -ForegroundColor Red
    exit 1
}

# 2. Verificar se a instancia ja existe ou precisa ser criada
Write-Host "`n[2/4] Verificando status da instancia '$instanceName'..." -ForegroundColor Gray
$instanceExists = $false
$qrBase64 = $null

try {
    $instances = Invoke-RestMethod -Uri "$baseUrl/instance/fetchInstances" -Method Get -Headers $headers -ErrorAction Stop
    foreach ($inst in $instances) {
        $foundName = $inst.name
        if (-not $foundName) { $foundName = $inst.instance.instanceName }
        if (-not $foundName) { $foundName = $inst.instanceName }

        if ($foundName -eq $instanceName) {
            $instanceExists = $true
            break
        }
    }
}
catch {}

if ($instanceExists) {
    Write-Host "[OK] Instancia '$instanceName' ja existe no sistema." -ForegroundColor Green
}
else {
    Write-Host "Criando instancia '$instanceName'..." -ForegroundColor Cyan
    try {
        $createResponse = Invoke-RestMethod -Uri "$baseUrl/instance/create" -Method Post -Headers $headers -Body $createPayload -ErrorAction Stop
        Write-Host "[OK] Instancia '$instanceName' criada com sucesso!" -ForegroundColor Green
        $instanceExists = $true
    }
    catch {
        Write-Host "[Aviso] Falha ao criar instancia: $($_.Exception.Message)" -ForegroundColor Red
    }
}

# Configurar rejeicao automatica de chamadas e configuracoes adicionais
try {
    Invoke-RestMethod -Uri "$baseUrl/settings/set/$instanceName" -Method Post -Headers $headers -Body $settingsPayload -ErrorAction SilentlyContinue | Out-Null
    Write-Host "[OK] Configuracoes de chamada e privacidade aplicadas!" -ForegroundColor Green
}
catch {}

# 3. Configurar e Confirmar Webhook
Write-Host "`n[3/4] Configurando Webhook para $webhookUrl..." -ForegroundColor Gray
try {
    Invoke-RestMethod -Uri "$baseUrl/webhook/set/$instanceName" -Method Post -Headers $headers -Body $webhookPayload -ErrorAction Stop | Out-Null
    Write-Host "[OK] Webhook registrado com sucesso!" -ForegroundColor Green
}
catch {
    Write-Host "Aviso no webhook: $($_.Exception.Message)" -ForegroundColor Gray
}

# 4. Obter status da conexao e Gerar Visualizacao do QR Code
Write-Host "`n[4/4] Verificando status da conexao..." -ForegroundColor Gray
try {
    $stateResponse = Invoke-RestMethod -Uri "$baseUrl/instance/connectionState/$instanceName" -Method Get -Headers $headers -ErrorAction Stop
    $state = $stateResponse.instance.state
    Write-Host "Status da conexao da instancia '$instanceName': $state" -ForegroundColor Cyan

    if ($state -eq "open") {
        Write-Host "`n==========================================================" -ForegroundColor Green
        Write-Host "[OK] WhatsApp ja esta conectado e pronto para uso!" -ForegroundColor Green
        Write-Host "==========================================================" -ForegroundColor Green
    }
    else {
        Write-Host "Buscando imagem do QR Code autenticado..." -ForegroundColor Gray
        $qrRetries = 0
        $maxQrRetries = 10

        while (-not $qrBase64 -and $qrRetries -lt $maxQrRetries) {
            $qrRetries++
            Write-Host "Aguardando emissao do QR Code (tentativa $qrRetries de $maxQrRetries)..." -ForegroundColor Gray
            try {
                $connectResponse = Invoke-RestMethod -Uri "$baseUrl/instance/connect/$instanceName" -Method Get -Headers $headers -ErrorAction Stop
                
                if ($connectResponse.base64) {
                    $qrBase64 = $connectResponse.base64
                }
                elseif ($connectResponse.qrcode.base64) {
                    $qrBase64 = $connectResponse.qrcode.base64
                }

                if ($qrBase64) {
                    break
                }
            }
            catch {}

            Start-Sleep -Seconds 3
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
            Write-Host "[!] Nao foi possivel obter o QR Code diretamente ainda." -ForegroundColor Yellow
        }
    }
}
catch {
    Write-Host "Status da conexao: $($_.Exception.Message)" -ForegroundColor Yellow
}

Write-Host "`nConcluido!" -ForegroundColor Green
