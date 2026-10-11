import { useEffect, useMemo, useRef, useState } from "react";
import { Link, useNavigate, useParams, useSearchParams } from "react-router-dom";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api, ApiError } from "../lib/api";
import { useUrlFilters } from "../lib/hooks";
import type { CustomerDetail, OpenSale, Paged, Payment } from "../lib/types";
import { L } from "../lib/types";
import { Badge, Balance, Button, Card, Check, DataTable, EmptyState, ErrorBlock, FormError, KV, LinkButton, Modal, Money, NumberInput, Notice, PageHeader, Pager, Select, Spinner, TextInput, TextArea, useConfirm, useDebounced, useToast } from "../components/ui";
import { CustomerPicker } from "../components/pickers";
import { dateTimeTr, dateTr, money, newKey, parseDecimal, round2, todayIso } from "../lib/format";

export function PaymentsPage() {
  const [f, set] = useUrlFilters({ search: "", from: "", to: "", method: "", status: "Active", unallocated: "", customerId: "", page: "1" });
  const [text, setText] = useState(f.search);
  const dq = useDebounced(text);
  useEffect(() => { if (dq !== f.search) set({ search: dq }); }, [dq]); // eslint-disable-line
  const { data, isLoading, error, refetch } = useQuery({
    queryKey: ["payments", f],
    queryFn: () => api.get<Paged<Payment>>("/payments", { search: f.search, from: f.from, to: f.to, method: f.method, status: f.status, unallocated: f.unallocated ? true : undefined, customerId: f.customerId, page: f.page, pageSize: 25 }),
  });
  return (
    <div className="stack">
      <PageHeader title="Tahsilatlar" actions={<LinkButton to="/tahsilatlar/yeni">+ Tahsilat gir</LinkButton>} />
      <div className="filters">
        <TextInput label="Ara" placeholder="Tahsilat no, müşteri, dekont no" value={text} onChange={(e) => setText(e.target.value)} />
        <TextInput label="Başlangıç" type="date" value={f.from} onChange={(e) => set({ from: e.target.value })} />
        <TextInput label="Bitiş" type="date" value={f.to} onChange={(e) => set({ to: e.target.value })} />
        <Select label="Yöntem" value={f.method} onChange={(e) => set({ method: e.target.value })}><option value="">Hepsi</option>{Object.entries(L.method).map(([k, v]) => <option key={k} value={k}>{v}</option>)}</Select>
        <Select label="Durum" value={f.status} onChange={(e) => set({ status: e.target.value })}><option value="Active">Geçerli</option><option value="Reversed">Geri alınan</option><option value="">Hepsi</option></Select>
      </div>
      <div className="chips"><button className={`chip ${f.unallocated ? "on" : ""}`} onClick={() => set({ unallocated: f.unallocated ? "" : "1" })}>Dağıtılmamış tutarı olanlar</button></div>
      <Card flush>
        {isLoading ? <Spinner /> : error ? <ErrorBlock error={error} onRetry={refetch} /> : (
          <>
            <DataTable rows={data!.items} rowKey={(r) => r.id} rowHref={(r) => `/tahsilatlar/${r.id}`} empty={<EmptyState title="Tahsilat bulunamadı" action={<LinkButton to="/tahsilatlar/yeni">+ Tahsilat gir</LinkButton>} />}
              columns={[
                { header: "Tahsilat", title: true, cell: (r) => <><strong>{r.customerName}</strong><div className="muted small">{r.number} · {dateTr(r.paymentDate)}</div></> },
                { header: "Yöntem", cell: (r) => <>{L.method[r.method]}{r.bankReference && <div className="muted small">{r.bankReference}</div>}</> },
                { header: "Tutar", num: true, cell: (r) => <strong><Money value={r.amount} /></strong> },
                { header: "Faturalara/satışlara dağıtılan", num: true, cell: (r) => r.status === "Reversed" ? "—" : <Money value={r.allocatedAmount} /> },
                { header: "Dağıtılmamış", num: true, cell: (r) => r.status === "Reversed" ? "—" : r.unallocatedAmount > 0 ? <Money value={r.unallocatedAmount} /> : "—" },
                { header: "Durum", cell: (r) => r.status === "Reversed" ? <Badge tone="bad">Geri alındı</Badge> : <Badge tone="good">Geçerli</Badge> },
              ]} />
            <Pager page={data!.page} pageSize={data!.pageSize} total={data!.total} onPage={(p) => set({ page: String(p) }, false)} />
          </>
        )}
      </Card>
    </div>
  );
}

