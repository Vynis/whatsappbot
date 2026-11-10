using Microsoft.AspNetCore.Http;
using XerifeBot.Api.Dtos;

namespace XerifeBot.Api.Services;

public interface ICatalogImportService
{
    Task<UploadCatalogResultDto> ImportCatalogAsync(IFormFile file, bool clearExisting = true);
    Task<UploadCatalogResultDto> ImportCsvTextAsync(string csvContent, bool clearExisting = true);
    Task<bool> ClearCatalogAsync();
}
