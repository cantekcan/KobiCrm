using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using SutTicaretCRM.Api;
using SutTicaretCRM.Application;
using SutTicaretCRM.Infrastructure;
using SutTicaretCRM.Infrastructure.Persistence;

// Docker HEALTHCHECK için: uygulama kendi /health ucunu çağırıp çıkış kodu döner.
if (args.Contains("--healthcheck"))
{
    try
    {
        using var hc = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        var res = await hc.GetAsync("http://localhost:8080/health");
        return res.IsSuccessStatusCode ? 0 : 1;
    }
    catch { return 1; }
}

var builder = WebApplication.CreateBuilder(args);

// Yerel gizli ayarlar için .env dosyası (varsa) okunur; gerçek parolalar repoda tutulmaz.
EnvFile.LoadInto(builder.Configuration);

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

// ── Kimlik doğrulama (JWT) ──
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection("Jwt"));
builder.Services.AddSingleton<TokenService>();
var jwt = builder.Configuration.GetSection("Jwt").Get<JwtOptions>() ?? new JwtOptions();
if (string.IsNullOrWhiteSpace(jwt.Key) || jwt.Key.Length < 32 || jwt.Key.Contains("BURAYA", StringComparison.OrdinalIgnoreCase))
    throw new InvalidOperationException("Jwt:Key en az 32 karakterlik rastgele bir değer olmalıdır (.env içinde JWT_KEY). Yer tutucu değer kabul edilmez.");
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o =>
{
    o.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true, ValidIssuer = jwt.Issuer, ValidateAudience = true, ValidAudience = jwt.Issuer,
        ValidateIssuerSigningKey = true, IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key)),
        ValidateLifetime = true, ClockSkew = TimeSpan.FromMinutes(1),
        NameClaimType = System.Security.Claims.ClaimTypes.Name,
    };
});
builder.Services.AddAuthorizationBuilder().SetFallbackPolicy(new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = 429;
    o.AddPolicy("login", ctx => RateLimitPartition.GetFixedWindowLimiter(ctx.Connection.RemoteIpAddress?.ToString() ?? "?",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1) }));
});

builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddProblemDetails();
builder.Services.AddControllers().AddJsonOptions(o =>
{
    o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    o.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    o.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.Never;
});
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(o => o.MultipartBodyLengthLimit = 12 * 1024 * 1024);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(o =>
{
    o.SwaggerDoc("v1", new OpenApiInfo { Title = "Süt Ticareti CRM API", Version = "v1", Description = "Müşteri, satış, fatura taslağı, cari hesap ve tahsilat yönetimi." });
    o.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme { Type = SecuritySchemeType.Http, Scheme = "bearer", BearerFormat = "JWT", Description = "Giriş yanıtındaki token." });
    o.AddSecurityRequirement(doc => new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference("Bearer", doc)] = [] });
    o.CustomSchemaIds(t => t.FullName?.Replace('+', '.'));
});

var app = builder.Build();

// ── Başlangıç: migration + varsayılan ayarlar + ilk yönetici ──
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await DbSeeder.InitializeAsync(db, app.Configuration, app.Logger);
    if (args.Contains("reset-admin-password"))
        return await AdminCommands.ResetPasswordAsync(db, args, app.Logger);
    if (args.Contains("seed-demo"))
    {
        await DemoData.SeedAsync(scope.ServiceProvider, app.Logger);
        return 0;
    }
}

app.UseExceptionHandler();
app.Use(async (ctx, next) =>
{
    ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
    ctx.Response.Headers["X-Frame-Options"] = "DENY";
    ctx.Response.Headers["Referrer-Policy"] = "no-referrer";
    if (!app.Environment.IsDevelopment() && !ctx.Request.Path.StartsWithSegments("/swagger"))
        ctx.Response.Headers["Content-Security-Policy"] = "default-src 'self'; img-src 'self' data: blob:; style-src 'self' 'unsafe-inline'; frame-ancestors 'none'";
    await next();
});
if (!app.Environment.IsDevelopment() && app.Configuration.GetValue("Https:Redirect", false)) app.UseHttpsRedirection();
if (!app.Environment.IsDevelopment() && app.Configuration.GetValue("Https:Hsts", false)) app.UseHsts();

if (app.Environment.IsDevelopment() || app.Configuration.GetValue("Swagger:Enabled", false))
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseDefaultFiles();
app.UseStaticFiles();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapGet("/health", () => Results.Ok(new { status = "ok" })).AllowAnonymous();
// Bilinmeyen /api adresleri SPA sayfasını DEĞİL, JSON 404 döndürür.
app.MapFallback("/api/{**rest}", () => Results.Problem(statusCode: 404, title: "Böyle bir API adresi yok.")).AllowAnonymous();
app.MapFallbackToFile("index.html").AllowAnonymous();

app.Run();
return 0;

public partial class Program;
