// Para, tarih ve ondalık sayı yardımcıları. Tüm hesaplar tam sayı kuruş üzerinden yapılır (kayan nokta hatası olmasın).

const moneyFmt = new Intl.NumberFormat("tr-TR", { style: "currency", currency: "TRY", minimumFractionDigits: 2, maximumFractionDigits: 2 });
const numFmt = new Intl.NumberFormat("tr-TR", { minimumFractionDigits: 0, maximumFractionDigits: 3 });

export const money = (v: number | null | undefined) => (v === null || v === undefined ? "—" : moneyFmt.format(v));
export const num = (v: number | null | undefined) => (v === null || v === undefined ? "—" : numFmt.format(v));

/** "2026-10-11" → "11.10.2026" */
export function dateTr(v: string | null | undefined): string {
  if (!v) return "—";
  const [y, m, d] = v.slice(0, 10).split("-");
  return `${d}.${m}.${y}`;
}

export function dateTimeTr(v: string | null | undefined): string {
  if (!v) return "—";
  const dt = new Date(v.endsWith("Z") || v.includes("+") ? v : v + "Z");
  return dt.toLocaleString("tr-TR", { dateStyle: "short", timeStyle: "short" });
}

/** Bugünün tarihi (cihazın yerel saati) yyyy-MM-dd. */
export function todayIso(): string {
  const d = new Date();
  const p = (n: number) => String(n).padStart(2, "0");
  return `${d.getFullYear()}-${p(d.getMonth() + 1)}-${p(d.getDate())}`;
}

export function addDays(iso: string, days: number): string {
  const [y, m, d] = iso.split("-").map(Number);
  const dt = new Date(y, m - 1, d + days);
  const p = (n: number) => String(n).padStart(2, "0");
  return `${dt.getFullYear()}-${p(dt.getMonth() + 1)}-${p(dt.getDate())}`;
}

export function firstOfMonthIso(): string {
  const t = todayIso();
  return t.slice(0, 8) + "01";
}

/**
 * Kullanıcının yazdığı sayıyı çözer. "1.234,50", "1234,5", "1234.5", "1 234,5" desteklenir.
 * Geçersizse null döner (sessizce 0 yapılmaz).
 */
export function parseDecimal(input: string | number | null | undefined): number | null {
  if (input === null || input === undefined) return null;
  if (typeof input === "number") return Number.isFinite(input) ? input : null;
  let s = input.trim().replace(/\s/g, "");
  if (s === "") return null;
  if (!/^[+-]?[\d.,]+$/.test(s)) return null;
  const lastComma = s.lastIndexOf(",");
  const lastDot = s.lastIndexOf(".");
  if (lastComma >= 0 && lastDot >= 0) {
    // Hangisi sonradan geliyorsa ondalık ayracıdır
    if (lastComma > lastDot) s = s.replace(/\./g, "").replace(",", ".");
    else s = s.replace(/,/g, "");
  } else if (lastComma >= 0) {
    s = s.replace(/\./g, "");
    if ((s.match(/,/g) ?? []).length > 1) return null;
    s = s.replace(",", ".");
  } else if (lastDot >= 0) {
    const parts = s.split(".");
    // "1.234" → binlik ayracı sayılır (3 haneli tek grup); "12.5" ondalık
    if (parts.length > 2 || (parts.length === 2 && parts[1].length === 3 && parts[0].length <= 3 && parts[0] !== "0")) s = parts.join("");
  }
  const n = Number(s);
  return Number.isFinite(n) ? n : null;
}

/** Kuruşa yuvarla (yarım yukarı, sıfırdan uzağa) — sunucudaki Math.Round(AwayFromZero) ile aynı. */
export const round2 = (v: number) => Math.sign(v) * Math.round((Math.abs(v) + Number.EPSILON) * 100) / 100;

export interface LineCalcInput { quantity: number; unitPrice: number; discountPercent: number; discountAmount: number; vatRate: number }
export interface LineCalc { gross: number; discount: number; net: number; vat: number; total: number }

/** Önizleme hesabı. KESİN tutarı sunucu hesaplar; bu yalnızca kullanıcıya anında geri bildirim içindir. */
export function calcLine(l: LineCalcInput): LineCalc {
  const gross = round2(l.quantity * l.unitPrice);
  const discount = round2(round2((gross * l.discountPercent) / 100) + round2(l.discountAmount));
  const net = round2(gross - discount);
  const vat = round2((net * l.vatRate) / 100);
  return { gross, discount, net, vat, total: round2(net + vat) };
}

export function sumLines(lines: LineCalc[]): LineCalc {
  return lines.reduce(
    (a, l) => ({ gross: round2(a.gross + l.gross), discount: round2(a.discount + l.discount), net: round2(a.net + l.net), vat: round2(a.vat + l.vat), total: round2(a.total + l.total) }),
    { gross: 0, discount: 0, net: 0, vat: 0, total: 0 },
  );
}

export function newKey(): string {
  return typeof crypto !== "undefined" && "randomUUID" in crypto ? crypto.randomUUID() : "xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx".replace(/[xy]/g, (c) => { const r = (Math.random() * 16) | 0; return (c === "x" ? r : (r & 3) | 8).toString(16); });
}
