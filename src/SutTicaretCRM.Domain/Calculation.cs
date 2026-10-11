namespace SutTicaretCRM.Domain;

/// <summary>İş kuralı ihlali. API katmanında 422/409 olarak döner; mesajlar kullanıcıya gösterilir.</summary>
public class DomainException(string message) : Exception(message);

public static class Money
{
    public static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}

public readonly record struct LineInput(
    decimal Quantity,
    decimal UnitPrice,
    decimal DiscountPercent,
    decimal DiscountAmount,
    decimal VatRate);

public readonly record struct LineResult(decimal Gross, decimal Discount, decimal Net, decimal Vat, decimal Total);

public readonly record struct DocumentTotals(decimal Gross, decimal Discount, decimal Net, decimal Vat, decimal Total);

/// <summary>
/// Satış/fatura/alış satır hesapları. Fiyatlar KDV hariçtir.
/// Satır bazında 2 hane yuvarlanır (kuruş, yarım yukarı); belge toplamı satır toplamlarının toplamıdır.
/// Böylece satış ile ondan türetilen fatura her zaman aynı tutarı verir.
/// </summary>
public static class DocumentCalculator
{
    public static LineResult CalculateLine(LineInput line)
    {
        if (line.Quantity <= 0) throw new DomainException("Miktar sıfırdan büyük olmalıdır.");
        if (line.UnitPrice < 0) throw new DomainException("Birim fiyat negatif olamaz.");
        if (line.DiscountPercent < 0 || line.DiscountPercent > 100) throw new DomainException("İndirim oranı 0 ile 100 arasında olmalıdır.");
        if (line.DiscountAmount < 0) throw new DomainException("İndirim tutarı negatif olamaz.");
        if (line.VatRate < 0 || line.VatRate > 100) throw new DomainException("KDV oranı 0 ile 100 arasında olmalıdır.");

        var gross = Money.Round(line.Quantity * line.UnitPrice);
        var discount = Money.Round(gross * line.DiscountPercent / 100m) + Money.Round(line.DiscountAmount);
        if (discount > gross) throw new DomainException("İndirim, satır tutarından büyük olamaz.");
        var net = gross - discount;
        var vat = Money.Round(net * line.VatRate / 100m);
        return new LineResult(gross, discount, net, vat, net + vat);
    }

    public static DocumentTotals Sum(IEnumerable<LineResult> lines)
    {
        decimal gross = 0, discount = 0, net = 0, vat = 0, total = 0;
        foreach (var l in lines)
        {
            gross += l.Gross; discount += l.Discount; net += l.Net; vat += l.Vat; total += l.Total;
        }
        return new DocumentTotals(gross, discount, net, vat, total);
    }
}

/// <summary>T.C. kimlik no ve vergi kimlik no (VKN) biçim/sağlama doğrulaması. Bu, GİB'de kayıtlı olduğu anlamına gelmez.</summary>
public static class TurkishTaxIds
{
    public static bool IsValidTckn(string? value)
    {
        if (value is null || value.Length != 11 || !value.All(char.IsAsciiDigit) || value[0] == '0') return false;
        var d = value.Select(c => c - '0').ToArray();
        var odd = d[0] + d[2] + d[4] + d[6] + d[8];
        var even = d[1] + d[3] + d[5] + d[7];
        var d10 = (((odd * 7) - even) % 10 + 10) % 10;
        var d11 = d.Take(10).Sum() % 10;
        return d[9] == d10 && d[10] == d11;
    }

    public static bool IsValidVkn(string? value)
    {
        if (value is null || value.Length != 10 || !value.All(char.IsAsciiDigit)) return false;
        return ComputeVknCheckDigit(value[..9]) == value[9] - '0';
    }

    public static int ComputeVknCheckDigit(string first9)
    {
        var sum = 0;
        for (var i = 0; i < 9; i++)
        {
            var digit = first9[i] - '0';
            var tmp = (digit + (9 - i)) % 10;
            var tmp2 = (int)((tmp * Math.Pow(2, 9 - i)) % 9);
            if (tmp != 0 && tmp2 == 0) tmp2 = 9;
            sum += tmp2;
        }
        return (10 - sum % 10) % 10;
    }
}
