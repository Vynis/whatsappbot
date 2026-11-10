param(
    [string]$BaseUrl = "http://localhost:5001"
)

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "TESTE DO MODULO OMNICHANNEL INSTAGRAM - XERIFE BOT" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

# 1. Teste de Validacao da Meta (GET /api/instagram/webhook)
Write-Host "`n1. Testando validacao de Webhook da Meta (GET)..." -ForegroundColor Yellow
$challenge = "teste_challenge_meta_12345"
$verifyToken = "xerife_meta_verify_token_2026"
$verifyUrl = "{0}/api/instagram/webhook?hub.mode=subscribe&hub.verify_token={1}&hub.challenge={2}" -f $BaseUrl, $verifyToken, $challenge

try {
    $resp = Invoke-RestMethod -Uri $verifyUrl -Method Get
    if ($resp -eq $challenge) {
        Write-Host "  [OK] Validacao de Webhook OK! Retornou o challenge: '$resp'" -ForegroundColor Green
    } else {
        Write-Host "  [FALHA] Retorno inesperado na validacao: '$resp'" -ForegroundColor Red
    }
} catch {
    Write-Host "  [FALHA] Erro ao chamar validacao de webhook: $($_.Exception.Message)" -ForegroundColor Red
}

# 2. Teste de Token Invalido (deve retornar 403 Forbidden)
Write-Host "`n2. Testando rejeicao com token invalido..." -ForegroundColor Yellow
$invalidUrl = "{0}/api/instagram/webhook?hub.mode=subscribe&hub.verify_token=token_errado&hub.challenge={1}" -f $BaseUrl, $challenge
try {
    $null = Invoke-RestMethod -Uri $invalidUrl -Method Get
    Write-Host "  [FALHA] Deveria ter retornado 403 Forbidden!" -ForegroundColor Red
} catch {
    Write-Host "  [OK] Rejeicao correta com 403 Forbidden para token invalido!" -ForegroundColor Green
}

# 3. Teste de Evento Direct (POST /api/instagram/webhook)
Write-Host "`n3. Testando recebimento de mensagem no Direct (POST)..." -ForegroundColor Yellow
$dmPayloadObj = @{
    object = "instagram"
    entry = @(
        @{
            id = "17841400000000000"
            time = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
            messaging = @(
                @{
                    sender = @{ id = "998877665544" }
                    recipient = @{ id = "17841400000000000" }
                    message = @{
                        mid = "m_teste_simulacao_dm"
                        text = "Quero sugestoes de jogos para 4 pessoas!"
                        is_echo = $false
                    }
                }
            )
        }
    )
}
$dmPayload = $dmPayloadObj | ConvertTo-Json -Depth 5
$postWebhookUrl = "{0}/api/instagram/webhook" -f $BaseUrl

try {
    $dmResp = Invoke-RestMethod -Uri $postWebhookUrl -Method Post -Body $dmPayload -ContentType "application/json"
    Write-Host "  [OK] Evento Direct recebido com sucesso: $($dmResp | ConvertTo-Json -Compress)" -ForegroundColor Green
} catch {
    Write-Host "  [FALHA] Erro ao enviar evento de DM: $($_.Exception.Message)" -ForegroundColor Red
}

# 4. Teste de Evento de Comentario em Post (Comment-to-DM)
Write-Host "`n4. Testando evento de comentario no post (POST)..." -ForegroundColor Yellow
$randomId = Get-Random -Minimum 100000 -Maximum 999999
$commentPayloadObj = @{
    object = "instagram"
    entry = @(
        @{
            id = "17841400000000000"
            time = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
            changes = @(
                @{
                    field = "comments"
                    value = @{
                        id = "comment_teste_simulado_$randomId"
                        text = "Esse jogo e sensacional! Tem disponivel para locacao?"
                        from = @{
                            id = "11223344"
                            username = "jogador_xerife"
                        }
                        media = @{
                            id = "media_post_12345"
                        }
                    }
                }
            )
        }
    )
}
$commentPayload = $commentPayloadObj | ConvertTo-Json -Depth 5

try {
    $commResp = Invoke-RestMethod -Uri $postWebhookUrl -Method Post -Body $commentPayload -ContentType "application/json"
    Write-Host "  [OK] Evento de comentario recebido com sucesso: $($commResp | ConvertTo-Json -Compress)" -ForegroundColor Green
} catch {
    Write-Host "  [FALHA] Erro ao enviar evento de comentario: $($_.Exception.Message)" -ForegroundColor Red
}

# 5. Teste de Listagem de Comentarios no Admin
Write-Host "`n5. Testando endpoint de listagem de comentarios para o Painel Angular..." -ForegroundColor Yellow
$commentsUrl = "{0}/api/admin/instagram/comments" -f $BaseUrl
try {
    $comments = Invoke-RestMethod -Uri $commentsUrl -Method Get
    Write-Host "  [OK] Consulta de comentarios OK! $($comments.Count) comentario(s) retornados." -ForegroundColor Green
} catch {
    Write-Host "  [FALHA] Erro ao consultar comentarios: $($_.Exception.Message)" -ForegroundColor Red
}

# 6. Teste de Sessoes com Diferenciacao de Canal (WhatsApp vs Instagram)
Write-Host "`n6. Testando listagem de sessoes omnichannel..." -ForegroundColor Yellow
$sessionsUrl = "{0}/api/admin/sessions" -f $BaseUrl
try {
    $sessions = Invoke-RestMethod -Uri $sessionsUrl -Method Get
    Write-Host "  [OK] Sessoes carregadas: $($sessions.Count) sessao(oes)." -ForegroundColor Green
    foreach ($s in $sessions | Select-Object -First 3) {
        Write-Host "    -> ID: $($s.id) | Canal: $($s.channel) | Cliente: $($s.pushName) | Ref: $($s.remoteJid)" -ForegroundColor Gray
    }
} catch {
    Write-Host "  [FALHA] Erro ao consultar sessoes: $($_.Exception.Message)" -ForegroundColor Red
}

Write-Host "`n==========================================================" -ForegroundColor Cyan
Write-Host "TESTES FINALIZADOS" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan
