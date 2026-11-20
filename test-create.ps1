param(
    [string]$InstanceName = "KoraAtendimento",
    [string]$ApiKey = "Kora_Token_Secreto_2026",
    [string]$BaseUrl = "http://localhost:8080"
)

$headers = @{
    "apikey"       = $ApiKey
    "Content-Type" = "application/json"
}

$payload = @{
    instanceName = $InstanceName
    token        = $ApiKey
    qrcode       = $true
    integration  = "WHATSAPP-BAILEYS"
} | ConvertTo-Json

try {
    $resp = Invoke-RestMethod -Uri "$BaseUrl/instance/create" -Method Post -Headers $headers -Body $payload
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