export function PaymentFormPage() {
  const [sp] = useSearchParams();
  const nav = useNavigate();
  const qc = useQueryClient();
  const toast = useToast();
  const keyRef = useRef(newKey());
  const [customer, setCustomer] = useState<{ id: string; name: string } | null>(null);
  const [date, setDate] = useState(todayIso());
  const [amount, setAmount] = useState("");
  const [method, setMethod] = useState("BankTransfer");
  const [ref, setRef] = useState("");
  const [note, setNote] = useState("");
  const [mode, setMode] = useState<"auto" | "manual" | "none">("auto");
  const [alloc, setAlloc] = useState<Record<string, string>>({});
  const [file, setFile] = useState<File | null>(null);

  const pre = sp.get("musteri");
  const preSale = sp.get("satis");
  const { data: preC } = useQuery({ queryKey: ["customer", pre], queryFn: () => api.get<CustomerDetail>(`/customers/${pre}`), enabled: !!pre });
  useEffect(() => { if (preC && !customer) setCustomer({ id: preC.id, name: preC.name }); }, [preC]); // eslint-disable-line
  const { data: detail } = useQuery({ queryKey: ["customer", customer?.id], queryFn: () => api.get<CustomerDetail>(`/customers/${customer!.id}`), enabled: !!customer });
  const { data: open } = useQuery({ queryKey: ["open-sales", customer?.id], queryFn: () => api.get<OpenSale[]>(`/customers/${customer!.id}/open-sales`), enabled: !!customer });

  useEffect(() => { if (preSale && open && amount === "") { const s = open.find((o) => o.saleId === preSale); if (s) { setAmount(String(s.remaining).replace(".", ",")); setMode("manual"); setAlloc({ [s.saleId]: String(s.remaining).replace(".", ",") }); } } }, [open, preSale]); // eslint-disable-line

  const amt = parseDecimal(amount);
  const allocSum = useMemo(() => round2(Object.values(alloc).reduce((a, v) => a + (parseDecimal(v) ?? 0), 0)), [alloc]);
  const remainingAfter = detail && amt ? detail.balance - amt : null;

  const save = useMutation({
    mutationFn: async () => {
      const body = {
        customerId: customer!.id, paymentDate: date, amount: amt, method, bankReference: ref || null, note: note || null, clientRequestId: keyRef.current,
        autoAllocate: mode === "auto",
        allocations: mode === "manual" ? Object.entries(alloc).map(([saleId, v]) => ({ saleId, amount: parseDecimal(v) ?? 0 })).filter((a) => a.amount > 0) : null,
      };
      const p = await api.post<Payment>("/payments", body);
      if (file) { try { await api.upload(`/payments/${p.id}/receipt`, file); } catch (e) { toast.error("Tahsilat kaydedildi ancak dekont yüklenemedi: " + (e as Error).message); } }
      return p;
    },
    onSuccess: (p) => { qc.invalidateQueries(); toast.success(`${money(p.amount)} tahsilat kaydedildi.${p.unallocatedAmount > 0 ? ` ${money(p.unallocatedAmount)} genel bakiyeden mahsup edildi.` : ""}`); nav(`/tahsilatlar/${p.id}`); },
  });
  const err = save.error instanceof ApiError ? save.error : null;
  const manualOver = mode === "manual" && amt !== null && allocSum > amt + 0.0001;
  const canSave = !!customer && !!amt && amt > 0 && !manualOver;

  return (
    <div className="stack">
      <PageHeader title="Tahsilat gir" back={{ to: "/tahsilatlar", label: "Tahsilatlar" }} />
      <Card title="Müşteri ve tutar">
        <div className="form-grid">
          <div className="span-2"><CustomerPicker value={customer} required onChange={(c) => { setCustomer(c ? { id: c.id, name: c.name } : null); setAlloc({}); }} autoFocus={!pre} /></div>
          {detail && <div className="span-2 notice info">Müşterinin güncel bakiyesi: <Balance value={detail.balance} />{detail.overdueAmount > 0 && <> · vadesi geçmiş: <span className="pos">{money(detail.overdueAmount)}</span></>}</div>}
          <NumberInput label="Tahsil edilen tutar (TL)" required value={amount} onChange={setAmount} error={err?.field("amount")} />
          <TextInput label="Tahsilat tarihi" type="date" value={date} onChange={(e) => setDate(e.target.value)} error={err?.field("paymentDate")} />
          <Select label="Ödeme yöntemi" value={method} onChange={(e) => setMethod(e.target.value)}>{Object.entries(L.method).map(([k, v]) => <option key={k} value={k}>{v}</option>)}</Select>
          <TextInput label="Banka referansı / dekont no (isteğe bağlı)" value={ref} onChange={(e) => setRef(e.target.value)} />
          <TextArea className="span-2" label="Açıklama (isteğe bağlı)" value={note} onChange={(e) => setNote(e.target.value)} />
          <div className="span-2 stack-sm"><label className="field"><span>Dekont dosyası (isteğe bağlı: PDF, JPG, PNG — en fazla 5 MB)</span><input type="file" accept=".pdf,.jpg,.jpeg,.png" onChange={(e) => setFile(e.target.files?.[0] ?? null)} /></label></div>
        </div>
      </Card>

      {customer && (
        <Card title="Hangi satışlara/faturalara sayılsın?">
          <div className="segmented" role="group" style={{ marginBottom: ".8rem" }}>
            <button type="button" className={mode === "auto" ? "on" : ""} onClick={() => setMode("auto")}>Otomatik (en eski vadeden)</button>
            <button type="button" className={mode === "manual" ? "on" : ""} onClick={() => setMode("manual")}>Ben seçeyim</button>
            <button type="button" className={mode === "none" ? "on" : ""} onClick={() => setMode("none")}>Genel bakiyeden mahsup</button>
          </div>
          {mode === "none" && <Notice tone="info">Tutar müşterinin cari bakiyesinden düşer, ancak belirli bir satışa bağlanmaz. Daha sonra tahsilat ekranından satışlara dağıtabilirsiniz.</Notice>}
          {mode === "auto" && <Notice tone="info">Tutar, vadesi en yakın/eski açık satışlardan başlayarak dağıtılır; artan kısım genel bakiyeden mahsup edilir (örneğin açılış borcuna).</Notice>}
          {mode === "manual" && (
            <>
              <DataTable rows={open ?? []} rowKey={(o) => o.saleId} empty={<EmptyState title="Açık satış yok" text="Bu müşterinin açık satış borcu yok; tutar genel bakiyeden düşer." />}
                columns={[
                  { header: "Satış / fatura", title: true, cell: (o) => <><strong>{o.number}</strong>{o.invoiceRef && <span className="muted small"> · {o.officialNumber ?? o.invoiceRef}</span>}<div className="muted small">Vade {dateTr(o.dueDate)} {o.isOverdue && <Badge tone="bad">Vadesi geçmiş</Badge>}</div></> },
                  { header: "Kalan", num: true, cell: (o) => <Money value={o.remaining} /> },
                  { header: "Bu tahsilattan", num: true, cell: (o) => (
                    <div className="row" style={{ justifyContent: "flex-end", flexWrap: "nowrap" }}>
                      <input style={{ width: 120, minHeight: 40 }} inputMode="decimal" aria-label={`${o.number} için tutar`} value={alloc[o.saleId] ?? ""} onChange={(e) => setAlloc({ ...alloc, [o.saleId]: e.target.value })} />
                      <Button size="sm" variant="secondary" onClick={() => setAlloc({ ...alloc, [o.saleId]: String(Math.min(o.remaining, Math.max(0, round2((amt ?? 0) - round2(allocSum - (parseDecimal(alloc[o.saleId]) ?? 0)))))).replace(".", ",") })}>Doldur</Button>
                    </div>) },
                ]} />
              <p className={manualOver ? "pos" : "muted"} style={{ marginTop: ".6rem" }}>Dağıtılan: <strong>{money(allocSum)}</strong> / {amt ? money(amt) : "—"}{amt ? ` · genel bakiyeden mahsup: ${money(Math.max(0, round2(amt - allocSum)))}` : ""}{manualOver ? " — dağıtım tahsilat tutarını aşıyor!" : ""}</p>
            </>
          )}
        </Card>
      )}

      {remainingAfter !== null && <Notice tone="good">Kayıttan sonra müşterinin bakiyesi: <strong>{money(Math.abs(remainingAfter))}{remainingAfter < 0 ? " (müşteri alacaklı)" : ""}</strong></Notice>}
      <FormError error={save.error} />
      {err?.status === 0 && <Notice tone="warn">Bağlantı kesildi. Tekrar “Kaydet”e basmak güvenlidir: aynı tahsilat iki kez oluşmaz.</Notice>}
      <div className="form-actions"><Button loading={save.isPending} disabled={!canSave} onClick={() => save.mutate()}>Tahsilatı kaydet</Button><Button variant="secondary" onClick={() => nav(-1)}>Vazgeç</Button></div>
    </div>
  );
}

