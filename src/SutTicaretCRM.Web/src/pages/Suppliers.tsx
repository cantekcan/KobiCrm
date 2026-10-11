import { FormEvent, useEffect, useState } from "react";
import { Link, useNavigate, useParams, useSearchParams } from "react-router-dom";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "../lib/api";
import type { Paged, Product, Purchase, Settings, Supplier, SupplierListItem } from "../lib/types";
import { L } from "../lib/types";
import { Badge, Button, Card, Check, DataTable, EmptyState, ErrorBlock, FormError, KV, LinkButton, Modal, Money, NumberInput, Notice, PageHeader, Select, Spinner, TextArea, TextInput, useConfirm, useToast } from "../components/ui";
import { ProductPicker, SupplierSelect } from "../components/pickers";
import { calcLine, dateTr, money, newKey, num, parseDecimal, sumLines, todayIso } from "../lib/format";

export function SuppliersPage() {
  const { data, isLoading, error, refetch } = useQuery({ queryKey: ["suppliers"], queryFn: () => api.get<SupplierListItem[]>("/suppliers") });
  return (
    <div className="stack">
      <PageHeader title="Tedarikçiler" subtitle="Alışlar ve tedarikçilere olan borçlarınız" actions={<><LinkButton to="/tedarikciler/yeni">+ Tedarikçi</LinkButton><LinkButton to="/alislar/yeni" variant="secondary">+ Alış gir</LinkButton></>} />
      <Card flush>
        {isLoading ? <Spinner /> : error ? <ErrorBlock error={error} onRetry={refetch} /> : (
          <DataTable rows={data!} rowKey={(s) => s.id} rowHref={(s) => `/tedarikciler/${s.id}`} empty={<EmptyState title="Henüz tedarikçi yok" text="Süt/ürün aldığınız kişi ve firmaları ekleyin; alışlarla stok artar ve borcunuz takip edilir." action={<LinkButton to="/tedarikciler/yeni">+ Tedarikçi ekle</LinkButton>} />}
            columns={[
              { header: "Tedarikçi", title: true, cell: (s) => <><strong>{s.name}</strong> {!s.isActive && <Badge>Pasif</Badge>}<div className="muted small">{s.phone ?? ""}</div></> },
              { header: "Borcumuz", num: true, cell: (s) => <strong className={s.balance > 0 ? "pos" : ""}><Money value={s.balance} /></strong> },
              { header: "Son alış", cell: (s) => dateTr(s.lastPurchaseDate) },
            ]} />
        )}
      </Card>
    </div>
  );
}

