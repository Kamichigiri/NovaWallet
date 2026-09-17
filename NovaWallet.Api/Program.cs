using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using NovaWallet.Api;
using NovaWallet.Api.ServiceExtentions;
using NovaWallet.Application.Contracts;
using NovaWallet.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.ConfigureTelemetry(builder.Configuration);
builder.Services.ConfigureSwagger();

builder.Services.AddDbContext<NovaWalletDbContext>(o => o.UseSqlServer(config.GetConnectionString("NovaWalletDb")));
builder.Services.AddScoped<IWalletService, WalletService>();
builder.Services.AddSingleton<ISystemClock, SystemClock>();
var jwtKey = config["Jwt:SigningKey"] ?? throw new InvalidOperationException("Jwt:SigningKey is required.");
var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o =>
{
    o.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true, ValidIssuer = config["Jwt:Issuer"],
        ValidateAudience = true, ValidAudience = config["Jwt:Audience"],
        ValidateIssuerSigningKey = true, IssuerSigningKey = key,
        ValidateLifetime = true, ClockSkew = TimeSpan.FromSeconds(1000)
    };
});
builder.Services.AddAuthorization();
builder.Services.AddIpRateLimiter(builder.Configuration);

var app = builder.Build();
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<NovaWalletDbContext>();
    await db.Database.EnsureCreatedAsync();
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
app.UseHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = async (context, report) =>
    {
        context.Response.ContentType = "text/plain";

        var response = report.Entries.ToList();

        foreach (var item in response)
        {
            await context.Response.WriteAsync($"{item.Value.Status} {item.Key}, {item.Value.Description}\n");
        }
    }
});

var options = new ForwardedHeadersOptions
{
    ForwardedHeaders =
    ForwardedHeaders.XForwardedFor |
    ForwardedHeaders.XForwardedProto
};

app.UseForwardedHeaders(options);

app.UseRateLimiter();

app.UseMiddleware<ProblemDetailsMiddleware>();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

// Development-only mock issuer endpoint. Remove/disable outside development.
app.MapPost("/dev/auth/token", (TokenRequest req, IConfiguration cfg) =>
{
    if (!app.Environment.IsDevelopment()) return Results.NotFound();
    var claims = new[] { new Claim(JwtRegisteredClaimNames.Sub, req.Subject), new Claim("scope", "wallet:write") };
    var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
    var token = new JwtSecurityToken(cfg["Jwt:Issuer"], cfg["Jwt:Audience"], claims, expires: DateTime.UtcNow.AddHours(1), signingCredentials: creds);
    return Results.Ok(new { access_token = new JwtSecurityTokenHandler().WriteToken(token), token_type = "Bearer" });
}).AllowAnonymous();

app.Run();

public record TokenRequest(string Subject);