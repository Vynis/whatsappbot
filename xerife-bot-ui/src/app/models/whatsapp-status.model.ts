export interface WhatsAppStatus {
  instanceName: string;
  status: string;
  isConnected: boolean;
  ownerJid?: string;
  formattedPhone?: string;
  profileName?: string;
}
