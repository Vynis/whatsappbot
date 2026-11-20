using Kora.Api.Models;

namespace Kora.Api.Services;

public interface IJwtTokenService
{
    string GenerateToken(User user, Tenant tenant);
}
