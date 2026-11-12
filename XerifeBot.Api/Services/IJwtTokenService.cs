using XerifeBot.Api.Models;

namespace XerifeBot.Api.Services;

public interface IJwtTokenService
{
    string GenerateToken(User user, Tenant tenant);
}
