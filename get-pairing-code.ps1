param (
    [string]$PhoneNumber
)

$baseUrl = "http://localhost:8080"
$apiKey = "XerifeBG_Token_Secreto_2026"
$instanceName = "XerifeAtendimento"

$headers = @{
    "apikey"       = $apiKey
    "Content-Type" = "application/json"
}

if (-not $PhoneNumber) {
    Write-Host "==========================================================" -ForegroundColor Cyan
    Write-Host "Pareamento por Codigo de 8 digitos (Pairing Code)" -ForegroundColor Yellow
    Write-Host "==========================================================" -ForegroundColor Cyan
    $PhoneNumber = Read-Host "Informe o numero do WhatsApp (ex: 62991333856)"
}

$cleanNumber = $PhoneNumber -replace '\D',''

# Se o usuario digitou sem o DDI 55 do Brasil, adiciona automaticamente
if ($cleanNumber.Length -eq 10 -or $cleanNumber.Length -eq 11) {
    $cleanNumber = "55$cleanNumber"
    Write-Host "DDI 55 adicionado automaticamente: $cleanNumber" -ForegroundColor Gray
}

Write-Host "`n[1/3] Tentando obter codigo via GET /instance/connect ($cleanNumber)..." -ForegroundColor Cyan
$code = $null

try {
    $resp = Invoke-RestMethod -Uri "$baseUrl/instance/connect/$instanceName`?number=$cleanNumber" -Method Get -Headers $headers -ErrorAction Stop
    if ($resp.code) { $code = $resp.code }
    elseif ($resp.pairingCode) { $code = $resp.pairingCode }
    if (-not $code) {
        Write-Host "Resposta: $($resp | ConvertTo-Json -Compress)" -ForegroundColor Gray
    }
}
catch {
    Write-Host "Aviso no metodo GET: $($_.Exception.Message)" -ForegroundColor Gray
}

if (-not $code) {
    Write-Host "`n[2/3] Tentando via POST /instance/connect ($cleanNumber)..." -ForegroundColor Cyan
    $body = @{ number = $cleanNumber } | ConvertTo-Json
    try {
        $respPost = Invoke-RestMethod -Uri "$baseUrl/instance/connect/$instanceName" -Method Post -Headers $headers -Body $body -ErrorAction Stop
        if ($respPost.code) { $code = $respPost.code }
        elseif ($respPost.pairingCode) { $code = $respPost.pairingCode }
        if (-not $code) {
            Write-Host "Resposta POST: $($respPost | ConvertTo-Json -Compress)" -ForegroundColor Gray
        }
    }
    catch {
        Write-Host "Aviso no metodo POST: $($_.Exception.Message)" -ForegroundColor Gray
    }
}

if (-not $code) {
    Write-Host "`n[3/3] Tentando via POST /instance/pair ($cleanNumber)..." -ForegroundColor Cyan
    $pairBody = @{ phone = $cleanNumber } | ConvertTo-Json
    try {
        $respPair = Invoke-RestMethod -Uri "$baseUrl/instance/pair" -Method Post -Headers $headers -Body $pairBody -ErrorAction Stop
        if ($respPair.code) { $code = $respPair.code }
        elseif ($respPair.pairingCode) { $code = $respPair.pairingCode }
        if (-not $code) {
            Write-Host "Resposta Pair: $($respPair | ConvertTo-Json -Compress)" -ForegroundColor Gray
        }
    }
    catch {
        Write-Host "Aviso no metodo Pair: $($_.Exception.Message)" -ForegroundColor Gray
    }
}

if ($code) {
    Write-Host "`n==========================================================" -ForegroundColor Green
    Write-Host "  CODIGO DE PAREAMENTO: $code  " -ForegroundColor Yellow -BackgroundColor DarkGreen
    Write-Host "==========================================================" -ForegroundColor Green
    Write-Host "`nComo conectar no WhatsApp do celular:" -ForegroundColor White
    Write-Host "1. Abra o WhatsApp no celular ($cleanNumber)" -ForegroundColor Gray
    Write-Host "2. Menu (3 pontinhos ou Configuracoes) -> Aparelhos Conectados" -ForegroundColor Gray
    Write-Host "3. Toque em 'Conectar um aparelho'" -ForegroundColor Gray
    Write-Host "4. No rodape da camera, toque em: 'Conectar com numero de telefone'" -ForegroundColor Cyan
    Write-Host "5. Digite o codigo acima: $code" -ForegroundColor Yellow
    Write-Host "`nPronto! A conexao sera estabelecida imediatamente!" -ForegroundColor Green
}
else {
    Write-Host "`n[!] O WhatsApp nao gerou o pairing code nesta tentativa." -ForegroundColor Yellow
    Write-Host "Verifique os logs recentes do container:" -ForegroundColor Gray
    docker logs xerife_evolution_api --tail 15
}
