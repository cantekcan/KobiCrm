using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using SutTicaretCRM.Application;
using SutTicaretCRM.Application.Services;
using SutTicaretCRM.Domain;
using SutTicaretCRM.Infrastructure.Persistence;
using SutTicaretCRM.Infrastructure.Services;

namespace SutTicaretCRM.Api.Controllers;

// ───────────── Kimlik ─────────────

public record LoginRequest(string UserName, string Password);
public record LoginResponse(string Token, DateTime ExpiresUtc, string UserName, string DisplayName);
public record ChangePasswordRequest(string CurrentPassword, string NewPassword);

[ApiController, Route("api/auth")]
public class AuthController(AppDbContext db, TokenService tokens, IClock clock, IAuditLogger audit) : ControllerBase
{
    private const int MaxFailures = 5;

    [AllowAnonymous, HttpPost("login"), EnableRateLimiting("login")]
    public async Task<ActionResult<LoginResponse>> Login(LoginRequest r, CancellationToken ct)
    {
        const string generic = "Kullanıcı adı veya parola hatalı.";
        var name = (r.UserName ?? "").Trim();
        var user = await db.Users.FirstOrDefaultAsync(u => u.UserName == name, ct);
        if (user is null || !user.IsActive)
        {
            // Zamanlama farkıyla kullanıcı varlığı sızmasın.
            PasswordHasher.Verify(r.Password ?? "", PasswordHasher.Hash("x"));
            return Unauthorized(new ProblemDetails { Status = 401, Title = generic });
        }
        if (user.LockoutEndUtc is { } end && end > clock.UtcNow)
            return StatusCode(423, new ProblemDetails { Status = 423, Title = $"Çok fazla hatalı deneme. {Math.Ceiling((end - clock.UtcNow).TotalMinutes)} dakika sonra tekrar deneyin." });

        if (!PasswordHasher.Verify(r.Password ?? "", user.PasswordHash))
        {
            user.FailedLoginCount++;
            if (user.FailedLoginCount >= MaxFailures) { user.LockoutEndUtc = clock.UtcNow.AddMinutes(15); user.FailedLoginCount = 0; }
            await db.SaveChangesAsync(ct);
            return Unauthorized(new ProblemDetails { Status = 401, Title = generic });
        }
        user.FailedLoginCount = 0; user.LockoutEndUtc = null;
        await db.SaveChangesAsync(ct);
        var (token, exp) = tokens.Create(user);
        return new LoginResponse(token, exp, user.UserName, user.DisplayName);
    }

    [HttpGet("me")]
    public ActionResult<object> Me() => new { userName = User.Identity?.Name };

    [HttpPost("change-password")]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest r, CancellationToken ct)
    {
        var id = Guid.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
        var user = await db.Users.FirstAsync(u => u.Id == id, ct);
        if (!PasswordHasher.Verify(r.CurrentPassword ?? "", user.PasswordHash)) throw new ValidationFailedException("currentPassword", "Mevcut parola hatalı.");
        var problem = DbSeeder.ValidateAdminPassword(r.NewPassword);
        if (problem is not null) throw new ValidationFailedException("newPassword", problem.Replace("Yönetici parolası", "Parola"));
        user.PasswordHash = PasswordHasher.Hash(r.NewPassword);
        audit.Log("Parola değiştirildi", nameof(Domain.User), user.Id, "");
        await db.SaveChangesAsync(ct);
        return NoContent();
    }
}

// ───────────── Müşteriler ─────────────

