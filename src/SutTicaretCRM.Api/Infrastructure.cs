using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using SutTicaretCRM.Application;
using SutTicaretCRM.Domain;

namespace SutTicaretCRM.Api;

/// <summary>İstek sahibi kullanıcı. HTTP bağlamı yoksa (başlangıç, CLI) "sistem" kullanıcısı.</summary>
public sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public Guid? UserId =>
        Guid.TryParse(accessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    public string UserName => accessor.HttpContext?.User.Identity?.Name ?? "sistem";
}

public sealed class JwtOptions
{
    public string Key { get; set; } = "";
    public string Issuer { get; set; } = "SutTicaretCRM";
    public int ExpireHours { get; set; } = 12;
}

public sealed class TokenService(Microsoft.Extensions.Options.IOptions<JwtOptions> options)
{
    public (string Token, DateTime ExpiresUtc) Create(User user)
    {
        var o = options.Value;
        var expires = DateTime.UtcNow.AddHours(o.ExpireHours);
        var creds = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(o.Key)), SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(o.Issuer, o.Issuer,
            [new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()), new Claim(ClaimTypes.Name, user.UserName)],
            expires: expires, signingCredentials: creds);
        return (new JwtSecurityTokenHandler().WriteToken(token), expires);
    }
}

/// <summary>Beklenen hataları kullanıcıya anlaşılır Türkçe yanıtlara çevirir; beklenmeyenlerde ayrıntı sızdırmaz.</summary>
public sealed class ApiExceptionHandler(ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext ctx, Exception ex, CancellationToken ct)
    {
        var (status, title, errors) = ex switch
        {
            ValidationFailedException v => (400, v.Message, v.Errors),
            DomainException d => (422, d.Message, null),
            NotFoundException n => (404, n.Message, null),
            ConflictException c => (409, c.Message, null),
            DbUpdateConcurrencyException => (409, "Kayıt başka bir işlemle değiştirilmiş. Sayfayı yenileyip tekrar deneyin.", null),
            DbUpdateException { InnerException: PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } } =>
                (409, "Bu işlem daha önce yapılmış ya da aynı kayıt zaten var.", null),
            DbUpdateException { InnerException: PostgresException { SqlState: PostgresErrorCodes.CheckViolation } } =>
                (422, "İşlem veritabanı bütünlük kuralına aykırı olduğu için reddedildi.", null),
            OperationCanceledException => (499, "İstek iptal edildi.", null),
            BadHttpRequestException b => (400, b.Message, null),
            _ => (500, "Beklenmeyen bir hata oluştu. İşlem kaydedilmedi. Sorun sürerse kayıtlara (log) bakın.", null),
        };
        if (status >= 500) logger.LogError(ex, "Beklenmeyen hata {Path}", ctx.Request.Path);
        else logger.LogInformation("İstek reddedildi ({Status}) {Path}: {Message}", status, ctx.Request.Path, ex.Message);

        ctx.Response.StatusCode = status;
        var problem = errors is null
            ? new ProblemDetails { Status = status, Title = title }
            : new ValidationProblemDetails(errors.ToDictionary(k => k.Key, v => v.Value)) { Status = status, Title = title };
        await ctx.Response.WriteAsJsonAsync(problem, problem.GetType(), cancellationToken: ct);
        return true;
    }
}
