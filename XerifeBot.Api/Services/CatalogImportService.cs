using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using XerifeBot.Api.Data;
using XerifeBot.Api.Dtos;
using XerifeBot.Api.Models;

namespace XerifeBot.Api.Services;

public class CatalogImportService : ICatalogImportService
{
    private readonly AppDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<CatalogImportService> _logger;

    public CatalogImportService(
        AppDbContext dbContext,
        ITenantContext tenantContext,
        ILogger<CatalogImportService> logger)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    public async Task<bool> ClearCatalogAsync()
    {
        try
        {
            _logger.LogWarning("Zerando catálogo de jogos do tenant atual...");
            var games = await _dbContext.BoardGames.ToListAsync();
            _dbContext.BoardGames.RemoveRange(games);
            await _dbContext.SaveChangesAsync();
            _logger.LogInformation("Catálogo de jogos do tenant zerado com sucesso! ({Count} jogos removidos)", games.Count);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao zerar tabela BoardGames do tenant");
            return false;
        }
    }

    public async Task<UploadCatalogResultDto> ImportCatalogAsync(IFormFile file, bool clearExisting = true)
    {
        if (file == null || file.Length == 0)
        {
            return new UploadCatalogResultDto
            {
                Success = false,
                Message = "Nenhum arquivo foi enviado ou o arquivo está vazio."
            };
        }

        using var reader = new StreamReader(file.OpenReadStream(), Encoding.UTF8);
        string csvContent = await reader.ReadToEndAsync();
        return await ImportCsvTextAsync(csvContent, clearExisting);
    }

    public async Task<UploadCatalogResultDto> ImportCsvTextAsync(string csvContent, bool clearExisting = true)
    {
        var result = new UploadCatalogResultDto();
        try
        {
            if (string.IsNullOrWhiteSpace(csvContent))
            {
                result.Success = false;
                result.Message = "O conteúdo do arquivo CSV está vazio.";
                return result;
            }

            var lines = csvContent
                .Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.Trim())
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .ToList();

            if (lines.Count < 2)
            {
                result.Success = false;
                result.Message = "O arquivo precisa conter uma linha de cabeçalho e pelo menos um jogo.";
                return result;
            }

            // Detectar delimitador (vírgula ou ponto e vírgula)
            char delimiter = lines[0].Contains(';') ? ';' : ',';

            var headers = ParseCsvLine(lines[0], delimiter)
                .Select(h => h.Trim().ToLowerInvariant())
                .ToList();

            int colBoardgameId = headers.FindIndex(h => h == "boardgame_id" || h == "id");
            int colName = headers.FindIndex(h => h == "name" || h == "nome" || h == "jogo");
            int colAquisition = headers.FindIndex(h => h == "aquisition_price" || h == "acquisition_price" || h == "preco_aquisicao");
            int colIdBgg = headers.FindIndex(h => h == "id_bgg" || h == "bgg_id");
            int colLudopedia = headers.FindIndex(h => h == "link_ludopedia" || h == "ludopedia");
            int colCopyName = headers.FindIndex(h => h == "copy_name" || h == "copia");
            int colSlug = headers.FindIndex(h => h == "slug");
            int colStatus = headers.FindIndex(h => h == "status");
            int colComments = headers.FindIndex(h => h == "comments" || h == "observacoes");

            if (colName == -1)
            {
                result.Success = false;
                result.Message = "Cabeçalho obrigatório 'name' não foi encontrado no arquivo.";
                return result;
            }

            if (clearExisting)
            {
                await ClearCatalogAsync();
            }

            var gamesToInsert = new List<BoardGame>();
            int availableCount = 0;
            int rentedCount = 0;

            for (int i = 1; i < lines.Count; i++)
            {
                var fields = ParseCsvLine(lines[i], delimiter);
                if (fields.Count <= colName) continue;

                string name = fields[colName].Trim();
                if (string.IsNullOrWhiteSpace(name)) continue;

                string? boardgameId = colBoardgameId != -1 && fields.Count > colBoardgameId ? fields[colBoardgameId].Trim() : null;
                string? copyName = colCopyName != -1 && fields.Count > colCopyName ? fields[colCopyName].Trim() : null;
                string? slug = colSlug != -1 && fields.Count > colSlug ? fields[colSlug].Trim() : null;
                string? linkLudopedia = colLudopedia != -1 && fields.Count > colLudopedia ? fields[colLudopedia].Trim() : null;
                string? comments = colComments != -1 && fields.Count > colComments ? fields[colComments].Trim() : null;

                decimal? aquisitionPrice = null;
                if (colAquisition != -1 && fields.Count > colAquisition)
                {
                    string rawPrice = fields[colAquisition].Replace("R$", "").Trim();
                    if (decimal.TryParse(rawPrice, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal p) ||
                        decimal.TryParse(rawPrice, NumberStyles.Any, new CultureInfo("pt-BR"), out p))
                    {
                        aquisitionPrice = p;
                    }
                }

                int? idBgg = null;
                if (colIdBgg != -1 && fields.Count > colIdBgg && int.TryParse(fields[colIdBgg].Trim(), out int bgg))
                {
                    idBgg = bgg;
                }

                string status = colStatus != -1 && fields.Count > colStatus ? fields[colStatus].Trim().ToLowerInvariant() : "available";
                bool isAvailable = status == "available" || status == "disponivel" || status == "disponível" || status == "1" || status == "true";

                if (isAvailable) availableCount++;
                else rentedCount++;

                // Preço de locação não consta na planilha (valores oficiais ficam no site Rejoy)
                decimal rentalPrice = 0.00m;

                // Categoria e jogadores padrão
                string category = "Geral";
                int minPlayers = 2;
                int maxPlayers = 4;

                var game = new BoardGame
                {
                    TenantId = _tenantContext.CurrentTenantId ?? Guid.Empty,
                    Name = name,
                    BoardgameId = boardgameId,
                    Category = category,
                    MinPlayers = minPlayers,
                    MaxPlayers = maxPlayers,
                    AquisitionPrice = aquisitionPrice,
                    RentalPrice = rentalPrice,
                    IdBgg = idBgg,
                    LinkLudopedia = linkLudopedia,
                    CopyName = copyName,
                    Slug = slug,
                    Status = status,
                    Comments = comments,
                    IsAvailable = isAvailable
                };

                gamesToInsert.Add(game);
                result.ImportedNames.Add(name);
            }

            if (gamesToInsert.Count == 0)
            {
                result.Success = false;
                result.Message = "Nenhum jogo válido foi identificado nas linhas do arquivo.";
                return result;
            }

            _dbContext.BoardGames.AddRange(gamesToInsert);
            await _dbContext.SaveChangesAsync();

            result.Success = true;
            result.GamesImported = gamesToInsert.Count;
            result.GamesAvailable = availableCount;
            result.GamesRented = rentedCount;
            result.Message = $"Sucesso! {gamesToInsert.Count} jogos importados para o catálogo ({availableCount} disponíveis e {rentedCount} alugados/indisponíveis).";

            _logger.LogInformation("Importação de CSV concluída com sucesso: {Count} jogos inseridos", gamesToInsert.Count);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao importar arquivo CSV de catálogo");
            result.Success = false;
            result.Message = $"Erro no processamento do arquivo: {ex.Message}";
            return result;
        }
    }

    private static List<string> ParseCsvLine(string line, char delimiter)
    {
        var fields = new List<string>();
        var current = new StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];

            if (c == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++; // Pula o segundo caractere de aspas escapadas
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (c == delimiter && !inQuotes)
            {
                fields.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }

        fields.Add(current.ToString());
        return fields;
    }
}