[ApiController, Route("api/customers")]
public class CustomersController(CustomerService customers, LedgerService ledger, ProductService products, PaymentService payments) : ControllerBase
{
    [HttpGet]
    public Task<PagedResult<CustomerListItem>> List([FromQuery] string? search, [FromQuery] string? sort, [FromQuery] bool desc, [FromQuery] string? balance,
        [FromQuery] bool? active, [FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken ct = default) =>
        customers.ListAsync(search, sort, desc, balance, active, page, pageSize, ct);

    [HttpGet("{id:guid}")]
    public Task<CustomerDetailDto> Get(Guid id, CancellationToken ct) => customers.GetAsync(id, ct);

    [HttpPost]
    public async Task<ActionResult<CustomerDetailDto>> Create(CustomerRequest r, CancellationToken ct)
    {
        var c = await customers.CreateAsync(r, ct);
        return CreatedAtAction(nameof(Get), new { id = c.Id }, c);
    }

    [HttpPut("{id:guid}")]
    public Task<CustomerDetailDto> Update(Guid id, CustomerRequest r, CancellationToken ct) => customers.UpdateAsync(id, r, ct);

    [HttpPost("{id:guid}/opening-balance")]
    public Task<CustomerDetailDto> AddOpeningBalance(Guid id, OpeningBalanceRequest r, CancellationToken ct) => customers.AddOpeningBalanceAsync(id, r, ct);

    [HttpGet("{id:guid}/statement")]
    public Task<LedgerStatementDto> Statement(Guid id, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct) =>
        ledger.GetCustomerStatementAsync(id, from, to, ct);

    [HttpPost("{id:guid}/ledger-entries")]
    public Task<LedgerEntryDto> AddLedgerEntry(Guid id, ManualLedgerRequest r, CancellationToken ct) => ledger.AddManualEntryAsync(id, r, ct);

    [HttpGet("{id:guid}/product-history")]
    public Task<IReadOnlyList<CustomerProductHistoryItem>> ProductHistory(Guid id, CancellationToken ct) => customers.GetProductHistoryAsync(id, ct);

    [HttpGet("{id:guid}/open-sales")]
    public Task<IReadOnlyList<OpenSaleDto>> OpenSales(Guid id, CancellationToken ct) => payments.GetOpenSalesAsync(id, ct);

    [HttpGet("{id:guid}/prices")]
    public Task<IReadOnlyList<CustomerPriceDto>> Prices(Guid id, CancellationToken ct) => products.ListCustomerPricesAsync(id, null, ct);

    public record PriceRequest(decimal Price);

    [HttpPut("{id:guid}/prices/{productId:guid}")]
    public Task<CustomerPriceDto> SetPrice(Guid id, Guid productId, PriceRequest r, CancellationToken ct) => products.SetCustomerPriceAsync(id, productId, r.Price, ct);

    [HttpDelete("{id:guid}/prices/{productId:guid}")]
    public async Task<IActionResult> DeletePrice(Guid id, Guid productId, CancellationToken ct)
    {
        await products.DeleteCustomerPriceAsync(id, productId, ct);
        return NoContent();
    }

    [HttpGet("{id:guid}/price-lookup/{productId:guid}")]
    public Task<PriceLookupDto> LookupPrice(Guid id, Guid productId, CancellationToken ct) => products.LookupPriceAsync(id, productId, ct);
}

// ───────────── Ürünler ─────────────

[ApiController, Route("api/products")]
public class ProductsController(ProductService products) : ControllerBase
{
    [HttpGet]
    public Task<PagedResult<ProductDto>> List([FromQuery] string? search, [FromQuery] Guid? categoryId, [FromQuery] bool? active, [FromQuery] bool? lowStock,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default) =>
        products.ListAsync(search, categoryId, active, lowStock, page, pageSize, ct);

    [HttpGet("{id:guid}")]
    public Task<ProductDto> Get(Guid id, CancellationToken ct) => products.GetAsync(id, ct);

    [HttpPost]
    public async Task<ActionResult<ProductDto>> Create(ProductRequest r, CancellationToken ct)
    {
        var p = await products.CreateAsync(r, ct);
        return CreatedAtAction(nameof(Get), new { id = p.Id }, p);
    }

    [HttpPut("{id:guid}")]
    public Task<ProductDto> Update(Guid id, ProductRequest r, CancellationToken ct) => products.UpdateAsync(id, r, ct);

    [HttpGet("{id:guid}/customer-prices")]
    public Task<IReadOnlyList<CustomerPriceDto>> CustomerPrices(Guid id, CancellationToken ct) => products.ListCustomerPricesAsync(null, id, ct);

