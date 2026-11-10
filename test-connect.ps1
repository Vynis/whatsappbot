$baseUrl = "http://localhost:8080"
$apiKey = "XerifeBG_Token_Secreto_2026"
$instanceName = "XerifeAtendimento"

$headers = @{
    "apikey"       = $apiKey
    "Content-Type" = "application/json"
}

Write-Host "Consultando conexao e QR Code de $instanceName..." -ForegroundColor Cyan

$retry = 0
$maxRetries = 10
$qrFound = $false

while (-not $qrFound -and $retry -lt $maxRetries) {
    $retry++
    Write-Host "Tentativa $retry de $maxRetries..." -ForegroundColor Gray
    try {
        $resp = Invoke-RestMethod -Uri "$baseUrl/instance/connect/$instanceName" -Method Get -Headers $headers
        
        $b64 = $null
        if ($resp.base64) { $b64 = $resp.base64 }
        elseif ($resp.qrcode.base64) { $b64 = $resp.qrcode.base64 }

        if ($b64) {
            $qrFound = $true
            Write-Host "[OK] QR Code em Base64 obtido com sucesso!" -ForegroundColor Green

            $qrSrc = $b64
            if (-not $qrSrc.StartsWith("data:image")) {
                $qrSrc = "data:image/png;base64,$b64"
            }

            $htmlPath = Join-Path $PSScriptRoot "qrcode.html"
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
            [System.IO.File]::WriteAllText($htmlPath, $html, [System.Text.Encoding]::UTF8)
            Write-Host "Abrindo visualizacao do QR Code no navegador..." -ForegroundColor Cyan
            Start-Process $htmlPath
            break
        }
        else {
            Write-Host "Aguardando geracao... Resposta: $($resp | ConvertTo-Json -Compress)" -ForegroundColor Gray
        }
    }
    catch {
        Write-Host "Aviso: $($_.Exception.Message)" -ForegroundColor Gray
    }

    Start-Sleep -Seconds 3
}

if (-not $qrFound) {
    Write-Host "[!] Nao foi possivel obter o Base64 apos $maxRetries tentativas." -ForegroundColor Yellow
}
