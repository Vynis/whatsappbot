namespace XerifeBot.Api.Services;

public interface ITenantContext
{
    Guid? CurrentTenantId { get; }
    void SetTenantId(Guid tenantId);
}
