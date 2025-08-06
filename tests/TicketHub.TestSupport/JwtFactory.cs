using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace TicketHub.TestSupport;

public static class JwtFactory
{
    public const string Key = "test-signing-key-0123456789abcdef0123456789";

    public static string Create(Guid userId, string role, string? email = null) =>
        new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
            "tickethub-identity", "tickethub",
            [new Claim("sub", userId.ToString()), new Claim("role", role), new Claim("email", email ?? $"{role.ToLowerInvariant()}@test.local"), new Claim("name", role)],
            DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow.AddMinutes(15),
            new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Key)), SecurityAlgorithms.HmacSha256)));
}