export function PaymentDetailPage() {
  const { id } = useParams();
  const qc = useQueryClient();
  const toast = useToast();
  const confirm = useConfirm();
  const { data: p, isLoading, error, refetch } = useQuery({ queryKey: ["payment", id], queryFn: () => api.get<Payment>(`/payments/${id}`) });
  const { data: open } = useQuery({ queryKey: ["open-sales", p?.customerId], enabled: !!p && p.status === "Active" && p.unallocatedAmount > 0, queryFn: () => api.get<OpenSale[]>(`/customers/${p!.customerId}/open-sales`) });
  const [modal, setModal] = useState(false);
  const [upload, setUpload] = useState<File | null>(null);
  const reverse = useMutation({
    mutationFn: (reason: string) => api.post(`/payments/${id}/reverse`, { reason }),
    onSuccess: () => { qc.invalidateQueries(); toast.success("Tahsilat geri alındı. Müşteri borcu geri yüklendi; kayıt ve ters kayıt saklanır."); },
  });
  const receipt = useMutation({ mutationFn: () => api.upload(`/payments/${id}/receipt`, upload!), onSuccess: () => { qc.invalidateQueries(); setUpload(null); toast.success("Dekont yüklendi."); } });

  if (isLoading) return <Spinner />;
  if (error || !p) return <ErrorBlock error={error} onRetry={refetch} />;
  return (
    <div className="stack">
      <PageHeader title={<>{p.number} {p.status === "Reversed" ? <Badge tone="bad">Geri alındı</Badge> : <Badge tone="good">Geçerli</Badge>}</>} back={{ to: "/tahsilatlar", label: "Tahsilatlar" }}
        subtitle={<><Link to={`/musteriler/${p.customerId}`}>{p.customerName}</Link> · {dateTr(p.paymentDate)} · {L.method[p.method]}</>}
        actions={p.status === "Active" ? <>
          {p.unallocatedAmount > 0 && <Button variant="secondary" onClick={() => setModal(true)}>Kalan tutarı satışlara dağıt</Button>}
          <Button variant="outline-danger" onClick={async () => {
            const r = await confirm({ title: "Tahsilatı geri al", danger: true, confirmLabel: "Geri al", reasonLabel: "Geri alma nedeni (zorunlu)",
              message: <div className="stack-sm"><p><strong>{p.number}</strong> ({money(p.amount)}) geri alınacak:</p><ul><li>Müşterinin borcu <strong>{money(p.amount)}</strong> artar (cari hesaba ters kayıt).</li><li>Bu tahsilatın satışlara dağıtımı kalkar; ilgili satışlar tekrar açık borç olur.</li><li>Tahsilat kaydı silinmez; “Geri alındı” olarak saklanır.</li></ul></div> });
            if (r.ok) reverse.mutate(r.reason);
          }}>Tahsilatı geri al</Button></> : undefined} />
      {p.status === "Reversed" && <Notice tone="error" title="Bu tahsilat geri alındı">{dateTimeTr(p.reversedAtUtc)} · {p.reverseReason}</Notice>}
      <div className="grid cols-3 cols-2-mobile">
        <div className="stat"><div className="label">Tutar</div><div className="value">{money(p.amount)}</div></div>
        <div className="stat"><div className="label">Satışlara dağıtılan</div><div className="value">{p.status === "Active" ? money(p.allocatedAmount) : "—"}</div></div>
        <div className={`stat ${p.unallocatedAmount > 0 ? "warn" : ""}`}><div className="label">Dağıtılmamış (genel bakiyeden mahsup)</div><div className="value">{p.status === "Active" ? money(p.unallocatedAmount) : "—"}</div></div>
      </div>
      <Card title="Ayrıntılar"><KV items={[["Banka referansı / dekont no", p.bankReference], ["Açıklama", p.note], ["Kayıt zamanı", dateTimeTr(p.createdAtUtc)],
        ["Dekont", p.hasReceipt ? <a href="#" onClick={(e) => { e.preventDefault(); api.download(`/payments/${p.id}/receipt`, `${p.number}-dekont`).catch((er) => toast.error(er.message)); }}>Dekontu indir</a> : "Yok"]]} />
        {p.status === "Active" && !p.hasReceipt && <div className="stack-sm" style={{ marginTop: ".8rem" }}><input type="file" accept=".pdf,.jpg,.jpeg,.png" onChange={(e) => setUpload(e.target.files?.[0] ?? null)} /><div><Button size="sm" disabled={!upload} loading={receipt.isPending} onClick={() => receipt.mutate()}>Dekont yükle</Button></div><FormError error={receipt.error} /></div>}
      </Card>
      <Card title="Dağıtımlar" flush>
        <DataTable rows={p.allocations} rowKey={(a) => a.id} empty={<EmptyState title="Dağıtım yok" text="Bu tahsilat tamamen genel bakiyeden mahsup edildi." />}
          columns={[
            { header: "Satış", title: true, cell: (a) => <Link to={`/satislar/${a.saleId}`}>{a.saleNumber}</Link> },
            { header: "Fatura", cell: (a) => a.invoiceId ? <Link to={`/faturalar/${a.invoiceId}`}>{a.officialNumber ?? a.invoiceRef}</Link> : "—" },
            { header: "Tutar", num: true, cell: (a) => <Money value={a.amount} /> },
            { header: "Durum", cell: (a) => a.isActive ? <Badge tone="good">Geçerli</Badge> : <Badge tone="bad">Kalktı</Badge> },
          ]} />
      </Card>
      <FormError error={reverse.error} />
      {modal && <AllocateModal p={p} open={open ?? []} onClose={() => setModal(false)} />}
    </div>
  );
}

