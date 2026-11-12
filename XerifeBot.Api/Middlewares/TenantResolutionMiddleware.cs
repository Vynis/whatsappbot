using System.Security.Claims;
using XerifeBot.Api.Services;

namespace XerifeBot.Api.Middlewares;

public class TenantResolutionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<TenantResolutionMiddleware> _logger;

    public TenantResolutionMiddleware(RequestDelegate next, ILogger<TenantResolutionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, ITenantContext tenantContext)
    {
        // 1. Prioridade: Se o usuário estiver autenticado via JWT, extrai o tenant_id da claim
        if (context.User?.Identity?.IsAuthenticated == true)
        {
            var tenantClaim = context.User.FindFirst("tenant_id")?.Value
                ?? context.User.FindFirst("TenantId")?.Value;

            if (!string.IsNullOrWhiteSpace(tenantClaim) && Guid.TryParse(tenantClaim, out var tenantGuid))
            {
                tenantContext.SetTenantId(tenantGuid);
            }
        }
        // 2. Fallback: Cabeçalho opcional X-Tenant-Id (útil para chamadas de integração ou testes)
        else if (context.Request.Headers.TryGetValue("X-Tenant-Id", out var headerTenant) &&
                 !string.IsNullOrWhiteSpace(headerTenant) &&
                 Guid.TryParse(headerTenant.ToString(), out var headerGuid))
        {
            tenantContext.SetTenantId(headerGuid);
        }

        await _next(context);
    }
}
