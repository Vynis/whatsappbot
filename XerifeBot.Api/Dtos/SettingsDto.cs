namespace XerifeBot.Api.Dtos;

public class BotSettingsDto
{
    public bool IsBotActiveGlobally { get; set; } = true;
    public string RejoyCatalogUrl { get; set; } = "https://xerifebg.rejoy.com.br/search";
    public bool IsAiEnabled { get; set; } = false;
    public string AiProvider { get; set; } = "gemini"; // "gemini" ou "openai"
    public string AiApiKey { get; set; } = string.Empty;
    public string AiModel { get; set; } = "gemini-1.5-flash";
    public string AiSystemPrompt { get; set; } = string.Empty;
    public DateTime? LastSyncAt { get; set; }
    public int TotalGamesInDb { get; set; }
}

public class UpdateBotSettingsRequest
{
    public bool? IsBotActiveGlobally { get; set; }
    public string? RejoyCatalogUrl { get; set; }
    public bool? IsAiEnabled { get; set; }
    public string? AiProvider { get; set; }
    public string? AiApiKey { get; set; }
    public string? AiModel { get; set; }
    public string? AiSystemPrompt { get; set; }
}

public class ImportCsvTextRequest
{
    public string CsvContent { get; set; } = string.Empty;
    public bool ClearExisting { get; set; } = true;
}

public class SyncCatalogRequest
{
    public string? CustomUrl { get; set; }
}

public class SyncCatalogResultDto
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public int GamesAdded { get; set; }
    public int GamesUpdated { get; set; }
    public int TotalGames { get; set; }
    public List<string> SyncedGameNames { get; set; } = new();
}

public class TestAiRequest
{
    public string Provider { get; set; } = "gemini";
    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = "gemini-1.5-flash";
    public string? Message { get; set; }
}

public class TestAiResponse
{
    public bool Success { get; set; }
    public string Reply { get; set; } = string.Empty;
    public string? Error { get; set; }
}
