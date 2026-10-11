import { useEffect, useState } from "react";
import { Link, useNavigate, useSearchParams } from "react-router-dom";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "../lib/api";
import { useUrlFilters } from "../lib/hooks";
import type { InvoiceListItem, OfficialStatus, Paged, SaleListItem } from "../lib/types";
import { L } from "../lib/types";
import { Badge, Button, Card, DataTable, EmptyState, ErrorBlock, FormError, Modal, Money, Notice, PageHeader, Pager, Select, Spinner, TextInput, useDebounced, useToast } from "../components/ui";
import { CustomerPicker } from "../components/pickers";
import { dateTr } from "../lib/format";

export const internalTone = (s: InvoiceListItem["internalStatus"]) => (s === "Completed" ? "good" : s === "Cancelled" ? "bad" : s === "Error" || s === "CancellationRequired" ? "warn" : s === "Draft" ? "neutral" : "info") as "good" | "bad" | "warn" | "neutral" | "info";
export const officialTone = (s: OfficialStatus) => (s === "Issued" ? "good" : s === "Cancelled" || s === "Failed" ? "bad" : s === "ResultUnknown" ? "warn" : s === "Submitting" ? "info" : "neutral") as "good" | "bad" | "warn" | "neutral" | "info";

/** Resmî belgenin kaynağı ve doğrulanma durumu — "GİB'den doğrulanmış" izlenimi verilmez. */
export function SourceBadge({ source, verified, provider }: { source: string; verified: boolean; provider?: string | null }) {
  if (source === "ManualEntry") return <Badge tone="warn">Kullanıcı girişi · GİB'den doğrulanmadı</Badge>;
  if (source === "Provider") return <Badge tone={verified ? "info" : "warn"}>Sağlayıcı: {provider ?? "?"}</Badge>;
  return null;
}

export function InvoicesPage() {
  const nav = useNavigate();
  const [sp, setSp] = useSearchParams();
  const [f, set] = useUrlFilters({ search: "", internalStatus: "", officialStatus: "", from: "", to: "", customerId: "", open: "", awaitingOfficial: "", page: "1" });
  const [text, setText] = useState(f.search);
  const dq = useDebounced(text);
  const [prepare, setPrepare] = useState(sp.get("hazirla") === "1");
  useEffect(() => { if (dq !== f.search) set({ search: dq }); }, [dq]); // eslint-disable-line react-hooks/exhaustive-deps
  const { data, isLoading, error, refetch } = useQuery({
    queryKey: ["invoices", f],
    queryFn: () => api.get<Paged<InvoiceListItem>>("/invoices", {
      search: f.search, internalStatus: f.internalStatus, officialStatus: f.officialStatus, from: f.from, to: f.to, customerId: f.customerId,
      open: f.open ? true : undefined, awaitingOfficial: f.awaitingOfficial ? true : undefined, page: f.page, pageSize: 25,
    }),
  });
  const filtered = Object.entries(f).some(([k, v]) => k !== "page" && v);
  return (
    <div className="stack">
      <PageHeader title="Faturalar" subtitle="İç süreç durumu ile resmî belge durumu ayrı izlenir" actions={<Button onClick={() => setPrepare(true)}>+ Fatura taslağı hazırla</Button>} />
      <Notice tone="info">Bu ekrandaki <strong>taslak</strong> ve <strong>iç onay</strong> resmî fatura değildir. Resmî fatura yalnızca LUCA / GİB portalı (veya doğrulanmış bir sağlayıcı) üzerinde düzenlenir; numarası alındığında buraya kaydedilir.</Notice>
      <div className="filters">
        <TextInput label="Ara" placeholder="İç ref., resmî no veya müşteri" value={text} onChange={(e) => setText(e.target.value)} />
        <Select label="İç süreç durumu" value={f.internalStatus} onChange={(e) => set({ internalStatus: e.target.value })}><option value="">Hepsi</option>{Object.entries(L.internal).map(([k, v]) => <option key={k} value={k}>{v}</option>)}</Select>
        <Select label="Resmî belge durumu" value={f.officialStatus} onChange={(e) => set({ officialStatus: e.target.value })}><option value="">Hepsi</option>{Object.entries(L.official).map(([k, v]) => <option key={k} value={k}>{v}</option>)}</Select>
        <TextInput label="Başlangıç" type="date" value={f.from} onChange={(e) => set({ from: e.target.value })} />
        <TextInput label="Bitiş" type="date" value={f.to} onChange={(e) => set({ to: e.target.value })} />
      </div>
      <div className="chips">
        <button className={`chip ${f.awaitingOfficial ? "on" : ""}`} onClick={() => set({ awaitingOfficial: f.awaitingOfficial ? "" : "1" })}>Resmî işlem bekleyenler</button>
        <button className={`chip ${f.open ? "on" : ""}`} onClick={() => set({ open: f.open ? "" : "1" })}>Açık (tahsilatı bekleyen)</button>
        <button className={`chip ${f.internalStatus === "Draft" ? "on" : ""}`} onClick={() => set({ internalStatus: f.internalStatus === "Draft" ? "" : "Draft" })}>Taslaklar</button>
        {filtered && <button className="chip" onClick={() => { setText(""); setSp({}, { replace: true }); }}>Filtreleri temizle ✕</button>}
      </div>
      <Card flush>
        {isLoading ? <Spinner /> : error ? <ErrorBlock error={error} onRetry={refetch} /> : (
          <>
            <DataTable rows={data!.items} rowKey={(r) => r.id} rowHref={(r) => `/faturalar/${r.id}`}
              empty={<EmptyState title={filtered ? "Filtreye uyan fatura yok" : "Henüz fatura yok"} text={filtered ? undefined : "Onaylı bir satıştan fatura taslağı hazırlayın."} action={<Button onClick={() => setPrepare(true)}>+ Fatura taslağı hazırla</Button>} />}
              columns={[
                { header: "Fatura", title: true, cell: (r) => <><strong>{r.officialNumber ?? "Resmî no yok"}</strong><div className="muted small">{r.internalRef} · {dateTr(r.invoiceDate)}</div></> },
                { header: "Müşteri", cell: (r) => r.customerName },
                { header: "Belge türü", wide: true, cell: (r) => L.docType[r.documentType] },
                { header: "KDV hariç", num: true, wide: true, cell: (r) => <Money value={r.netTotal} /> },
                { header: "KDV", num: true, wide: true, cell: (r) => <Money value={r.vatTotal} /> },
                { header: "Genel toplam", num: true, cell: (r) => <strong><Money value={r.grandTotal} /></strong> },
                { header: "Tahsil", num: true, cell: (r) => <Money value={r.paidTotal} /> },
                { header: "Kalan", num: true, cell: (r) => <Money value={r.remaining} /> },
                { header: "Vade", cell: (r) => dateTr(r.dueDate) },
                { header: "İç süreç", cell: (r) => <Badge tone={internalTone(r.internalStatus)}>{L.internal[r.internalStatus]}</Badge> },
                { header: "Resmî belge", cell: (r) => <div className="stack-sm"><Badge tone={officialTone(r.officialStatus)}>{L.official[r.officialStatus]}</Badge><SourceBadge source={r.officialSource} verified={r.officialVerified} provider={r.officialProvider} /></div> },
                { header: "Ekler", wide: true, cell: (r) => r.attachments.length === 0 ? "—" : <span className="small">{r.attachments.map((a) => a.kind.toUpperCase()).join(", ")}</span> },
              ]} />
            <Pager page={data!.page} pageSize={data!.pageSize} total={data!.total} onPage={(p) => set({ page: String(p) }, false)} />
          </>
        )}
      </Card>
      {prepare && <PrepareModal initialCustomerId={sp.get("customerId")} onClose={() => setPrepare(false)} onCreated={(id) => nav(`/faturalar/${id}`)} />}
    </div>
  );
}

