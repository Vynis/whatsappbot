import { Component, OnInit, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ApiService } from './services/api.service';
import { CustomerSession } from './models/customer-session.model';
import { BoardGame } from './models/board-game.model';
import { DashboardStats } from './models/dashboard-stats.model';
import { WhatsAppStatus } from './models/whatsapp-status.model';
import { BotSettings, UploadCatalogResult, TestAiResponse } from './models/settings.model';
import { InstagramCommentLog, SetupIceBreakersResult } from './models/instagram-comment.model';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './app.component.html',
  styleUrls: ['./app.component.css']
})
export class AppComponent implements OnInit, OnDestroy {
  activeTab: 'sessions' | 'comments' | 'games' | 'settings' = 'sessions';

  sessions: CustomerSession[] = [];
  comments: InstagramCommentLog[] = [];
  games: BoardGame[] = [];
  stats: DashboardStats | null = null;
  whatsAppStatus: WhatsAppStatus | null = null;
  backendOffline = false;

  isLoadingSessions = false;
  isLoadingComments = false;
  isLoadingGames = false;
  isUpdatingSessionId: number | null = null;
  isClosingSessionId: number | null = null;
  isUpdatingGameId: number | null = null;

  sessionSearch = '';
  sessionStatusFilter: 'all' | 'active' | 'paused' = 'all';
  sessionChannelFilter: 'all' | 'WhatsApp' | 'Instagram' = 'all';

  commentSearch = '';
  isSettingUpIceBreakers = false;
  iceBreakersResult: SetupIceBreakersResult | null = null;

  gameSearch = '';
  gameAvailabilityFilter: 'all' | 'available' | 'rented' = 'all';

  autoRefresh = true;
  private refreshInterval: any;