export function SupplierFormPage() {
  const { id } = useParams();
  const editing = !!id;
  const nav = useNavigate();
  const qc = useQueryClient();
  const toast = useToast();
  const [f, setF] = useState({ name: "", taxOffice: "", taxNumber: "", phone: "", email: "", address: "", note: "", isActive: true });
  const [withOpening, setWithOpening] = useState(false);
  const [op, setOp] = useState({ amount: "", date: todayIso(), description: "Geçmiş dönemden devreden tedarikçi borcu" });
  const { data, isLoading } = useQuery({ queryKey: ["supplier", id], queryFn: () => api.get<Supplier>(`/suppliers/${id}`), enabled: editing });
  useEffect(() => { if (data) setF({ name: data.name, taxOffice: data.taxOffice ?? "", taxNumber: data.taxNumber ?? "", phone: data.phone ?? "", email: data.email ?? "", address: data.address ?? "", note: data.note ?? "", isActive: data.isActive }); }, [data]);
  const amt = parseDecimal(op.amount);
  const save = useMutation({
    mutationFn: () => {
      const body: any = { ...f, taxOffice: f.taxOffice || null, taxNumber: f.taxNumber || null, phone: f.phone || null, email: f.email || null, address: f.address || null, note: f.note || null };
      if (!editing && withOpening && amt) body.openingBalance = { amount: amt, date: op.date, description: op.description };
      return editing ? api.put<Supplier>(`/suppliers/${id}`, body) : api.post<Supplier>("/suppliers", body);
    },
    onSuccess: (s) => { qc.invalidateQueries(); toast.success("Tedarikçi kaydedildi."); nav(`/tedarikciler/${s.id}`); },
  });
  if (editing && isLoading) return <Spinner />;
  return (
    <form className="stack" onSubmit={(e: FormEvent) => { e.preventDefault(); save.mutate(); }}>
      <PageHeader title={editing ? "Tedarikçiyi düzenle" : "Yeni tedarikçi"} back={{ to: "/tedarikciler", label: "Tedarikçiler" }} />
      <Card><div className="form-grid">
        <TextInput className="span-2" label="Tedarikçi adı" required value={f.name} onChange={(e) => setF({ ...f, name: e.target.value })} autoFocus />
        <TextInput label="Telefon" value={f.phone} onChange={(e) => setF({ ...f, phone: e.target.value })} />
        <TextInput label="E-posta" value={f.email} onChange={(e) => setF({ ...f, email: e.target.value })} />
        <TextInput label="Vergi dairesi" value={f.taxOffice} onChange={(e) => setF({ ...f, taxOffice: e.target.value })} />
        <TextInput label="Vergi / T.C. kimlik no" inputMode="numeric" value={f.taxNumber} onChange={(e) => setF({ ...f, taxNumber: e.target.value.replace(/\D/g, "") })} />
        <TextInput className="span-2" label="Adres" value={f.address} onChange={(e) => setF({ ...f, address: e.target.value })} />
        <TextArea className="span-2" label="Not" value={f.note} onChange={(e) => setF({ ...f, note: e.target.value })} />
        <div className="span-2"><Check checked={f.isActive} onChange={(v) => setF({ ...f, isActive: v })}>Aktif</Check></div>
      </div></Card>
      {!editing && <Card title="Geçmişten kalan borcumuz (açılış)"><div className="stack-sm">
        <Check checked={withOpening} onChange={setWithOpening}>Bu tedarikçiye uygulamaya geçmeden önceki borcumuz var</Check>
        {withOpening && <div className="form-grid"><NumberInput label="Borcumuz (TL)" value={op.amount} onChange={(v) => setOp({ ...op, amount: v })} /><TextInput label="Açılış tarihi" type="date" value={op.date} onChange={(e) => setOp({ ...op, date: e.target.value })} /></div>}
      </div></Card>}
      <FormError error={save.error} />
      <div className="form-actions"><Button type="submit" loading={save.isPending} disabled={!f.name.trim()}>Kaydet</Button><Button variant="secondary" onClick={() => nav(-1)}>Vazgeç</Button></div>
    </form>
  );
}

export function SupplierDetailPage() {
  const { id } = useParams();
  const qc = useQueryClient();
  const toast = useToast();
  const confirm = useConfirm();
  const [pay, setPay] = useState(false);
  const { data: s, isLoading, error, refetch } = useQuery({ queryKey: ["supplier", id], queryFn: () => api.get<Supplier>(`/suppliers/${id}`) });
  const { data: purchases } = useQuery({ queryKey: ["purchases", id], queryFn: () => api.get<Paged<Purchase>>("/purchases", { supplierId: id, pageSize: 50 }) });
  const approve = useMutation({ mutationFn: (pid: string) => api.post(`/purchases/${pid}/approve`), onSuccess: () => { qc.invalidateQueries(); toast.success("Alış onaylandı: stok arttı, tedarikçi borcu işlendi."); } });
  const cancel = useMutation({ mutationFn: (v: { pid: string; reason: string }) => api.post(`/purchases/${v.pid}/cancel`, { reason: v.reason }), onSuccess: () => { qc.invalidateQueries(); toast.success("Alış iptal edildi (ters kayıt)."); } });
  if (isLoading) return <Spinner />;
  if (error || !s) return <ErrorBlock error={error} onRetry={refetch} />;
  return (
    <div className="stack">
      <PageHeader title={s.name} back={{ to: "/tedarikciler", label: "Tedarikçiler" }} subtitle={s.phone ?? undefined}
        actions={<><LinkButton to={`/alislar/yeni?tedarikci=${s.id}`}>+ Alış gir</LinkButton><Button variant="secondary" onClick={() => setPay(true)}>Ödeme yap</Button><LinkButton to={`/cari/tedarikci/${s.id}`} variant="secondary">Cari ekstre</LinkButton><LinkButton to={`/tedarikciler/${s.id}/duzenle`} variant="ghost">Düzenle</LinkButton></>} />
      <div className="grid cols-3 cols-2-mobile"><div className={`stat ${s.balance > 0 ? "warn" : ""}`}><div className="label">Tedarikçiye borcumuz</div><div className="value">{money(s.balance)}</div></div></div>
      <Card title="Alışlar" flush>
        <DataTable rows={purchases?.items ?? []} rowKey={(p) => p.id} empty={<EmptyState title="Alış yok" />}
          columns={[
            { header: "Alış", title: true, cell: (p) => <><strong>{p.number}</strong><div className="muted small">{dateTr(p.documentDate)}{p.supplierDocumentNo ? ` · ${p.supplierDocumentNo}` : ""}</div></> },
            { header: "Kalemler", cell: (p) => <span className="small">{p.items.map((i) => `${i.productName} ${num(i.quantity)} ${i.unit}`).join(", ")}</span> },
            { header: "Tutar", num: true, cell: (p) => <Money value={p.grandTotal} /> },
            { header: "Durum", cell: (p) => <Badge tone={p.status === "Approved" ? "good" : p.status === "Cancelled" ? "bad" : "neutral"}>{L.purchaseStatus[p.status]}</Badge> },
            { header: "", cell: (p) => <div className="row">
              {p.status === "Draft" && <Button size="sm" onClick={() => approve.mutate(p.id)}>Onayla</Button>}
              {p.status !== "Cancelled" && <Button size="sm" variant="outline-danger" onClick={async () => { const r = await confirm({ title: "Alışı iptal et", danger: true, confirmLabel: "İptal et", reasonLabel: "Nedeni (zorunlu)", message: "Alış iptal edilir: tedarikçi cari hesabına ters kayıt düşer ve stok geri alınır. Kayıt silinmez." }); if (r.ok) cancel.mutate({ pid: p.id, reason: r.reason }); }}>İptal</Button>}
            </div> },
          ]} />
      </Card>
      <Card title="Bilgiler"><KV items={[["Vergi dairesi / no", [s.taxOffice, s.taxNumber].filter(Boolean).join(" / ") || "—"], ["E-posta", s.email], ["Adres", s.address], ["Not", s.note]]} /></Card>
      <FormError error={approve.error ?? cancel.error} />
      {pay && <PayModal supplier={s} onClose={() => setPay(false)} />}
    </div>
  );
}

