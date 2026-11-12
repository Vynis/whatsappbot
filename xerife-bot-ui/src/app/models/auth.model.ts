export interface UserSummary {
  id: string;
  fullName: string;
  email: string;
  role: string;
  tenantId: string;
}

export interface TenantSummary {
  id: string;
  name: string;
  slug: string;
  businessType: number; // 1: Locadora, 2: Loja, 3: Híbrido
  plan: string;
  whatsAppInstanceName?: string;
  isActive: boolean;
}

export interface AuthResponse {
  token: string;
  user: UserSummary;
  tenant: TenantSummary;
}

export interface WhatsAppConnectResult {
  instanceName: string;
  status: string;
  isConnected: boolean;
  qrCodeBase64?: string;
  pairingCode?: string;
  message?: string;
}

export interface WhatsAppActionResponse {
  success: boolean;
  message: string;
  status?: string;
}

export interface TenantProfile {
  id: string;
  name: string;
  slug: string;
  businessType: number;
  cityOrRegion?: string;
  websiteUrl?: string;
  plan: string;
  whatsAppInstanceName?: string;
}
