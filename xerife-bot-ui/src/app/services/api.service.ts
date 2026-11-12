import { Injectable } from '@angular/core';
import { HttpClient, HttpHeaders } from '@angular/common/http';
import { Observable } from 'rxjs';
import { CustomerSession } from '../models/customer-session.model';
import { BoardGame } from '../models/board-game.model';
import { DashboardStats } from '../models/dashboard-stats.model';
import { WhatsAppStatus } from '../models/whatsapp-status.model';
import { BotSettings, UploadCatalogResult, TestAiResponse } from '../models/settings.model';
import {
  AuthResponse,
  UserSummary,
  TenantSummary,
  WhatsAppConnectResult,
  WhatsAppActionResponse,
  TenantProfile
} from '../models/auth.model';

@Injectable({
  providedIn: 'root'
})
export class ApiService {
  private readonly baseUrl = 'http://localhost:5001/api';
  private readonly adminUrl = 'http://localhost:5001/api/admin';
  private readonly authUrl = 'http://localhost:5001/api/auth';

  private readonly TOKEN_KEY = 'saas_auth_token';
  private readonly USER_KEY = 'saas_user';
  private readonly TENANT_KEY = 'saas_tenant';

  constructor(private http: HttpClient) {}

  // =========================================================================
  // Gestão de Sessão e Token JWT
  // =========================================================================

  getToken(): string | null {
    if (typeof window !== 'undefined') {
      return localStorage.getItem(this.TOKEN_KEY);
    }
    return null;
  }

  getCurrentUser(): UserSummary | null {
    if (typeof window !== 'undefined') {
      const data = localStorage.getItem(this.USER_KEY);
      if (data) {
        try {
          return JSON.parse(data);
        } catch {
          return null;
        }
      }
    }
    return null;
  }

  getCurrentTenant(): TenantSummary | null {
    if (typeof window !== 'undefined') {
      const data = localStorage.getItem(this.TENANT_KEY);
      if (data) {
        try {
          return JSON.parse(data);
        } catch {
          return null;
        }
      }
    }
    return null;
  }

  setAuth(response: AuthResponse): void {
    if (typeof window !== 'undefined') {
      if (response.token) {
        localStorage.setItem(this.TOKEN_KEY, response.token);
      }
      if (response.user) {
        localStorage.setItem(this.USER_KEY, JSON.stringify(response.user));
      }
      if (response.tenant) {
        localStorage.setItem(this.TENANT_KEY, JSON.stringify(response.tenant));
      }
    }
  }

  clearAuth(): void {
    if (typeof window !== 'undefined') {
      localStorage.removeItem(this.TOKEN_KEY);
      localStorage.removeItem(this.USER_KEY);
      localStorage.removeItem(this.TENANT_KEY);
    }
  }

  isAuthenticated(): boolean {
    return !!this.getToken();
  }

  private getAuthHeaders(): { headers: HttpHeaders } {
    const token = this.getToken();
    let headers = new HttpHeaders();
    if (token) {
      headers = headers.set('Authorization', `Bearer ${token}`);
    }
    return { headers };
  }

  // =========================================================================
  // Endpoints de Autenticação (Auth)
  // =========================================================================

  login(credentials: { email: string; password: string }): Observable<AuthResponse> {
    return this.http.post<AuthResponse>(`${this.authUrl}/login`, credentials);
  }

  register(data: {
    companyName: string;
    fullName: string;
    email: string;
    password: string;
    businessType: number;
    cityOrRegion?: string;
    websiteUrl?: string;
  }): Observable<AuthResponse> {
    return this.http.post<AuthResponse>(`${this.authUrl}/register`, data);
  }

  getMe(): Observable<AuthResponse> {
    return this.http.get<AuthResponse>(`${this.authUrl}/me`, this.getAuthHeaders());
  }

  // =========================================================================
  // Conexão Dinâmica WhatsApp & QR Code (Evolution API v2)
  // =========================================================================

  getWhatsAppStatus(): Observable<WhatsAppStatus> {
    return this.http.get<WhatsAppStatus>(`${this.adminUrl}/whatsapp-status`, this.getAuthHeaders());
  }

  connectWhatsApp(): Observable<WhatsAppConnectResult> {
    return this.http.post<WhatsAppConnectResult>(`${this.adminUrl}/whatsapp/connect`, {}, this.getAuthHeaders());
  }

  disconnectWhatsApp(): Observable<WhatsAppActionResponse> {
    return this.http.post<WhatsAppActionResponse>(`${this.adminUrl}/whatsapp/disconnect`, {}, this.getAuthHeaders());
  }

