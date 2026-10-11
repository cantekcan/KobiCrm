import { describe, expect, it } from "vitest";
import { addDays, calcLine, parseDecimal, round2, sumLines } from "./format";

// Bu örnekler tests/SutTicaretCRM.UnitTests/DomainTests.cs (DocumentCalculatorTests) ile BİREBİR aynıdır:
// arayüz önizlemesi ile sunucu hesabı aynı sonucu vermelidir.
describe("calcLine (sunucu DocumentCalculator ile aynı sonuçlar)", () => {
  it("yüzde + sabit indirim sonra KDV", () => {
    const r = calcLine({ quantity: 10, unitPrice: 200, discountPercent: 5, discountAmount: 50, vatRate: 10 });
    expect(r).toEqual({ gross: 2000, discount: 150, net: 1850, vat: 185, total: 2035 });
  });
  it("KDV kuruşa yarım yukarı yuvarlanır (3 × 10,05 → KDV 3,02)", () => {
    const r = calcLine({ quantity: 3, unitPrice: 10.05, discountPercent: 0, discountAmount: 0, vatRate: 10 });
    expect(r.net).toBe(30.15);
    expect(r.vat).toBe(3.02);
    expect(r.total).toBe(33.17);
  });
  it("belge toplamı yuvarlanmış satırların toplamıdır", () => {
    const a = calcLine({ quantity: 1, unitPrice: 0.05, discountPercent: 0, discountAmount: 0, vatRate: 10 });
    const t = sumLines([a, a]);
    expect(t.vat).toBe(0.02);
    expect(t.total).toBe(round2(t.net + t.vat));
  });
  it("demo senaryo: 10,5 kg × 195 − %10, KDV %10 = 2.027,03", () => {
    const r = calcLine({ quantity: 10.5, unitPrice: 195, discountPercent: 10, discountAmount: 0, vatRate: 10 });
    expect(r.discount).toBe(204.75);
    expect(r.net).toBe(1842.75);
    expect(r.vat).toBe(184.28);
    expect(r.total).toBe(2027.03);
  });
  it("KDV'siz satır", () => {
    expect(calcLine({ quantity: 2, unitPrice: 50, discountPercent: 0, discountAmount: 0, vatRate: 0 }).total).toBe(100);
  });
});

describe("parseDecimal (Türkçe girişler)", () => {
  it.each([
    ["1234,5", 1234.5], ["1.234,50", 1234.5], ["1,234.50", 1234.5], ["1234.5", 1234.5], ["12.5", 12.5], ["1.234", 1234], ["0,75", 0.75],
    ["100.000", 100000], ["100.000,25", 100000.25], ["  42 ", 42], ["-5,5", -5.5], ["1 234,5", 1234.5],
  ])("%s → %d", (input, expected) => expect(parseDecimal(input)).toBe(expected));

  it.each(["", "   ", "abc", "1,2,3", "12a", "--1", "1..2.3,4,5"])("geçersiz '%s' → null (sessizce 0 yapılmaz)", (input) => expect(parseDecimal(input)).toBeNull());

  it("null/undefined/NaN güvenli", () => {
    expect(parseDecimal(null)).toBeNull();
    expect(parseDecimal(undefined)).toBeNull();
    expect(parseDecimal(NaN)).toBeNull();
    expect(parseDecimal(7)).toBe(7);
  });
});

describe("tarih yardımcıları", () => {
  it("addDays ay/yıl sınırını aşar", () => {
    expect(addDays("2026-10-31", 1)).toBe("2026-11-01");
    expect(addDays("2026-12-31", 30)).toBe("2027-01-30");
    expect(addDays("2026-03-01", -1)).toBe("2026-02-28");
  });
});
