export interface CustomerSession {
  id: number;
  remoteJid: string;
  channel: 'WhatsApp' | 'Instagram';
  pushName: string;
  formattedPhone: string;
  isBotPaused: boolean;
  botPausedUntil?: string | null;
  pauseReason?: string | null;
  lastInteractionAt: string;
  isCurrentlyPaused: boolean;
}