function PayModal({ supplier, onClose }: { supplier: Supplier; onClose: () => void }) {
  const qc = useQueryClient();
  const toast = useToast();
  const [f, setF] = useState({ amount: "", date: todayIso(), method: "BankTransfer", note: "" });
  const a = parseDecimal(f.amount);
  const m = useMutation({ mutationFn: () => api.post(`/suppliers/${supplier.id}/payments`, { amount: a, date: f.date, method: f.method, note: f.note || null }), onSuccess: () => { qc.invalidateQueries(); toast.success(`${money(a)} ödeme tedarikçi cari hesabına işlendi.`); onClose(); } });
  return (
    <Modal title={`Tedarikçiye ödeme — ${supplier.name}`} onClose={onClose} footer={<><Button variant="secondary" onClick={onClose}>Vazgeç</Button><Button loading={m.isPending} disabled={!a || a <= 0} onClick={() => m.mutate()}>Ödemeyi kaydet</Button></>}>
      <div className="stack">
        <Notice tone="info">Mevcut borcunuz: <strong>{money(supplier.balance)}</strong>. Ödeme borcunuzu azaltır.</Notice>
        <NumberInput label="Ödenen tutar (TL)" value={f.amount} onChange={(v) => setF({ ...f, amount: v })} required />
        <TextInput label="Tarih" type="date" value={f.date} onChange={(e) => setF({ ...f, date: e.target.value })} />
        <Select label="Yöntem" value={f.method} onChange={(e) => setF({ ...f, method: e.target.value })}>{Object.entries(L.method).map(([k, v]) => <option key={k} value={k}>{v}</option>)}</Select>
        <TextInput label="Not" value={f.note} onChange={(e) => setF({ ...f, note: e.target.value })} />
        <FormError error={m.error} />
      </div>
    </Modal>
  );
}

interface PLine { key: string; productId: string; name: string; unit: string; qty: string; price: string; vat: string }