  restartWhatsApp(): Observable<WhatsAppActionResponse> {
    return this.http.post<WhatsAppActionResponse>(`${this.adminUrl}/whatsapp/restart`, {}, this.getAuthHeaders());
  }

  // =========================================================================
  // Sessões de Atendimento
  // =========================================================================

  getSessions(): Observable<CustomerSession[]> {
    return this.http.get<CustomerSession[]>(`${this.adminUrl}/sessions`, this.getAuthHeaders());
  }

  togglePause(sessionId: number, durationHours: number = 12, reason?: string): Observable<CustomerSession> {
    return this.http.post<CustomerSession>(
      `${this.adminUrl}/sessions/${sessionId}/toggle-pause`,
      { durationHours, reason },
      this.getAuthHeaders()
    );
  }

  closeSession(sessionId: number, sendGoodbyeMessage: boolean = true, customMessage?: string): Observable<CustomerSession> {
    return this.http.post<CustomerSession>(
      `${this.adminUrl}/sessions/${sessionId}/close`,
      { sendGoodbyeMessage, customMessage },
      this.getAuthHeaders()
    );
  }

  getStats(): Observable<DashboardStats> {
    return this.http.get<DashboardStats>(`${this.adminUrl}/stats`, this.getAuthHeaders());
  }

  // =========================================================================
  // Gestão Completa de Catálogo de Jogos (CRUD + CSV)
  // =========================================================================

  getGames(): Observable<BoardGame[]> {
    return this.http.get<BoardGame[]>(`${this.adminUrl}/games`, this.getAuthHeaders());
  }

  createGame(game: Partial<BoardGame>): Observable<BoardGame> {
    return this.http.post<BoardGame>(`${this.adminUrl}/games`, game, this.getAuthHeaders());
  }

  updateGame(gameId: number, game: Partial<BoardGame>): Observable<BoardGame> {
    return this.http.put<BoardGame>(`${this.adminUrl}/games/${gameId}`, game, this.getAuthHeaders());
  }

  deleteGame(gameId: number): Observable<{ success: boolean; message: string }> {
    return this.http.delete<{ success: boolean; message: string }>(`${this.adminUrl}/games/${gameId}`, this.getAuthHeaders());
  }

  toggleGameAvailability(gameId: number): Observable<BoardGame> {
    return this.http.put<BoardGame>(`${this.adminUrl}/games/${gameId}/toggle-availability`, {}, this.getAuthHeaders());
  }

  uploadCatalogCsv(file: File, clearExisting: boolean = true): Observable<UploadCatalogResult> {
    const formData = new FormData();
    formData.append('file', file, file.name);
    const token = this.getToken();
    let headers = new HttpHeaders();
    if (token) {
      headers = headers.set('Authorization', `Bearer ${token}`);
    }
    return this.http.post<UploadCatalogResult>(
      `${this.adminUrl}/games/upload?clearExisting=${clearExisting}`,
      formData,
      { headers }
    );
  }

  clearCatalog(): Observable<{ success: boolean; message: string }> {
    return this.http.post<{ success: boolean; message: string }>(`${this.adminUrl}/games/clear`, {}, this.getAuthHeaders());
  }

  // =========================================================================
  // Perfil da Empresa (Tenant) e Configurações de IA
  // =========================================================================

  getProfile(): Observable<TenantProfile> {
    return this.http.get<TenantProfile>(`${this.adminUrl}/profile`, this.getAuthHeaders());
  }

  updateProfile(profile: Partial<TenantProfile>): Observable<TenantProfile> {
    return this.http.put<TenantProfile>(`${this.adminUrl}/profile`, profile, this.getAuthHeaders());
  }

  getSettings(): Observable<BotSettings> {
    return this.http.get<BotSettings>(`${this.adminUrl}/settings`, this.getAuthHeaders());
  }

  toggleGlobalBot(): Observable<BotSettings> {
    return this.http.post<BotSettings>(`${this.adminUrl}/toggle-global-bot`, {}, this.getAuthHeaders());
  }

  updateSettings(settings: Partial<BotSettings>): Observable<BotSettings> {
    return this.http.put<BotSettings>(`${this.adminUrl}/settings`, settings, this.getAuthHeaders());
  }

  testAi(provider: string, apiKey: string, model: string, message?: string): Observable<TestAiResponse> {
    return this.http.post<TestAiResponse>(
      `${this.adminUrl}/ai/test`,
      { provider, apiKey, model, message },
      this.getAuthHeaders()
    );
  }
}
