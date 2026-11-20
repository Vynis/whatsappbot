# ⚡ Kora AI - Plataforma SaaS Omnichannel de Atendimento Inteligente

Sistema completo de atendimento omnichannel receptivo (inbound) e gestão multi-tenant, integrando **Evolution API v2** (WhatsApp via Baileys), **Instagram Graph API** (Meta v21.0), **Google Gemini / OpenAI**, backend em **ASP.NET Core 8**, banco de dados **MySQL 8.0** e painel de controle em **Angular 18**.

---

## 🏗 Arquitetura do Projeto

```text
whatsapp_bot/
├── docker-compose.yml              # Evolution API v2, MySQL (3307), Redis 7, Postgres, API e UI
├── setup-instance.ps1              # Script de inicialização da instância e Webhook WhatsApp
├── docker/
│   └── mysql-init/init.sql         # Inicialização dos schemas (kora_db e evolution_db)
├── Kora.sln                        # Solution do .NET 8
├── Kora.Api/                       # Backend ASP.NET Core 8 Web API
│   ├── Controllers/
│   │   ├── WhatsAppWebhookController.cs  # Webhook assíncrono com trava de handover
│   │   ├── InstagramWebhookController.cs # Webhook do Instagram Direct e Comment-to-DM
│   │   ├── AdminController.cs            # Endpoints REST administrativos e de configurações
│   │   └── AuthController.cs             # Autenticação JWT multi-tenant
│   ├── Data/
│   │   └── AppDbContext.cs               # Contexto EF Core e Seed inicial
│   ├── Models/                           # CustomerSession, Tenant, User, MessageLog
│   ├── Dtos/                             # DTOs de Webhook, Envio e Admin
│   ├── Services/
│   │   ├── EvolutionApiService.cs        # Integração Baileys com delays humanizados
│   │   ├── BotFlowService.cs             # Fluxo de opções e handover
│   │   ├── AiBotService.cs               # Motor de IA com prompts parametrizados por tenant
│   │   └── InstagramGraphService.cs      # Integração Meta Graph API
│   └── Migrations/                       # Migrations Code-First do EF Core
└── kora-ui/                        # Frontend Angular 18 (Standalone Components)
    └── src/app/
        ├── app.component.html            # Dashboard Omnichannel, QR Code e Configurações
        ├── app.component.ts              # Regras de filtro, alternância e sincronização
        └── services/api.service.ts       # Comunicação HTTP com a API C#
```

---

## 🚀 Guia de Execução com Docker

### 1. Subir Toda a Stack Docker

Com o Docker Desktop em execução:

```powershell
docker compose up -d --build
```

Os seguintes serviços serão inicializados:
- **`kora_ui`**: Interface Angular 18 em `http://localhost:4202`
- **`kora_api`**: Backend .NET 8 em `http://localhost:5001` (Swagger em `http://localhost:5001/swagger`)
- **`kora_evolution_api`**: Evolution API v2 em `http://localhost:8080`
- **`kora_mysql`**: MySQL 8.0 na porta `3307` (`kora_db`)
- **`kora_postgres`**: PostgreSQL 15 (banco nativo da Evolution API)
- **`kora_redis`**: Redis 7 com persistência AOF

---

## 🔑 Credenciais Padrão de Acesso

- **URL do Painel:** `http://localhost:4202`
- **Administrador Kora AI:** `admin@kora.ai` / `AdminKora2026!`
- **Administrador Legado:** `admin@xerife.com.br` / `AdminXerife2026!`

---

## 🧠 Principais Recursos

1. **Human Handover Inteligente:**
   - Detecção automática de respostas enviadas pelo celular do operador (`fromMe == true`), pausando o robô por 12 horas.
   - Opção 9 no WhatsApp transfere diretamente para o atendimento humano.
   - Reativação com 1 clique no painel ou por comando do cliente (`#bot` ou `menu`).

2. **Inteligência Artificial Personalizável por Tenant:**
   - Prompts de sistema totalmente customizáveis pelo painel administrativo.
   - Suporte a provedores de IA (Google Gemini, OpenAI).

3. **Omnichannel Nativo (WhatsApp + Instagram Direct):**
   - Atendimento unificado no painel para ambos os canais.
   - Comment-to-DM no Instagram: respostas automáticas para comentários com abertura de conversa no Direct.
