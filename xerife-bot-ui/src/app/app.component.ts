import { Component, OnInit, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ApiService } from './services/api.service';
import { CustomerSession } from './models/customer-session.model';
import { BoardGame } from './models/board-game.model';
import { DashboardStats } from './models/dashboard-stats.model';
import { WhatsAppStatus } from './models/whatsapp-status.model';
import { BotSettings, UploadCatalogResult, TestAiResponse } from './models/settings.model';
import {
  UserSummary,
  TenantSummary,
  TenantProfile,
  WhatsAppConnectResult
} from './models/auth.model';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './app.component.html',
  styleUrls: ['./app.component.css']
})
export class AppComponent implements OnInit, OnDestroy {
  // =========================================================================
  // Estado de Autenticação & Multi-Tenancy
  // =========================================================================
  isLoggedIn = false;
  authMode: 'login' | 'register' = 'login';
  currentUser: UserSummary | null = null;
  currentTenant: TenantSummary | null = null;

  loginEmail = 'admin@xerife.com.br';
  loginPassword = 'AdminXerife2026!';
  loginError = '';
  isLoggingIn = false;

  registerData = {
    companyName: '',
    fullName: '',
    email: '',
    password: '',
    businessType: 1, // 1: Locadora, 2: Loja, 3: Híbrido
    cityOrRegion: '',
    websiteUrl: ''
  };
  registerError = '';
  isRegistering = false;

  // =========================================================================
  // Navegação por Abas
  // =========================================================================
  activeTab: 'sessions' | 'whatsapp' | 'games' | 'settings' = 'sessions';

  sessions: CustomerSession[] = [];
  games: BoardGame[] = [];
  stats: DashboardStats | null = null;
  whatsAppStatus: WhatsAppStatus | null = null;
  profile: TenantProfile | null = null;
  backendOffline = false;

  isLoadingSessions = false;
  isLoadingGames = false;
  isUpdatingSessionId: number | null = null;
  isClosingSessionId: number | null = null;
  isUpdatingGameId: number | null = null;

  sessionSearch = '';
  sessionStatusFilter: 'all' | 'active' | 'paused' = 'all';
  sessionChannelFilter: 'all' | 'WhatsApp' | 'Instagram' = 'all';

  gameSearch = '';
  gameAvailabilityFilter: 'all' | 'available' | 'rented' = 'all';

  autoRefresh = true;
  private refreshInterval: any;

  // =========================================================================
  // Conexão Dinâmica WhatsApp (Evolution API v2) & QR Code
  // =========================================================================
  isConnectingWhatsApp = false;
  qrCodeBase64: string | null = null;
  whatsAppConnectMessage = '';
  whatsAppActionLoading = false;
  pairingCode: string | null = null;

  // =========================================================================
  // Gestão de Jogos (Modal Criar / Editar)
  // =========================================================================
  showGameModal = false;
  isEditingGame = false;
  editingGameId: number | null = null;
  gameFormData: Partial<BoardGame> = {
    name: '',
    category: 'Geral',
    minPlayers: 2,
    maxPlayers: 4,
    rentalPrice: 0,
    salePrice: null,
    aquisitionPrice: null,
    linkLudopedia: '',
    comments: '',
    isAvailable: true
  };
  isSavingGame = false;
  isDeletingGameId: number | null = null;

  // =========================================================================
  // Perfil da Empresa
  // =========================================================================
  isSavingProfile = false;
  profileSaveSuccess = false;

  // =========================================================================
  // Alertas de Atendimento Humano (Sons, Banner & Notificações Desktop)
  // =========================================================================
  soundEnabled = true;
  notificationsEnabled = false;
  knownPausedSessionIds: Set<number> = new Set<number>();
  dismissedAlertSessionIds: Set<number> = new Set<number>();
  activeHandoverAlert: CustomerSession | null = null;
  originalDocumentTitle = 'Central de Atendimento - Board Game Bot SaaS';

  // =========================================================================
  // Upload Manual de Catálogo
  // =========================================================================
  showUploadPanel = false;
  selectedFile: File | null = null;
  clearExistingOnUpload = true;
  isUploading = false;
  isClearing = false;
  uploadResult: UploadCatalogResult | null = null;

