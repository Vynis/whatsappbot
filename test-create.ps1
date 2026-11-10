$baseUrl = "http://localhost:8080"
$apiKey = "XerifeBG_Token_Secreto_2026"

$headers = @{
    "apikey"       = $apiKey
    "Content-Type" = "application/json"
}

$payload = @{
    instanceName = "XerifeAtendimento"
    token        = $apiKey
    qrcode       = $true
    integration  = "WHATSAPP-BAILEYS"
} | ConvertTo-Json

try {
    $resp = Invoke-RestMethod -Uri "$baseUrl/instance/create" -Method Post -Headers $headers -Body $payload
    Write-Host "Sucesso na criacao!" -ForegroundColor Green
    $resp | ConvertTo-Json -Depth 5
}
catch {
    $stream = $_.Exception.Response.GetResponseStream()
    $reader = [System.IO.StreamReader]::new($stream)
    $errorBody = $reader.ReadToEnd()
    Write-Host "Detalhe exato do erro 400:" -ForegroundColor Red
    Write-Host $errorBody -ForegroundColor Yellow
}
