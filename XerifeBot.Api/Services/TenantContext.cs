namespace XerifeBot.Api.Services;

public class TenantContext : ITenantContext
{
    private Guid? _tenantId;

    public Guid? CurrentTenantId => _tenantId;

    public void SetTenantId(Guid tenantId)
    {
        _tenantId = tenantId;
    }
}