  // =========================================================================
  // Configurações & Inteligência Artificial
  // =========================================================================
  settings: BotSettings = {
    isBotActiveGlobally: true,
    isAiEnabled: false,
    aiProvider: 'gemini',
    aiApiKey: '',
    aiModel: 'gemini-flash-lite-latest',
    aiSystemPrompt: '',
    totalGamesInDb: 0
  };

  isTogglingGlobalBot = false;
  isLoadingSettings = false;
  isSavingSettings = false;
  settingsSaveSuccess = false;

  isTestingAi = false;
  testAiResult: TestAiResponse | null = null;
  testAiMessage = 'Olá! Apresente-se brevemente como o atendente virtual de jogos de tabuleiro.';
  showApiKey = false;

  constructor(private apiService: ApiService) {}

  ngOnInit(): void {
    if (typeof window !== 'undefined') {
      try {
        const savedSound = localStorage.getItem('xerife_sound_enabled');
        if (savedSound !== null) {
          this.soundEnabled = savedSound === 'true';
        }
      } catch (e) {}

      if ('Notification' in window) {
        this.notificationsEnabled = Notification.permission === 'granted';
      }

      if (this.apiService.isAuthenticated()) {
        this.isLoggedIn = true;
        this.currentUser = this.apiService.getCurrentUser();
        this.currentTenant = this.apiService.getCurrentTenant();
        this.loadAll();
        this.startAutoRefresh();
      }
    }

    if (typeof document !== 'undefined') {
      this.originalDocumentTitle = document.title || 'Central de Atendimento - Board Game Bot SaaS';
    }
  }

  ngOnDestroy(): void {
    this.stopAutoRefresh();
  }

  // =========================================================================
  // Ações de Autenticação (Login, Registro, Logout)
  // =========================================================================

  toggleAuthMode(): void {
    this.authMode = this.authMode === 'login' ? 'register' : 'login';
    this.loginError = '';
    this.registerError = '';
  }

  login(): void {
    if (!this.loginEmail || !this.loginPassword) {
      this.loginError = 'Preencha o e-mail e a senha.';
      return;
    }

    this.isLoggingIn = true;
    this.loginError = '';
    this.apiService.login({ email: this.loginEmail, password: this.loginPassword }).subscribe({
      next: (res) => {
        this.isLoggingIn = false;
        this.apiService.setAuth(res);
        this.isLoggedIn = true;
        this.currentUser = res.user;
        this.currentTenant = res.tenant;
        this.loadAll();
        this.startAutoRefresh();
      },
      error: (err) => {
        this.isLoggingIn = false;
        this.loginError = err?.error?.message || 'E-mail ou senha inválidos.';
      }
    });
  }

  register(): void {
    if (!this.registerData.companyName || !this.registerData.fullName || !this.registerData.email || !this.registerData.password) {
      this.registerError = 'Preencha todos os campos obrigatórios.';
      return;
    }

    this.isRegistering = true;
    this.registerError = '';
    this.apiService.register(this.registerData).subscribe({
      next: (res) => {
        this.isRegistering = false;
        this.apiService.setAuth(res);
        this.isLoggedIn = true;
        this.currentUser = res.user;
        this.currentTenant = res.tenant;
        this.activeTab = 'whatsapp'; // Redireciona logo para conectar o WhatsApp
        this.loadAll();
        this.startAutoRefresh();
      },
      error: (err) => {
        this.isRegistering = false;
        this.registerError = err?.error?.message || 'Erro ao realizar cadastro da empresa.';
      }
    });
  }

  logout(): void {
    this.apiService.clearAuth();
    this.isLoggedIn = false;
    this.currentUser = null;
    this.currentTenant = null;
    this.stopAutoRefresh();
    this.sessions = [];
    this.games = [];
    this.stats = null;
  }

  // =========================================================================
  // Carregamento de Dados
  // =========================================================================

  loadAll(): void {
    this.loadWhatsAppStatus();
    this.loadSessions();
    this.loadGames();
    this.loadStats();
    this.loadSettings();
    this.loadProfile();
  }

  loadProfile(): void {
    this.apiService.getProfile().subscribe({
      next: (p) => (this.profile = p),
      error: (err) => console.error('Erro ao carregar perfil:', err)
    });
  }

