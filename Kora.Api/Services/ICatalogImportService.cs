using Microsoft.AspNetCore.Http;
using Kora.Api.Dtos;

namespace Kora.Api.Services;

public interface ICatalogImportService
{
    Task<UploadCatalogResultDto> ImportCatalogAsync(IFormFile file, bool clearExisting = true);
    Task<UploadCatalogResultDto> ImportCsvTextAsync(string csvContent, bool clearExisting = true);
    Task<bool> ClearCatalogAsync();
}