    [HttpGet("categories")]
    public Task<IReadOnlyList<CategoryDto>> Categories(CancellationToken ct) => products.ListCategoriesAsync(ct);

    public record CategoryRequest(string Name);

    [HttpPost("categories")]
    public Task<CategoryDto> CreateCategory(CategoryRequest r, CancellationToken ct) => products.CreateCategoryAsync(r.Name, ct);
}

// ───────────── Satışlar ─────────────

[ApiController, Route("api/sales")]
public class SalesController(SaleService sales) : ControllerBase
{
    [HttpGet]
    public Task<PagedResult<SaleListItem>> List([FromQuery] Guid? customerId, [FromQuery] SaleStatus? status, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to,
        [FromQuery] string? search, [FromQuery] bool? openBalance, [FromQuery] bool? invoiced, [FromQuery] bool? overdue,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken ct = default) =>
        sales.ListAsync(customerId, status, from, to, search, openBalance, invoiced, overdue, page, pageSize, ct);

    [HttpGet("{id:guid}")]
    public Task<SaleDto> Get(Guid id, CancellationToken ct) => sales.GetAsync(id, ct);

    [HttpPost]
    public async Task<ActionResult<SaleDto>> Create(SaleRequest r, CancellationToken ct)
    {
        var s = await sales.CreateAsync(r, ct);
        return CreatedAtAction(nameof(Get), new { id = s.Id }, s);
    }

    [HttpPut("{id:guid}")]
    public Task<SaleDto> UpdateDraft(Guid id, SaleRequest r, CancellationToken ct) => sales.UpdateDraftAsync(id, r, ct);

    [HttpPost("{id:guid}/approve")]
    public Task<SaleDto> Approve(Guid id, CancellationToken ct) => sales.ApproveAsync(id, ct);

    public record ReasonRequest(string Reason);

    [HttpPost("{id:guid}/cancel")]
    public Task<SaleDto> Cancel(Guid id, ReasonRequest r, CancellationToken ct) => sales.CancelAsync(id, r.Reason, ct);
}

// ───────────── Tahsilatlar ─────────────

[ApiController, Route("api/payments")]
public class PaymentsController(PaymentService payments) : ControllerBase
{
    [HttpGet]
    public Task<PagedResult<PaymentDto>> List([FromQuery] Guid? customerId, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] PaymentMethod? method,
        [FromQuery] PaymentStatus? status, [FromQuery] bool? unallocated, [FromQuery] string? search,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken ct = default) =>
        payments.ListAsync(customerId, from, to, method, status, unallocated, search, page, pageSize, ct);

    [HttpGet("{id:guid}")]
    public Task<PaymentDto> Get(Guid id, CancellationToken ct) => payments.GetAsync(id, ct);

    [HttpPost]
    public async Task<ActionResult<PaymentDto>> Create(PaymentRequest r, CancellationToken ct)
    {
        var p = await payments.CreateAsync(r, ct);
        return CreatedAtAction(nameof(Get), new { id = p.Id }, p);
    }

    [HttpPost("{id:guid}/allocate")]
    public Task<PaymentDto> Allocate(Guid id, List<AllocationRequest> allocations, CancellationToken ct) => payments.AllocateAsync(id, allocations, ct);

    [HttpPost("{id:guid}/reverse")]
    public Task<PaymentDto> Reverse(Guid id, SalesController.ReasonRequest r, CancellationToken ct) => payments.ReverseAsync(id, r.Reason, ct);

    [HttpPost("{id:guid}/receipt"), RequestSizeLimit(6 * 1024 * 1024)]
    public async Task<PaymentDto> UploadReceipt(Guid id, IFormFile file, CancellationToken ct)
    {
        await using var ms = new MemoryStream();
        await file.CopyToAsync(ms, ct);
        return await payments.AttachReceiptAsync(id, ms, file.FileName, ct);
    }

    [HttpGet("{id:guid}/receipt")]
    public async Task<IActionResult> DownloadReceipt(Guid id, CancellationToken ct)
    {
        var (stream, type, name) = await payments.OpenReceiptAsync(id, ct);
        return File(stream, type, name);
    }
}
