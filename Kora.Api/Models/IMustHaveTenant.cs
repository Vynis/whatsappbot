namespace Kora.Api.Models;

public interface IMustHaveTenant
{
    Guid TenantId { get; set; }
}