function AllocateModal({ p, open, onClose }: { p: Payment; open: OpenSale[]; onClose: () => void }) {
  const qc = useQueryClient();
  const toast = useToast();
  const [alloc, setAlloc] = useState<Record<string, string>>({});
  const sum = round2(Object.values(alloc).reduce((a, v) => a + (parseDecimal(v) ?? 0), 0));
  const m = useMutation({
    mutationFn: () => api.post(`/payments/${p.id}/allocate`, Object.entries(alloc).map(([saleId, v]) => ({ saleId, amount: parseDecimal(v) ?? 0 })).filter((a) => a.amount > 0)),
    onSuccess: () => { qc.invalidateQueries(); toast.success("Dağıtım kaydedildi."); onClose(); },
  });
  return (
    <Modal title="Kalan tutarı dağıt" onClose={onClose} wide footer={<><Button variant="secondary" onClick={onClose}>Vazgeç</Button><Button loading={m.isPending} disabled={sum <= 0 || sum > p.unallocatedAmount + 0.0001} onClick={() => m.mutate()}>Dağıt</Button></>}>
      <div className="stack">
        <Notice tone="info">Dağıtılabilir tutar: <strong>{money(p.unallocatedAmount)}</strong>. Dağıtım müşteri bakiyesini değiştirmez; yalnızca hangi satışın kapandığını gösterir.</Notice>
        <DataTable rows={open} rowKey={(o) => o.saleId} empty={<EmptyState title="Açık satış yok" />} columns={[
          { header: "Satış", title: true, cell: (o) => <strong>{o.number}</strong> }, { header: "Vade", cell: (o) => dateTr(o.dueDate) }, { header: "Kalan", num: true, cell: (o) => <Money value={o.remaining} /> },
          { header: "Dağıt", num: true, cell: (o) => <input style={{ width: 120, minHeight: 40 }} inputMode="decimal" value={alloc[o.saleId] ?? ""} onChange={(e) => setAlloc({ ...alloc, [o.saleId]: e.target.value })} /> },
        ]} />
        <p className={sum > p.unallocatedAmount ? "pos" : "muted"}>Toplam: {money(sum)}</p>
        <FormError error={m.error} />
      </div>
    </Modal>
  );
}