  // =========================================================================
  // Alertas de Atendimento Humano (Sons, Banner & Notificações Desktop)
  // =========================================================================
  soundEnabled = true;
  notificationsEnabled = false;
  knownPausedSessionIds: Set<number> = new Set<number>();
  dismissedAlertSessionIds: Set<number> = new Set<number>();
  activeHandoverAlert: CustomerSession | null = null;
  originalDocumentTitle = 'Xerife Board Games - Central de Atendimento';

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
  testAiMessage = 'Olá! Apresente-se brevemente como o atendente virtual da Xerife Board Games.';
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
    }

    if (typeof document !== 'undefined') {
      this.originalDocumentTitle = document.title || 'Xerife Board Games - Central de Atendimento';
    }

    this.loadAll();
    this.startAutoRefresh();
  }

  ngOnDestroy(): void {
    this.stopAutoRefresh();
  }

  loadAll(): void {
    this.loadWhatsAppStatus();
    this.loadSessions();
    this.loadComments();
    this.loadGames();
    this.loadStats();
    this.loadSettings();
  }

  loadComments(): void {
    this.isLoadingComments = true;
    this.apiService.getInstagramComments().subscribe({
      next: (data) => {
        this.comments = data;
        this.isLoadingComments = false;
      },
      error: (err) => {
        console.error('Erro ao carregar comentários do Instagram:', err);
        this.isLoadingComments = false;
      }
    });
  }

  setupIceBreakers(): void {
    this.isSettingUpIceBreakers = true;
    this.iceBreakersResult = null;
    this.apiService.setupIceBreakers().subscribe({
      next: (res) => {
        this.isSettingUpIceBreakers = false;
        this.iceBreakersResult = res;
        setTimeout(() => (this.iceBreakersResult = null), 6000);
      },
      error: (err) => {
        this.isSettingUpIceBreakers = false;
        this.iceBreakersResult = {
          success: false,
          message: 'Falha ao conectar com a Meta Graph API. Verifique as credenciais no appsettings.json.'
        };
      }
    });
  }

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
        console.error('Erro ao carregar sessões:', err);
        this.isLoadingSessions = false;
        this.backendOffline = true;
      }
    });
  }

  processHandoverAlerts(currentSessions: CustomerSession[]): void {
    const pausedSessions = currentSessions.filter((s) => s.isCurrentlyPaused);

    // 1. Atualizar título da aba do navegador para chamar a atenção
    if (typeof document !== 'undefined') {
      if (pausedSessions.length > 0) {
        document.title = `(${pausedSessions.length}) 🚨 ATENDIMENTO HUMANO - Xerife`;
      } else {
        document.title = this.originalDocumentTitle;
      }
    }

    // 2. Verificar se há uma nova sessão pausada que ainda não conhecíamos
    const newlyPaused = pausedSessions.find((s) => !this.knownPausedSessionIds.has(s.id));
    if (newlyPaused) {
      this.playAlertSound();
      this.triggerDesktopNotification(newlyPaused);
      if (!this.dismissedAlertSessionIds.has(newlyPaused.id)) {
        this.activeHandoverAlert = newlyPaused;
      }
    } else if (this.activeHandoverAlert) {
      // Se a sessão do alerta atual foi reativada, fecha o alerta
      const current = pausedSessions.find((s) => s.id === this.activeHandoverAlert?.id);
      if (!current) {
        this.activeHandoverAlert = null;
      }
    } else if (pausedSessions.length > 0 && !this.activeHandoverAlert) {
      // Se há pausados e nenhum em banner, mostra o primeiro não dispensado
      this.activeHandoverAlert = pausedSessions.find((s) => !this.dismissedAlertSessionIds.has(s.id)) || null;
    }

    // 3. Atualizar conjunto de IDs conhecidos
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
      } else if (permission === 'denied') {
        alert('As notificações foram bloqueadas no navegador. Para ativar, clique no ícone de cadeado na barra de endereço do navegador.');
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
      // Primeiro tom do chime (D5 - 587.33 Hz)
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

      // Segundo tom do chime (A5 - 880 Hz)
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
    } catch (err) {
      console.warn('Alerta sonoro não emitido pelo navegador:', err);
    }
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
    } catch (e) {
      console.warn('Erro ao disparar notificação desktop:', e);
    }
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
      error: (err) => {
        console.error('Erro ao carregar catálogo de jogos:', err);
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
      error: (err) => {
        console.error('Erro ao carregar configurações:', err);
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
      error: (err) => {
        console.error('Erro ao alternar status global do robô:', err);
        this.isTogglingGlobalBot = false;
        alert('Erro ao alterar status global do robô. Verifique a conexão com a API.');
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
      error: (err) => {
        console.error('Erro ao salvar alteração da IA:', err);
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
      error: (err) => {
        console.error('Erro ao salvar configurações:', err);
        this.isSavingSettings = false;
        alert('Erro ao salvar configurações. Verifique o terminal da API.');
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
        error: (err) => {
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
      error: (err) => {
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
    if (!confirm('Tem certeza que deseja ZERAR todos os jogos do catálogo? Esta ação não pode ser desfeita.')) {
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
      error: (err) => {
        this.isClearing = false;
        alert('Erro ao zerar catálogo.');
      }
    });
  }

  downloadTemplate(): void {
    const csvContent = "boardgame_id,name,aquisition_price,id_bgg,link_ludopedia,copy_name,slug,status,comments\n" +
      "92829d29-0d06-4d19-b78d-4c3df733fef7,7 Wonders Duel,206.39,173346,https://ludopedia.com.br/jogo/7-wonders-duel,Cópia 1,7-wonders-duel,available,\n" +
      "6a0b4ecc-2bf2-443d-8991-f5d5abf261d9,Azul,329.90,230802,https://ludopedia.com.br/jogo/azul,Cópia 1,azul,available,\n" +
      "bca9ffc8-7a72-4545-96ac-3a0364f4394d,Coup,89.90,158408,https://ludopedia.com.br/jogo/coup,Cópia 1,coup,rented,";

    const blob = new Blob([csvContent], { type: 'text/csv;charset=utf-8;' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = 'modelo_catalogo_xerife.csv';
    a.click();
    URL.revokeObjectURL(url);
  }

  startAutoRefresh(): void {
    this.refreshInterval = setInterval(() => {
      if (this.autoRefresh && !this.backendOffline) {
        this.loadSessions();
        this.loadStats();
        this.loadWhatsAppStatus();
        if (this.activeTab === 'comments') {
          this.loadComments();
        }
      }
    }, 8000);
  }

  stopAutoRefresh(): void {
    if (this.refreshInterval) {
      clearInterval(this.refreshInterval);
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
      error: (err) => {
        console.error('Erro ao alternar pausa da sessão:', err);
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
      error: (err) => {
        console.error('Erro ao encerrar atendimento:', err);
        this.isClosingSessionId = null;
        alert('Erro ao encerrar atendimento. Verifique a conexão com a API.');
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
      error: (err) => {
        console.error('Erro ao alternar disponibilidade do jogo:', err);
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

  get filteredComments(): InstagramCommentLog[] {
    return this.comments.filter((c) => {
      if (!this.commentSearch) return true;
      const q = this.commentSearch.toLowerCase();
      return (
        c.username.toLowerCase().includes(q) ||
        c.commentText.toLowerCase().includes(q) ||
        (c.publicReplyText && c.publicReplyText.toLowerCase().includes(q))
      );
    });
  }

  get filteredGames(): BoardGame[] {
    return this.games.filter((game) => {
      const matchesSearch =
        !this.gameSearch ||
        game.name.toLowerCase().includes(this.gameSearch.toLowerCase()) ||
        game.category.toLowerCase().includes(this.gameSearch.toLowerCase());

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
}
