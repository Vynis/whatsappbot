export interface BotSettings {
  isBotActiveGlobally: boolean;
  isAiEnabled: boolean;
  aiProvider: 'gemini' | 'openai';
  aiApiKey: string;
  aiModel: string;
  aiSystemPrompt: string;
  totalGamesInDb: number;
}

export interface UploadCatalogResult {
  success: boolean;
  message: string;
  gamesImported: number;
  gamesAvailable: number;
  gamesRented: number;
  importedNames: string[];
}

export interface TestAiResponse {
  success: boolean;
  reply: string;
  error?: string;
}