  saveProfile(): void {
    if (!this.profile) return;
    this.isSavingProfile = true;
    this.profileSaveSuccess = false;
    this.apiService.updateProfile(this.profile).subscribe({
      next: (updated) => {
        this.profile = updated;
        if (this.currentTenant) {
          this.currentTenant.name = updated.name;
          this.currentTenant.businessType = updated.businessType;
        }
        this.isSavingProfile = false;
        this.profileSaveSuccess = true;
        setTimeout(() => (this.profileSaveSuccess = false), 3500);
      },
      error: (err) => {
        this.isSavingProfile = false;
        console.error('Erro ao salvar perfil:', err);
        alert('Erro ao salvar informações da empresa.');
      }
    });
  }

  // =========================================================================
  // Conexão Dinâmica de WhatsApp & QR Code
  // =========================================================================

  requestWhatsAppConnect(): void {
    this.isConnectingWhatsApp = true;
    this.qrCodeBase64 = null;
    this.pairingCode = null;
    this.whatsAppConnectMessage = 'Solicitando QR Code à Evolution API...';

    this.apiService.connectWhatsApp().subscribe({
      next: (res: WhatsAppConnectResult) => {
        this.isConnectingWhatsApp = false;
        this.whatsAppConnectMessage = res.message || '';
        if (res.qrCodeBase64) {
          this.qrCodeBase64 = res.qrCodeBase64;
        }
        if (res.pairingCode) {
          this.pairingCode = res.pairingCode;
        }
        this.loadWhatsAppStatus();
      },
      error: (err) => {
        this.isConnectingWhatsApp = false;
        this.whatsAppConnectMessage = 'Falha ao solicitar conexão de WhatsApp. Verifique se a Evolution API está online.';
        console.error('Erro ao conectar WhatsApp:', err);
      }
    });
  }

  disconnectWhatsApp(): void {
    if (!confirm('Deseja realmente desconectar o WhatsApp desta conta?')) {
      return;
    }

    this.whatsAppActionLoading = true;
    this.apiService.disconnectWhatsApp().subscribe({
      next: (res) => {
        this.whatsAppActionLoading = false;
        this.qrCodeBase64 = null;
        this.pairingCode = null;
        this.loadWhatsAppStatus();
        alert(res.message);
      },
      error: (err) => {
        this.whatsAppActionLoading = false;
        alert('Erro ao desconectar WhatsApp.');
      }
    });
  }

  restartWhatsApp(): void {
    this.whatsAppActionLoading = true;
    this.apiService.restartWhatsApp().subscribe({
      next: (res) => {
        this.whatsAppActionLoading = false;
        this.loadWhatsAppStatus();
        alert(res.message);
      },
      error: (err) => {
        this.whatsAppActionLoading = false;
        alert('Erro ao reiniciar WhatsApp.');
      }
    });
  }

  // =========================================================================
  // Gestão de Jogos (Criar, Editar, Excluir)
  // =========================================================================

  openNewGameModal(): void {
    this.isEditingGame = false;
    this.editingGameId = null;
    this.gameFormData = {
      name: '',
      category: 'Geral',
      minPlayers: 2,
      maxPlayers: 4,
      rentalPrice: 0,
      salePrice: null,
      aquisitionPrice: null,
      linkLudopedia: '',
      comments: '',
      isAvailable: true
    };
    this.showGameModal = true;
  }

  openEditGameModal(game: BoardGame): void {
    this.isEditingGame = true;
    this.editingGameId = game.id;
    this.gameFormData = { ...game };
    this.showGameModal = true;
  }

  closeGameModal(): void {
    this.showGameModal = false;
    this.isEditingGame = false;
    this.editingGameId = null;
  }

  saveGame(): void {
    if (!this.gameFormData.name) {
      alert('O nome do jogo é obrigatório.');
      return;
    }

    this.isSavingGame = true;

    if (this.isEditingGame && this.editingGameId) {
      this.apiService.updateGame(this.editingGameId, this.gameFormData).subscribe({
        next: (updated) => {
          this.isSavingGame = false;
          const index = this.games.findIndex((g) => g.id === updated.id);
          if (index !== -1) {
            this.games[index] = updated;
          }
          this.closeGameModal();
          this.loadStats();
        },
        error: (err) => {
          this.isSavingGame = false;
          alert('Erro ao atualizar jogo.');
        }
      });
    } else {
      this.apiService.createGame(this.gameFormData).subscribe({
        next: (created) => {
          this.isSavingGame = false;
          this.games.unshift(created);
          this.closeGameModal();
          this.loadStats();
        },
        error: (err) => {
          this.isSavingGame = false;
          alert('Erro ao cadastrar novo jogo.');
        }
      });
    }
  }

