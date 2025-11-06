using Microsoft.IdentityModel.Tokens;
using Stream_Postgres_WAL_Changes_Into_.NET_with_Npgsql_App.Dtos;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace Stream_Postgres_WAL_Changes_Into_.NET_with_Npgsql_App.Extensions;

public static class AuthenticationEndpoints
{
    public static void MapAuthenticationEndpoints(this WebApplication app, Microsoft.Extensions.Configuration.IConfiguration configuration)
    {
        app.MapPost("/auth/login", async (LoginRequest request) =>
        {
            var validUsername = configuration["Auth:Username"] ?? "admin";
            var validPassword = configuration["Auth:Password"] ?? "password";

            if (request.Username == validUsername && request.Password == validPassword)
            {
                var secret = configuration["Jwt:Secret"];
                if (string.IsNullOrEmpty(secret) || secret.Length < 32)
                {
                    return Results.Problem("JWT secret not configured properly", statusCode: 500);
                }

                var issuer = configuration["Jwt:Issuer"]!;
                var audience = configuration["Jwt:Audience"]!;
                var tokenHandler = new JwtSecurityTokenHandler();
                var key = Encoding.ASCII.GetBytes(secret);

                var tokenDescriptor = new SecurityTokenDescriptor
                {
                    Subject = new ClaimsIdentity(new[]
                    {
                        new Claim(ClaimTypes.Name, request.Username),
                        new Claim(ClaimTypes.Role, "Administrator"),
                        new Claim("user_id", Guid.NewGuid().ToString())
                    }),
                    Expires = DateTime.UtcNow.AddHours(1),
                    Issuer = issuer,
                    Audience = audience,
                    SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
                };

                var token = tokenHandler.CreateToken(tokenDescriptor);
                var tokenString = tokenHandler.WriteToken(token);

                return Results.Ok(new
                {
                    token = tokenString,
                    username = request.Username,
                    expiresIn = 3600
                });
            }

            return Results.Unauthorized();
        })
        .WithName("Login")
        .WithSummary("User Login")
        .WithDescription("Login to system with username and password")
        .WithTags("Authentication");
    }
}