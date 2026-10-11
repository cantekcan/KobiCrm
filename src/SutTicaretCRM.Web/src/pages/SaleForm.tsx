import { useEffect, useMemo, useRef, useState } from "react";
import { useNavigate, useParams, useSearchParams } from "react-router-dom";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api, ApiError } from "../lib/api";
import type { CustomerDetail, PriceLookup, Product, Sale, Settings } from "../lib/types";
import { L } from "../lib/types";
import { Badge, Button, Card, Check, ErrorBlock, FormError, Money, NumberInput, Notice, PageHeader, Select, Spinner, TextArea, TextInput, useToast } from "../components/ui";
import { CustomerPicker, ProductPicker } from "../components/pickers";
import { addDays, calcLine, money, newKey, num, parseDecimal, sumLines, todayIso } from "../lib/format";

interface Line {
  key: string; productId: string; name: string; code: string; unit: string; qty: string; price: string; discPct: string; discAmt: string; vat: string;
  special: boolean; track: boolean; stock: number;
}

/**
 * Satış oluşturma — en sık kullanılan ekran. Burada gösterilen tutarlar ÖNİZLEMEDİR;
 * kesin tutarları sunucu hesaplar ve kayıt sonrası ekranda o değerler gösterilir.
 */
export function SaleFormPage() {
  const { id } = useParams();
  const editing = !!id;
  const [sp] = useSearchParams();
  const nav = useNavigate();
  const qc = useQueryClient();
  const toast = useToast();
  const keyRef = useRef(newKey());   // ağ hatasında tekrar gönderimde aynı anahtar → mükerrer satış oluşmaz

  const [customer, setCustomer] = useState<{ id: string; name: string; term: number } | null>(null);
  const [date, setDate] = useState(todayIso());
  const [type, setType] = useState<"Cash" | "Credit">("Credit");
  const [due, setDue] = useState("");
  const [dueTouched, setDueTouched] = useState(false);
  const [note, setNote] = useState("");
  const [lines, setLines] = useState<Line[]>([]);
  const [paidNow, setPaidNow] = useState(false);
  const [payAmount, setPayAmount] = useState("");
  const [payMethod, setPayMethod] = useState("Cash");
  const [payRef, setPayRef] = useState("");

  const { data: settings } = useQuery({ queryKey: ["settings"], queryFn: () => api.get<Settings>("/settings") });
  const { data: existing, isLoading: loadingExisting, error: existingError } = useQuery({ queryKey: ["sale", id], queryFn: () => api.get<Sale>(`/sales/${id}`), enabled: editing });
  const preCustomer = sp.get("musteri");
  const { data: pre } = useQuery({ queryKey: ["customer", preCustomer], queryFn: () => api.get<CustomerDetail>(`/customers/${preCustomer}`), enabled: !!preCustomer && !editing });

  useEffect(() => { if (pre && !customer) setCustomer({ id: pre.id, name: pre.name, term: pre.paymentTermDays }); }, [pre]); // eslint-disable-line
  useEffect(() => {
    if (!existing) return;
    setCustomer({ id: existing.customerId, name: existing.customerName, term: 0 });
    setDate(existing.saleDate); setType(existing.paymentType); setDue(existing.dueDate); setDueTouched(true); setNote(existing.note ?? "");
    setLines(existing.items.map((i) => ({ key: i.id, productId: i.productId, name: i.productName, code: i.productCode, unit: i.unit, qty: String(i.quantity), price: String(i.unitPrice), discPct: String(i.discountPercent), discAmt: String(i.discountAmount), vat: String(i.vatRate), special: false, track: false, stock: 0 })));
  }, [existing]);

  // Vade tarihi önerisi: satış tarihi + müşterinin vadesi (kullanıcı değiştirmediyse)
  useEffect(() => { if (type === "Credit" && !dueTouched && customer) setDue(addDays(date, customer.term)); }, [type, date, customer, dueTouched]);

  const calc = useMemo(() => lines.map((l) => {
    const q = parseDecimal(l.qty), p = parseDecimal(l.price), dp = parseDecimal(l.discPct) ?? 0, da = parseDecimal(l.discAmt) ?? 0, v = parseDecimal(l.vat) ?? 0;
    if (!q || q <= 0 || p === null || p < 0 || dp < 0 || dp > 100 || da < 0) return null;
    const r = calcLine({ quantity: q, unitPrice: p, discountPercent: dp, discountAmount: da, vatRate: v });
    return r.discount > r.gross ? null : r;
  }), [lines]);
  const valid = calc.length > 0 && calc.every(Boolean);
  const totals = sumLines(calc.filter(Boolean) as any);

  useEffect(() => { if (paidNow && payAmount === "") setPayAmount(String(totals.total).replace(".", ",")); }, [paidNow]); // eslint-disable-line

  async function addProduct(p: Product) {
    let price = p.salePrice, special = false;
    if (customer) {
      try { const r = await api.get<PriceLookup>(`/customers/${customer.id}/price-lookup/${p.id}`); price = r.price; special = r.isCustomerPrice; } catch { /* varsayılan fiyat */ }
    }
    setLines((ls) => {
      const ex = ls.find((l) => l.productId === p.id);
      if (ex) return ls.map((l) => l.key === ex.key ? { ...l, qty: String((parseDecimal(l.qty) ?? 0) + 1) } : l);
      return [...ls, { key: newKey(), productId: p.id, name: p.name, code: p.code, unit: p.unit, qty: "1", price: String(price).replace(".", ","), discPct: "0", discAmt: "0", vat: String(p.vatRate), special, track: p.trackStock, stock: p.stockQuantity }];
    });
  }
  async function reapplyPrices() {
    if (!customer) return;
    const next = await Promise.all(lines.map(async (l) => { try { const r = await api.get<PriceLookup>(`/customers/${customer.id}/price-lookup/${l.productId}`); return { ...l, price: String(r.price).replace(".", ","), special: r.isCustomerPrice }; } catch { return l; } }));
    setLines(next);
    toast.info("Fiyatlar müşteriye göre güncellendi.");
  }
  const upLine = (key: string, patch: Partial<Line>) => setLines((ls) => ls.map((l) => (l.key === key ? { ...l, ...patch } : l)));

  const pay = parseDecimal(payAmount);
  const save = useMutation({
    mutationFn: (approve: boolean) => {
      const body = {
        customerId: customer!.id, saleDate: date, paymentType: type, dueDate: type === "Credit" ? due || null : null, note: note || null, approve,
        clientRequestId: editing ? null : keyRef.current,
        items: lines.map((l) => ({ productId: l.productId, quantity: parseDecimal(l.qty), unitPrice: parseDecimal(l.price), discountPercent: parseDecimal(l.discPct) ?? 0, discountAmount: parseDecimal(l.discAmt) ?? 0, vatRate: parseDecimal(l.vat) })),
        payment: approve && paidNow && pay && pay > 0 ? { amount: pay, method: payMethod, bankReference: payRef || null } : null,
      };
      return editing ? api.put<Sale>(`/sales/${id}`, body) : api.post<Sale>("/sales", body);
    },
    onSuccess: (s, approve) => {
      qc.invalidateQueries();
      toast.success(approve ? `Satış kaydedildi ve onaylandı: ${s.number} · ${money(s.grandTotal)}` : `Taslak kaydedildi: ${s.number}`);
      s.warnings?.forEach((w) => toast.error(w));
      nav(`/satislar/${s.id}`);
    },
  });
  const err = save.error instanceof ApiError ? save.error : null;

  if (editing && loadingExisting) return <Spinner />;
  if (editing && existingError) return <ErrorBlock error={existingError} />;
  if (editing && existing && existing.status !== "Draft") return <Notice tone="warn">Yalnızca taslak satışlar düzenlenebilir.</Notice>;

  const canSave = !!customer && valid && (type === "Cash" || !due || due >= date) && (!paidNow || (pay !== null && pay > 0 && pay <= totals.total + 0.0001));
  const vatRates = settings?.vatRates ?? [0, 1, 10, 20];

  return (
    <div className="stack">
      <PageHeader title={editing ? `Taslak satışı düzenle` : "Yeni satış"} back={{ to: editing ? `/satislar/${id}` : "/satislar", label: "Satışlar" }} />

      <Card title="1 · Müşteri ve tarih">
        <div className="form-grid">
          <div className="span-2">
            <CustomerPicker value={customer} required onChange={(c) => { setCustomer(c ? { id: c.id, name: c.name, term: 0 } : null); if (c) api.get<CustomerDetail>(`/customers/${c.id}`).then((d) => setCustomer({ id: d.id, name: d.name, term: d.paymentTermDays })); }} error={err?.field("customerId")} autoFocus />
            {customer && lines.length > 0 && !editing && <div style={{ marginTop: ".5rem" }}><Button size="sm" variant="secondary" onClick={reapplyPrices}>Bu müşterinin özel fiyatlarını satırlara uygula</Button></div>}
          </div>
          <TextInput label="Satış tarihi" type="date" value={date} onChange={(e) => setDate(e.target.value)} error={err?.field("saleDate")} />
          <div>
            <div style={{ fontWeight: 600, fontSize: ".95rem", marginBottom: ".3rem" }}>Ödeme şekli</div>
            <div className="segmented" role="group" aria-label="Ödeme şekli">
              <button type="button" className={type === "Credit" ? "on" : ""} onClick={() => setType("Credit")}>Vadeli</button>
              <button type="button" className={type === "Cash" ? "on" : ""} onClick={() => { setType("Cash"); setPaidNow(true); }}>Peşin</button>
            </div>
          </div>
          {type === "Credit" && <TextInput label="Vade tarihi" type="date" value={due} onChange={(e) => { setDue(e.target.value); setDueTouched(true); }} error={err?.field("dueDate") ?? (due && due < date ? "Vade, satış tarihinden önce olamaz." : undefined)} hint={customer ? `Müşterinin vadesi: ${customer.term} gün` : undefined} />}
        </div>
      </Card>

      <Card title="2 · Ürünler">
        <ProductPicker onPick={addProduct} />
        {err?.field("items") && <Notice tone="error">{err.field("items")}</Notice>}
        {lines.length === 0 ? (
          <p className="muted" style={{ marginTop: ".8rem" }}>Yukarıdaki kutuya yazarak ürün arayın ve satışa ekleyin.</p>
        ) : (
          <div className="lines" style={{ marginTop: ".9rem" }}>
            {lines.map((l, idx) => {
              const c = calc[idx];
              const q = parseDecimal(l.qty) ?? 0;
              const lineErr = err?.field(`items[${idx}]`);
              return (
                <div className="line" key={l.key}>
                  <div className="line-title"><span>{l.name} <span className="muted small">{l.code}</span> {l.special && <Badge tone="info">Müşteriye özel fiyat</Badge>}</span>
                    <Button size="sm" variant="outline-danger" onClick={() => setLines((ls) => ls.filter((x) => x.key !== l.key))} aria-label={`${l.name} satırını sil`}>Sil</Button></div>
                  <NumberInput label={`Miktar (${l.unit})`} value={l.qty} onChange={(v) => upLine(l.key, { qty: v })} min={0.001} />
                  <NumberInput label="Birim fiyat (KDV hariç)" value={l.price} onChange={(v) => upLine(l.key, { price: v })} min={0} />
                  <NumberInput label="İndirim %" value={l.discPct} onChange={(v) => upLine(l.key, { discPct: v })} min={0} />
                  <NumberInput label="İndirim TL" value={l.discAmt} onChange={(v) => upLine(l.key, { discAmt: v })} min={0} />
                  <Select label="KDV" value={l.vat} onChange={(e) => upLine(l.key, { vat: e.target.value })}>{vatRates.map((r) => <option key={r} value={r}>%{r}</option>)}</Select>
                  <div className="line-total" style={{ gridColumn: "1 / -1" }}>
                    {c ? <>Satır toplamı: {money(c.total)} <span className="muted small">(KDV {money(c.vat)})</span></> : <span className="pos">Bu satırdaki değerleri kontrol edin</span>}
                    {l.track && q > l.stock && <div className="small pos">Dikkat: stok {num(l.stock)} {l.unit}, satılan {num(q)} {l.unit}</div>}
                    {lineErr && <div className="small pos">{lineErr}</div>}
                  </div>
                </div>
              );
            })}
          </div>
        )}
        {lines.length > 0 && (
          <div className="totals" style={{ marginTop: "1rem" }}>
            <div><span>Brüt tutar</span><Money value={totals.gross} /></div>
            <div><span>İndirim</span><Money value={-totals.discount} /></div>
            <div><span>KDV hariç toplam</span><Money value={totals.net} /></div>
            <div><span>KDV</span><Money value={totals.vat} /></div>
            <div className="grand"><span>Genel toplam</span><Money value={totals.total} /></div>
            <div className="muted small" style={{ display: "block" }}>Bu tutar önizlemedir; kesin tutar kayıtta sunucuda yeniden hesaplanır.</div>
          </div>
        )}
      </Card>

      <Card title="3 · Tahsilat ve not">
        <div className="stack">
          {type === "Cash" ? <Notice tone="info">Peşin satış: tahsilat bilgisini aşağıda girebilirsiniz. Girmezseniz satış tahsil edilmemiş görünür ve sonradan tahsilat girebilirsiniz.</Notice>
            : <Notice tone="info">Vadeli satış müşterinin cari hesabına <strong>borç</strong> olarak işlenir. İsterseniz peşinat/tahsilat da girebilirsiniz.</Notice>}
          {!editing || true ? (
            <>
              <Check checked={paidNow} onChange={setPaidNow}>{type === "Cash" ? "Tutar şimdi tahsil edildi" : "Satış anında (kısmen) tahsilat alındı"}</Check>
              {paidNow && (
                <div className="form-grid">
                  <NumberInput label="Tahsil edilen tutar (TL)" value={payAmount} onChange={setPayAmount} error={pay !== null && pay > totals.total + 0.0001 ? "Satış tutarından fazla olamaz." : err?.field("payment.amount")} />
                  <Select label="Ödeme yöntemi" value={payMethod} onChange={(e) => setPayMethod(e.target.value)}>{Object.entries(L.method).map(([k, v]) => <option key={k} value={k}>{v}</option>)}</Select>
                  {payMethod !== "Cash" && <TextInput label="Banka referansı / dekont no (isteğe bağlı)" value={payRef} onChange={(e) => setPayRef(e.target.value)} />}
                  <div className="muted small">Tahsilat yalnızca “Kaydet ve onayla” ile işlenir.</div>
                </div>
              )}
            </>
          ) : null}
          <TextArea label="Satış notu (isteğe bağlı)" value={note} onChange={(e) => setNote(e.target.value)} />
        </div>
      </Card>

      <FormError error={save.error} />
      {err?.status === 0 && <Notice tone="warn">Bağlantı kesildi. Aynı satışı tekrar kaydetmeniz güvenlidir: sistem mükerrer satış oluşturmaz.</Notice>}
      <div className="form-actions" style={{ position: "sticky", bottom: 0, background: "var(--bg)", padding: ".6rem 0", marginTop: 0 }}>
        <Button loading={save.isPending} disabled={!canSave} onClick={() => save.mutate(true)}>Kaydet ve onayla</Button>
        <Button variant="secondary" loading={save.isPending} disabled={!customer || !valid} onClick={() => save.mutate(false)}>Taslak olarak kaydet</Button>
        {!canSave && <span className="muted small" style={{ alignSelf: "center" }}>{!customer ? "Müşteri seçin" : lines.length === 0 ? "Ürün ekleyin" : !valid ? "Satırları kontrol edin" : ""}</span>}
      </div>
      <p className="muted small">“Kaydet ve onayla”: satış müşteri cari hesabına işlenir ve stok düşer. “Taslak”: henüz cari ve stoğu etkilemez.</p>
    </div>
  );
}