  deleteGame(game: BoardGame): void {
    if (!confirm(`Deseja realmente excluir o jogo "${game.name}" do catálogo?`)) {
      return;
    }

    this.isDeletingGameId = game.id;
    this.apiService.deleteGame(game.id).subscribe({
      next: () => {
        this.games = this.games.filter((g) => g.id !== game.id);
        this.isDeletingGameId = null;
        this.loadStats();
      },
      error: (err) => {
        this.isDeletingGameId = null;
        alert('Erro ao excluir jogo.');
      }
    });
  }

  // =========================================================================
  // Restante das Funções Existentes
  // =========================================================================

  loadWhatsAppStatus(): void {
    this.apiService.getWhatsAppStatus().subscribe({
      next: (data) => (this.whatsAppStatus = data),
      error: (err) => console.error('Erro ao carregar status do WhatsApp:', err)
    });
  }

  loadStats(): void {
    this.apiService.getStats().subscribe({
      next: (data) => (this.stats = data),
      error: (err) => console.error('Erro ao carregar estatísticas:', err)
    });
  }

  loadSessions(): void {
    this.isLoadingSessions = true;
    this.apiService.getSessions().subscribe({
      next: (data) => {
        this.sessions = data;
        this.processHandoverAlerts(data);
        this.isLoadingSessions = false;
        this.backendOffline = false;
      },
      error: (err) => {
        this.isLoadingSessions = false;
        this.backendOffline = true;
      }
    });
  }

  processHandoverAlerts(currentSessions: CustomerSession[]): void {
    const pausedSessions = currentSessions.filter((s) => s.isCurrentlyPaused);

    if (typeof document !== 'undefined') {
      if (pausedSessions.length > 0) {
        document.title = `(${pausedSessions.length}) 🚨 ATENDIMENTO HUMANO`;
      } else {
        document.title = this.originalDocumentTitle;
      }
    }

    const newlyPaused = pausedSessions.find((s) => !this.knownPausedSessionIds.has(s.id));
    if (newlyPaused) {
      this.playAlertSound();
      this.triggerDesktopNotification(newlyPaused);
      if (!this.dismissedAlertSessionIds.has(newlyPaused.id)) {
        this.activeHandoverAlert = newlyPaused;
      }
    } else if (this.activeHandoverAlert) {
      const current = pausedSessions.find((s) => s.id === this.activeHandoverAlert?.id);
      if (!current) {
        this.activeHandoverAlert = null;
      }
    } else if (pausedSessions.length > 0 && !this.activeHandoverAlert) {
      this.activeHandoverAlert = pausedSessions.find((s) => !this.dismissedAlertSessionIds.has(s.id)) || null;
    }

    this.knownPausedSessionIds = new Set(pausedSessions.map((s) => s.id));
  }

  toggleSound(): void {
    this.soundEnabled = !this.soundEnabled;
    try {
      localStorage.setItem('xerife_sound_enabled', String(this.soundEnabled));
    } catch (e) {}

    if (this.soundEnabled) {
      this.playAlertSound();
    }
  }

  requestNotificationPermission(): void {
    if (typeof window === 'undefined' || !('Notification' in window)) {
      alert('Seu navegador não tem suporte a notificações de desktop.');
      return;
    }

    Notification.requestPermission().then((permission) => {
      this.notificationsEnabled = permission === 'granted';
      if (this.notificationsEnabled) {
        try {
          new Notification('🔔 Notificações Ativadas!', {
            body: 'Você será avisado aqui na área de trabalho quando um cliente pedir atendente humano.',
            icon: 'favicon.ico'
          });
        } catch (e) {}
      }
    });
  }

