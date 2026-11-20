param (
    [string]$InstanceName = "KoraAtendimento",
    [string]$ApiKey = "Kora_Token_Secreto_2026",
    [string]$BaseUrl = "http://localhost:8080"
)

$headers = @{
    "apikey"       = $ApiKey
    "Content-Type" = "application/json"
}

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "[1/2] Resposta de /instance/connect/${InstanceName}:" -ForegroundColor Yellow
Write-Host "==========================================================" -ForegroundColor Cyan
try {
    $resp = Invoke-RestMethod -Uri "$BaseUrl/instance/connect/$InstanceName" -Method Get -Headers $headers
    $resp | ConvertTo-Json -Depth 5
}
catch {
    $stream = $_.Exception.Response.GetResponseStream()
    $reader = [System.IO.StreamReader]::new($stream)
    Write-Host $reader.ReadToEnd() -ForegroundColor Red
}

Write-Host "`n==========================================================" -ForegroundColor Cyan
Write-Host "[2/2] Ultimas 30 linhas do log do container Docker:" -ForegroundColor Yellow
Write-Host "==========================================================" -ForegroundColor Cyan
docker logs kora_evolution_api --tail 30