export function PurchaseFormPage() {
  const nav = useNavigate();
  const qc = useQueryClient();
  const toast = useToast();
  const [sp] = useSearchParams();
  const key = useState(newKey())[0];
  const [supplierId, setSupplierId] = useState(sp.get("tedarikci") ?? "");
  const [date, setDate] = useState(todayIso());
  const [due, setDue] = useState("");
  const [docNo, setDocNo] = useState("");
  const [note, setNote] = useState("");
  const [lines, setLines] = useState<PLine[]>([]);
  const { data: settings } = useQuery({ queryKey: ["settings"], queryFn: () => api.get<Settings>("/settings") });
  const calc = lines.map((l) => { const q = parseDecimal(l.qty), p = parseDecimal(l.price); return q && q > 0 && p !== null && p >= 0 ? calcLine({ quantity: q, unitPrice: p, discountPercent: 0, discountAmount: 0, vatRate: parseDecimal(l.vat) ?? 0 }) : null; });
  const valid = lines.length > 0 && calc.every(Boolean);
  const t = sumLines(calc.filter(Boolean) as any);
  const add = (p: Product) => setLines((ls) => ls.some((l) => l.productId === p.id) ? ls : [...ls, { key: newKey(), productId: p.id, name: p.name, unit: p.unit, qty: "1", price: String(p.purchasePrice).replace(".", ","), vat: String(p.vatRate) }]);
  const save = useMutation({
    mutationFn: (approve: boolean) => api.post<Purchase>("/purchases", { supplierId, documentDate: date, dueDate: due || null, supplierDocumentNo: docNo || null, note: note || null, approve, clientRequestId: key,
      items: lines.map((l) => ({ productId: l.productId, quantity: parseDecimal(l.qty), unitPrice: parseDecimal(l.price), vatRate: parseDecimal(l.vat) })) }),
    onSuccess: (p, approve) => { qc.invalidateQueries(); toast.success(approve ? `Alış kaydedildi: stok arttı, ${money(p.grandTotal)} tedarikçi borcuna işlendi.` : "Alış taslak olarak kaydedildi."); nav(`/tedarikciler/${p.supplierId}`); },
  });
  return (
    <div className="stack">
      <PageHeader title="Alış gir" back={{ to: "/tedarikciler", label: "Tedarikçiler" }} />
      <Card title="Belge"><div className="form-grid">
        <SupplierSelect value={supplierId} onChange={setSupplierId} />
        <TextInput label="Belge tarihi" type="date" value={date} onChange={(e) => setDate(e.target.value)} />
        <TextInput label="Tedarikçi fatura/irsaliye no" value={docNo} onChange={(e) => setDocNo(e.target.value)} />
        <TextInput label="Vade tarihi (isteğe bağlı)" type="date" value={due} onChange={(e) => setDue(e.target.value)} />
      </div></Card>
      <Card title="Ürünler">
        <ProductPicker onPick={add} showStock />
        <div className="lines" style={{ marginTop: ".8rem" }}>{lines.map((l, i) => (
          <div className="line" key={l.key} style={{ gridTemplateColumns: "2fr repeat(3, 1fr) auto" }}>
            <div className="line-title" style={{ gridColumn: "1 / -1" }}><span>{l.name}</span><Button size="sm" variant="outline-danger" onClick={() => setLines(lines.filter((x) => x.key !== l.key))}>Sil</Button></div>
            <NumberInput label={`Miktar (${l.unit})`} value={l.qty} onChange={(v) => setLines(lines.map((x) => x.key === l.key ? { ...x, qty: v } : x))} min={0.001} />
            <NumberInput label="Birim alış fiyatı (KDV hariç)" value={l.price} onChange={(v) => setLines(lines.map((x) => x.key === l.key ? { ...x, price: v } : x))} min={0} />
            <Select label="KDV" value={l.vat} onChange={(e) => setLines(lines.map((x) => x.key === l.key ? { ...x, vat: e.target.value } : x))}>{(settings?.vatRates ?? [0, 1, 10, 20]).map((r) => <option key={r} value={r}>%{r}</option>)}</Select>
            <div className="line-total">{calc[i] ? money(calc[i]!.total) : "—"}</div>
          </div>))}</div>
        {lines.length > 0 && <div className="totals" style={{ marginTop: "1rem" }}><div><span>KDV hariç</span><Money value={t.net} /></div><div><span>KDV</span><Money value={t.vat} /></div><div className="grand"><span>Toplam</span><Money value={t.total} /></div></div>}
      </Card>
      <TextArea label="Not" value={note} onChange={(e) => setNote(e.target.value)} />
      <FormError error={save.error} />
      <div className="form-actions"><Button loading={save.isPending} disabled={!supplierId || !valid} onClick={() => save.mutate(true)}>Kaydet ve onayla (stok artır)</Button><Button variant="secondary" disabled={!supplierId || !valid} onClick={() => save.mutate(false)}>Taslak kaydet</Button></div>
    </div>
  );
}