  playAlertSound(): void {
    if (!this.soundEnabled || typeof window === 'undefined') return;
    try {
      const AudioCtxClass = window.AudioContext || (window as any).webkitAudioContext;
      if (!AudioCtxClass) return;
      const ctx = new AudioCtxClass();

      const now = ctx.currentTime;
      const osc1 = ctx.createOscillator();
      const gain1 = ctx.createGain();
      osc1.type = 'sine';
      osc1.frequency.setValueAtTime(587.33, now);
      gain1.gain.setValueAtTime(0.25, now);
      gain1.gain.exponentialRampToValueAtTime(0.001, now + 0.3);
      osc1.connect(gain1);
      gain1.connect(ctx.destination);
      osc1.start(now);
      osc1.stop(now + 0.3);

      const osc2 = ctx.createOscillator();
      const gain2 = ctx.createGain();
      osc2.type = 'sine';
      osc2.frequency.setValueAtTime(880, now + 0.12);
      gain2.gain.setValueAtTime(0.35, now + 0.12);
      gain2.gain.exponentialRampToValueAtTime(0.001, now + 0.55);
      osc2.connect(gain2);
      gain2.connect(ctx.destination);
      osc2.start(now + 0.12);
      osc2.stop(now + 0.55);
    } catch (err) {}
  }

  triggerDesktopNotification(session: CustomerSession): void {
    if (typeof window === 'undefined' || !('Notification' in window) || Notification.permission !== 'granted') {
      return;
    }

    try {
      const title = '🚨 Atendimento Humano Solicitado!';
      const phone = session.formattedPhone || session.remoteJid.replace('@s.whatsapp.net', '');
      const body = `${session.pushName || 'Cliente'} (${phone}) aguarda atendimento humano.\nMotivo: ${session.pauseReason || 'Transferência solicitada'}`;
      const notif = new Notification(title, {
        body: body,
        icon: 'favicon.ico'
      });
      notif.onclick = () => {
        window.focus();
        this.activeTab = 'sessions';
        this.sessionStatusFilter = 'paused';
      };
    } catch (e) {}
  }

  dismissHandoverAlert(session: CustomerSession): void {
    this.dismissedAlertSessionIds.add(session.id);
    this.activeHandoverAlert = null;
  }

  openChannelAction(session: CustomerSession): void {
    if (session.channel === 'Instagram') {
      window.open('https://www.instagram.com/direct/inbox/', '_blank');
    } else {
      this.openWhatsAppWeb(session);
    }
  }

  openWhatsAppWeb(session: CustomerSession): void {
    const clean = (session.remoteJid || '').replace('@s.whatsapp.net', '').replace(/[^0-9]/g, '');
    if (clean) {
      window.open(`https://web.whatsapp.com/send?phone=${clean}`, '_blank');
    } else {
      window.open('https://web.whatsapp.com', '_blank');
    }
  }

  filterByHumanHandover(): void {
    this.activeTab = 'sessions';
    this.sessionStatusFilter = 'paused';
  }

  loadGames(): void {
    this.isLoadingGames = true;
    this.apiService.getGames().subscribe({
      next: (data) => {
        this.games = data;
        this.isLoadingGames = false;
      },
      error: () => {
        this.isLoadingGames = false;
      }
    });
  }

  loadSettings(): void {
    this.isLoadingSettings = true;
    this.apiService.getSettings().subscribe({
      next: (data) => {
        this.settings = data;
        this.isLoadingSettings = false;
      },
      error: () => {
        this.isLoadingSettings = false;
      }
    });
  }

  toggleGlobalBot(): void {
    this.isTogglingGlobalBot = true;
    this.apiService.toggleGlobalBot().subscribe({
      next: (data) => {
        this.settings = data;
        this.isTogglingGlobalBot = false;
        this.settingsSaveSuccess = true;
        setTimeout(() => (this.settingsSaveSuccess = false), 3500);
      },
      error: () => {
        this.isTogglingGlobalBot = false;
        alert('Erro ao alterar status global do robô.');
      }
    });
  }

  onAiToggleChange(): void {
    this.isSavingSettings = true;
    this.apiService.updateSettings({ isAiEnabled: this.settings.isAiEnabled }).subscribe({
      next: (data) => {
        this.settings = data;
        this.isSavingSettings = false;
        this.settingsSaveSuccess = true;
        setTimeout(() => (this.settingsSaveSuccess = false), 3500);
      },
      error: () => {
        this.isSavingSettings = false;
        alert('Erro ao salvar alteração da IA.');
      }
    });
  }

  saveSettings(): void {
    this.isSavingSettings = true;
    this.settingsSaveSuccess = false;
    this.apiService.updateSettings(this.settings).subscribe({
      next: (data) => {
        this.settings = data;
        this.isSavingSettings = false;
        this.settingsSaveSuccess = true;
        setTimeout(() => (this.settingsSaveSuccess = false), 4000);
      },
      error: () => {
        this.isSavingSettings = false;
        alert('Erro ao salvar configurações.');
      }
    });
  }

