namespace XerifeBot.Api.Models;

public interface IMustHaveTenant
{
    Guid TenantId { get; set; }
}
