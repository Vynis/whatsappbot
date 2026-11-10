# 🤠 Xerife Board Games - Bot WhatsApp & Painel de Atendimento

Sistema completo de atendimento receptivo (inbound) e gestão para a **Xerife Board Games** (locadora de jogos de tabuleiro em Aparecida de Goiânia/GO), integrando **Evolution API v2** (WhatsApp via Baileys), backend em **ASP.NET Core 8**, banco de dados **MySQL 8.0** e painel de controle em **Angular 18**.

---

## 🏗 Arquitetura do Projeto

```text
whatsapp_bot/
├── docker-compose.yml              # Evolution API v2, MySQL (3307) e Redis 7
├── setup-instance.ps1              # Script de inicialização da instância e Webhook
├── docker/
│   └── mysql-init/init.sql         # Inicialização dos schemas (evolution_db e xerifebot_db)
├── XerifeBot.sln                   # Solution do .NET 8
├── XerifeBot.Api/                  # Backend ASP.NET Core 8 Web API
│   ├── Controllers/
│   │   ├── WhatsAppWebhookController.cs  # Webhook assíncrono com trava de handover
│   │   └── AdminController.cs            # Endpoints REST para o painel de controle
│   ├── Data/
│   │   └── AppDbContext.cs               # Contexto EF Core e Seed de 6 jogos
│   ├── Models/                           # CustomerSession, BoardGame, MessageLog
│   ├── Dtos/                             # DTOs de Webhook, Envio e Admin
│   ├── Services/
│   │   ├── EvolutionApiService.cs        # Delays humanizados (2 a 4s) e anti-falso-positivo
│   │   └── BotFlowService.cs             # Árvore do menu (1, 2, 3, 4, 9, fallback silencioso)
│   └── Migrations/                       # Migrations Code-First do EF Core
└── xerife-bot-ui/                  # Frontend Angular (Standalone Components)
    └── src/app/
        ├── app.component.html            # Dashboard com abas de Atendimento e Catálogo
        ├── app.component.ts              # Regras de filtro, alternância e sincronização
        └── services/api.service.ts       # Comunicação HTTP com a API C#
```

---

## 🚀 Guia de Execução Passo a Passo

### 1. Subir a Infraestrutura Docker

Certifique-se de que o Docker Desktop está em execução e execute:

```powershell
docker compose up -d
```