  testAiConnection(): void {
    if (!this.settings.aiApiKey) {
      alert('Por favor, informe a chave de API (API Key) antes de testar.');
      return;
    }

    this.isTestingAi = true;
    this.testAiResult = null;
    this.apiService
      .testAi(this.settings.aiProvider, this.settings.aiApiKey, this.settings.aiModel, this.testAiMessage)
      .subscribe({
        next: (res) => {
          this.isTestingAi = false;
          this.testAiResult = res;
        },
        error: () => {
          this.isTestingAi = false;
          this.testAiResult = {
            success: false,
            reply: '',
            error: 'Erro de comunicação ao testar IA. Verifique se a API está online.'
          };
        }
      });
  }

  // =========================================================================
  // Métodos de Upload Manual do Catálogo
  // =========================================================================

  toggleUploadPanel(): void {
    this.showUploadPanel = !this.showUploadPanel;
    this.uploadResult = null;
  }

  onFileSelected(event: any): void {
    const file = event.target.files?.[0];
    if (file) {
      this.selectedFile = file;
    }
  }

  submitUpload(): void {
    if (!this.selectedFile) {
      alert('Selecione um arquivo CSV para enviar.');
      return;
    }

    this.isUploading = true;
    this.uploadResult = null;

    this.apiService.uploadCatalogCsv(this.selectedFile, this.clearExistingOnUpload).subscribe({
      next: (res) => {
        this.isUploading = false;
        this.uploadResult = res;
        if (res.success) {
          this.loadGames();
          this.loadStats();
          this.selectedFile = null;
        }
      },
      error: () => {
        this.isUploading = false;
        this.uploadResult = {
          success: false,
          message: 'Erro no envio do arquivo. Verifique se o formato está correto.',
          gamesImported: 0,
          gamesAvailable: 0,
          gamesRented: 0,
          importedNames: []
        };
      }
    });
  }

  clearCatalog(): void {
    if (!confirm('Tem certeza que deseja ZERAR os jogos do catálogo desta empresa? Esta ação não pode ser desfeita.')) {
      return;
    }

    this.isClearing = true;
    this.apiService.clearCatalog().subscribe({
      next: (res) => {
        this.isClearing = false;
        alert(res.message);
        this.loadGames();
        this.loadStats();
      },
      error: () => {
        this.isClearing = false;
        alert('Erro ao zerar catálogo.');
      }
    });
  }

