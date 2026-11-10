import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { CustomerSession } from '../models/customer-session.model';
import { BoardGame } from '../models/board-game.model';
import { DashboardStats } from '../models/dashboard-stats.model';
import { WhatsAppStatus } from '../models/whatsapp-status.model';
import { BotSettings, UploadCatalogResult, TestAiResponse } from '../models/settings.model';
import { InstagramCommentLog, SetupIceBreakersResult } from '../models/instagram-comment.model';

@Injectable({
  providedIn: 'root'
})
export class ApiService {
  private readonly baseUrl = 'http://localhost:5001/api/admin';

  constructor(private http: HttpClient) {}

  getWhatsAppStatus(): Observable<WhatsAppStatus> {
    return this.http.get<WhatsAppStatus>(`${this.baseUrl}/whatsapp-status`);
  }

  getSessions(): Observable<CustomerSession[]> {
    return this.http.get<CustomerSession[]>(`${this.baseUrl}/sessions`);
  }

  togglePause(sessionId: number, durationHours: number = 12, reason?: string): Observable<CustomerSession> {
    return this.http.post<CustomerSession>(`${this.baseUrl}/sessions/${sessionId}/toggle-pause`, {
      durationHours,
      reason
    });
  }

  closeSession(sessionId: number, sendGoodbyeMessage: boolean = true, customMessage?: string): Observable<CustomerSession> {
    return this.http.post<CustomerSession>(`${this.baseUrl}/sessions/${sessionId}/close`, {
      sendGoodbyeMessage,
      customMessage
    });
  }

  getGames(): Observable<BoardGame[]> {
    return this.http.get<BoardGame[]>(`${this.baseUrl}/games`);
  }

  toggleGameAvailability(gameId: number): Observable<BoardGame> {
    return this.http.put<BoardGame>(`${this.baseUrl}/games/${gameId}/toggle-availability`, {});
  }

  getStats(): Observable<DashboardStats> {
    return this.http.get<DashboardStats>(`${this.baseUrl}/stats`);
  }

  // =========================================================================
  // Upload Manual e Gestão do Catálogo de Jogos
  // =========================================================================

  uploadCatalogCsv(file: File, clearExisting: boolean = true): Observable<UploadCatalogResult> {
    const formData = new FormData();
    formData.append('file', file, file.name);
    return this.http.post<UploadCatalogResult>(`${this.baseUrl}/games/upload?clearExisting=${clearExisting}`, formData);
  }

  clearCatalog(): Observable<{ success: boolean; message: string }> {
    return this.http.post<{ success: boolean; message: string }>(`${this.baseUrl}/games/clear`, {});
  }

  // =========================================================================
  // Configurações e Inteligência Artificial
  // =========================================================================

  getSettings(): Observable<BotSettings> {
    return this.http.get<BotSettings>(`${this.baseUrl}/settings`);
  }

  toggleGlobalBot(): Observable<BotSettings> {
    return this.http.post<BotSettings>(`${this.baseUrl}/toggle-global-bot`, {});
  }

  updateSettings(settings: Partial<BotSettings>): Observable<BotSettings> {
    return this.http.put<BotSettings>(`${this.baseUrl}/settings`, settings);
  }

  testAi(provider: string, apiKey: string, model: string, message?: string): Observable<TestAiResponse> {
    return this.http.post<TestAiResponse>(`${this.baseUrl}/ai/test`, {
      provider,
      apiKey,
      model,
      message
    });
  }

  // =========================================================================
  // Instagram Graph API (Comentários e Ice Breakers)
  // =========================================================================

  getInstagramComments(): Observable<InstagramCommentLog[]> {
    return this.http.get<InstagramCommentLog[]>(`${this.baseUrl}/instagram/comments`);
  }

  setupIceBreakers(): Observable<SetupIceBreakersResult> {
    return this.http.post<SetupIceBreakersResult>(`${this.baseUrl}/instagram/setup-icebreakers`, {});
  }
}