function PrepareModal({ initialCustomerId, onClose, onCreated }: { initialCustomerId: string | null; onClose: () => void; onCreated: (id: string) => void }) {
  const qc = useQueryClient();
  const toast = useToast();
  const [cust, setCust] = useState<{ id: string; name: string } | null>(null);
  useQuery({ queryKey: ["customer-min", initialCustomerId], enabled: !!initialCustomerId && !cust, queryFn: async () => { const c = await api.get<{ id: string; name: string }>(`/customers/${initialCustomerId}`); setCust({ id: c.id, name: c.name }); return c; } });
  const { data, isFetching } = useQuery({ queryKey: ["uninvoiced", cust?.id], enabled: !!cust, queryFn: () => api.get<Paged<SaleListItem>>("/sales", { customerId: cust!.id, invoiced: false, pageSize: 50 }) });
  const make = useMutation({
    mutationFn: (saleId: string) => api.post<{ id: string }>("/invoices/draft", { saleId }),
    onSuccess: (inv) => { qc.invalidateQueries(); toast.success("Fatura TASLAĞI hazırlandı. Bu henüz resmî fatura değildir."); onCreated(inv.id); },
  });
  return (
    <Modal title="Fatura taslağı hazırla" onClose={onClose} wide>
      <div className="stack">
        <Notice tone="info">Fatura, onaylanmış bir satıştan hazırlanır; tutarlar satıştan gelir. Taslak hazırlamak resmî fatura kesmek değildir ve cari hesabı etkilemez.</Notice>
        <CustomerPicker value={cust} onChange={(c) => setCust(c ? { id: c.id, name: c.name } : null)} />
        {cust && (isFetching ? <Spinner /> : (
          <DataTable rows={data?.items.filter((s) => s.status !== "Draft" && s.status !== "Cancelled") ?? []} rowKey={(s) => s.id}
            empty={<EmptyState title="Faturası hazırlanmamış onaylı satış yok" text="Önce satışı oluşturup onaylayın." />}
            columns={[
              { header: "Satış", title: true, cell: (s) => <><strong>{s.number}</strong><div className="muted small">{dateTr(s.saleDate)}</div></> },
              { header: "Toplam", num: true, cell: (s) => <Money value={s.grandTotal} /> },
              { header: "", cell: (s) => <Button size="sm" loading={make.isPending} onClick={() => make.mutate(s.id)}>Taslak hazırla</Button> },
            ]} />
        ))}
        <FormError error={make.error} />
      </div>
    </Modal>
  );
}