  downloadTemplate(): void {
    const csvContent =
      "boardgame_id,name,aquisition_price,id_bgg,link_ludopedia,copy_name,slug,status,comments\n" +
      "92829d29-0d06-4d19-b78d-4c3df733fef7,7 Wonders Duel,206.39,173346,https://ludopedia.com.br/jogo/7-wonders-duel,Cópia 1,7-wonders-duel,available,\n" +
      "6a0b4ecc-2bf2-443d-8991-f5d5abf261d9,Azul,329.90,230802,https://ludopedia.com.br/jogo/azul,Cópia 1,azul,available,\n" +
      "bca9ffc8-7a72-4545-96ac-3a0364f4394d,Coup,89.90,158408,https://ludopedia.com.br/jogo/coup,Cópia 1,coup,rented,";

    const blob = new Blob([csvContent], { type: 'text/csv;charset=utf-8;' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = 'modelo_catalogo_jogos.csv';
    a.click();
    URL.revokeObjectURL(url);
  }

  startAutoRefresh(): void {
    this.stopAutoRefresh();
    this.refreshInterval = setInterval(() => {
      if (this.autoRefresh && !this.backendOffline && this.isLoggedIn) {
        this.loadSessions();
        this.loadStats();
        this.loadWhatsAppStatus();
      }
    }, 8000);
  }

  stopAutoRefresh(): void {
    if (this.refreshInterval) {
      clearInterval(this.refreshInterval);
      this.refreshInterval = null;
    }
  }

  toggleAutoRefresh(): void {
    this.autoRefresh = !this.autoRefresh;
  }

  togglePause(session: CustomerSession): void {
    this.isUpdatingSessionId = session.id;
    this.apiService.togglePause(session.id, 12).subscribe({
      next: (updated) => {
        const index = this.sessions.findIndex((s) => s.id === updated.id);
        if (index !== -1) {
          this.sessions[index] = updated;
        }
        this.isUpdatingSessionId = null;
        this.loadStats();
      },
      error: () => {
        this.isUpdatingSessionId = null;
      }
    });
  }

  closeSession(session: CustomerSession, sendGoodbye: boolean = true): void {
    const clientName = session.pushName || 'este cliente';
    const channelName = session.channel === 'Instagram' ? 'Instagram Direct' : 'WhatsApp';
    const messageConfirm = sendGoodbye
      ? `Deseja encerrar o atendimento de ${clientName} (${channelName})?\n\n• Uma mensagem de agradecimento/despedida será enviada no ${channelName}.\n• O robô voltará a ficar ativo para novas mensagens.`
      : `Deseja encerrar o atendimento de ${clientName} (${channelName}) e reativar o robô?`;

    if (!confirm(messageConfirm)) {
      return;
    }

    this.isClosingSessionId = session.id;
    this.apiService.closeSession(session.id, sendGoodbye).subscribe({
      next: (updated) => {
        const index = this.sessions.findIndex((s) => s.id === updated.id);
        if (index !== -1) {
          this.sessions[index] = updated;
        }
        if (this.activeHandoverAlert?.id === updated.id) {
          this.activeHandoverAlert = null;
        }
        this.isClosingSessionId = null;
        this.loadStats();
      },
      error: () => {
        this.isClosingSessionId = null;
        alert('Erro ao encerrar atendimento.');
      }
    });
  }

  toggleGameAvailability(game: BoardGame): void {
    this.isUpdatingGameId = game.id;
    this.apiService.toggleGameAvailability(game.id).subscribe({
      next: (updated) => {
        const index = this.games.findIndex((g) => g.id === updated.id);
        if (index !== -1) {
          this.games[index] = updated;
        }
        this.isUpdatingGameId = null;
        this.loadStats();
      },
      error: () => {
        this.isUpdatingGameId = null;
      }
    });
  }

  get filteredSessions(): CustomerSession[] {
    return this.sessions.filter((session) => {
      const matchesSearch =
        !this.sessionSearch ||
        session.pushName.toLowerCase().includes(this.sessionSearch.toLowerCase()) ||
        session.formattedPhone.includes(this.sessionSearch) ||
        session.remoteJid.includes(this.sessionSearch);

      const matchesStatus =
        this.sessionStatusFilter === 'all' ||
        (this.sessionStatusFilter === 'paused' && session.isCurrentlyPaused) ||
        (this.sessionStatusFilter === 'active' && !session.isCurrentlyPaused);

      const matchesChannel =
        this.sessionChannelFilter === 'all' ||
        (this.sessionChannelFilter === 'WhatsApp' && (!session.channel || session.channel === 'WhatsApp')) ||
        (this.sessionChannelFilter === 'Instagram' && session.channel === 'Instagram');

      return matchesSearch && matchesStatus && matchesChannel;
    });
  }

  get filteredGames(): BoardGame[] {
    return this.games.filter((game) => {
      const name = game.name || (game as any).Name || '';
      const category = game.category || (game as any).Category || '';
      const matchesSearch =
        !this.gameSearch ||
        name.toLowerCase().includes(this.gameSearch.toLowerCase()) ||
        category.toLowerCase().includes(this.gameSearch.toLowerCase());

      const matchesAvailability =
        this.gameAvailabilityFilter === 'all' ||
        (this.gameAvailabilityFilter === 'available' && game.isAvailable) ||
        (this.gameAvailabilityFilter === 'rented' && !game.isAvailable);

      return matchesSearch && matchesAvailability;
    });
  }

  formatDate(dateString?: string | null): string {
    if (!dateString) return '-';
    const date = new Date(dateString);
    return date.toLocaleString('pt-BR', {
      day: '2-digit',
      month: '2-digit',
      year: 'numeric',
      hour: '2-digit',
      minute: '2-digit'
    });
  }

  getTimeFormatted(dateString?: string | null): string {
    if (!dateString) return '';
    const date = new Date(dateString);
    return date.toLocaleTimeString('pt-BR', { hour: '2-digit', minute: '2-digit' });
  }

  getBusinessTypeName(type?: number): string {
    switch (type) {
      case 1:
        return 'Locadora de Jogos';
      case 2:
        return 'Loja de Jogos';
      case 3:
        return 'Locadora & Loja (Híbrido)';
      default:
        return 'Empresa de Jogos';
    }
  }
}