Este comando iniciará:
- **`mysql_db`**: MySQL 8.0 na porta externa `3307` (banco de dados do Bot C# e Catálogo `xerifebot_db`).
- **`redis_evo`**: Redis 7 com persistência AOF para cache da Evolution.
- **`postgres_evo`**: PostgreSQL 15 (banco nativo oficial da Evolution API v2).
- **`evolution-api`**: Evolution API v2.2.2 na porta `8080`.

Para checar se os contêineres estão saudáveis:
```powershell
docker compose ps
```

---

### 2. Configurar a Instância do WhatsApp

Execute o script de automação no PowerShell:

```powershell
.\setup-instance.ps1
```

O script realizará:
1. Verificação de status da Evolution API.
2. Criação automática da instância `XerifeAtendimento` com `rejectCall: true`.
3. Configuração do Webhook para `http://host.docker.internal:5000/api/whatsapp/webhook` escutando o evento `MESSAGES_UPSERT`.
4. Exibição da URL para leitura do **QR Code**:
   👉 `http://localhost:8080/instance/connect/XerifeAtendimento`

> Abra o WhatsApp no celular: **Menu > Aparelhos Conectados > Conectar um aparelho** e leia o QR Code.

---

### 3. Iniciar o Backend (.NET 8 Web API)

Navegue até a pasta da API:

```powershell
cd XerifeBot.Api
```

#### Rodar as Migrations do EF Core (Opcional, pois a API executa `EnsureCreated` no startup):
```powershell
dotnet ef database update
```

#### Iniciar a API:
```powershell
dotnet run
```

A API estará rodando em `http://localhost:5000` (Swagger disponível em `http://localhost:5000/swagger`).

---

### 4. Iniciar o Painel de Controle (Angular)

Abra outro terminal e navegue até o frontend:

```powershell
cd xerife-bot-ui
```

Instale as dependências e inicie o servidor de desenvolvimento:

```powershell
npm install
npm start
```

Acesse o painel no navegador:
👉 **`http://localhost:4200`**

---

## 🧠 Recursos Críticos Implementados

### 1. Human Handover Inteligente (Trava de Atendimento)
- **Detecção via Celular (`fromMe == true`)**: Se o dono da Xerife responder o cliente manualmente pelo próprio aplicativo do WhatsApp, o sistema detecta que a mensagem não foi gerada pelo Bot e pausa o robô para aquele cliente por **12 horas** (`BotPausedUntil = DateTime.UtcNow.AddHours(12)`).
- **Opção 9**: Ao digitar `9` no menu ("Falar com o Xerife"), o cliente é avisado e o robô fica pausado automaticamente.
- **Reativação**: O bot pode ser reativado com 1 clique no painel Angular ou se o cliente digitar `#bot` ou `menu` a qualquer momento.

### 2. Anti-Banimento e Delays Humanizados
- Cada envio de mensagem calcula um delay pseudoaleatório entre **2000ms e 4000ms**, transmitido na propriedade `delay` da Evolution API para simular digitação natural ("digitando...").
- Um cache em memória (`IMemoryCache`) armazena os envios recentes do robô para evitar falsos positivos na captura de eventos `fromMe`.

### 3. Fallback Silencioso
- Se o cliente enviar mensagens aleatórias que não correspondam às saudações ou opções numéricas do menu, o bot não envia mensagens repetitivas de "comando inválido", mantendo a conversa limpa.

### 4. Disponibilidade do Catálogo em Tempo Real
- Na aba **Disponibilidade Rápida do Catálogo** do Angular, o interruptor (switch) atualiza o banco MySQL na hora.
- Quando um jogo é marcado como "Alugado", ele é omitido automaticamente da listagem imediata do WhatsApp enviada aos clientes.

---

## 📸 Módulo Omnichannel Instagram (@xerifebg) - Meta Graph API v21.0

O sistema expande o atendimento gratuito e nativo para o Instagram oficial da loja (**@xerifebg**), sem dependência de plataformas pagas como ManyChat.

### 1. Endpoints do Webhook do Instagram
- **`GET /api/instagram/webhook`**: Endpoint oficial de handshake e verificação da Meta. Valida `hub.mode == subscribe` e o `hub.verify_token` configurado, retornando o `hub.challenge` em texto puro.
- **`POST /api/instagram/webhook`**: Retorna `HTTP 200 OK` de imediato e processa os eventos em background assíncrono via `IServiceScopeFactory`:
  - **Direct Messages (`messaging`)**:
    - Detecção de eco (`is_echo == true`): caso o atendente humano responda pelo app do Instagram no celular, o bot detecta e pausa as respostas automáticas por 12 horas.
    - Se o cliente enviar mensagem, a IA do Xerife responde descontraída com sugestões do catálogo e regras da loja.
    - Se a IA responder com a tag `[PAUSAR_BOT]`, a tag é removida, a pausa de 12h é gravada e a mensagem limpa é entregue no Direct.
  - **Comentários nos Posts (`changes` -> `field == "comments"`)**:
    - Comentários da própria conta `@xerifebg` são ignorados.
    - A IA gera uma resposta pública empolgada com emojis (🤠🎲) convidando a olhar o Direct.
    - O recurso oficial **Private Replies** (`SendPrivateReplyToCommenterAsync`) abre automaticamente a conversa no Direct do seguidor com o cupom **VEMPRAXERIFE**.
    - Gravação de auditoria na tabela `InstagramCommentLogs`.

### 2. Ice Breakers e Boas-Vindas
Configurados via `POST /me/messenger_profile`:
1. "🎲 Quero sugestões de jogos!"
2. "🔎 Estou procurando um jogo específico"
3. "🎟️ Quero meu cupom de 1ª locação (VEMPRAXERIFE)"

### 3. Worker Experimental para Novos Seguidores (`INewFollowerWelcomeService`)
- Desacoplado via interface e desativado por padrão (`EnableExperimentalFollowerPoller: false`) no `appsettings.json` para proteger contra bloqueios de taxa da Meta.
- Prioriza o fluxo oficial recomendado pela Meta: **Ice Breakers** e gatilho **Comment-to-DM**.

### 4. Painel Angular Omnichannel
- **Aba "Atendimentos"**: Distinção visual por canal: **🟢 WhatsApp** e **📸 Instagram Direct**, permitindo pausar, reativar ou encerrar conversas com 1 clique.
- **Aba "Comentários do Instagram"**: Listagem dos comentários recentes nos posts, resposta dada pela IA, status do Direct com cupom e botão para reconfigurar Ice Breakers.
