import { useEffect, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { api } from "../lib/api";
import { useUrlFilters } from "../lib/hooks";
import type { Paged, SaleListItem } from "../lib/types";
import { L } from "../lib/types";
import { Badge, Card, DataTable, EmptyState, ErrorBlock, LinkButton, Money, PageHeader, Pager, Select, Spinner, TextInput, useDebounced } from "../components/ui";
import { dateTr } from "../lib/format";

export const saleTone = (s: SaleListItem["status"]) => (s === "Paid" ? "good" : s === "Cancelled" ? "bad" : s === "Draft" ? "neutral" : s === "PartiallyPaid" ? "warn" : "info") as "good" | "bad" | "neutral" | "warn" | "info";

export function SalesPage() {
  const [f, set] = useUrlFilters({ search: "", status: "", from: "", to: "", customerId: "", openBalance: "", invoiced: "", overdue: "", page: "1" });
  const [text, setText] = useState(f.search);
  const dq = useDebounced(text);
  useEffect(() => { if (dq !== f.search) set({ search: dq }); }, [dq]); // eslint-disable-line react-hooks/exhaustive-deps
  const { data, isLoading, error, refetch } = useQuery({
    queryKey: ["sales", f],
    queryFn: () => api.get<Paged<SaleListItem>>("/sales", {
      search: f.search, status: f.status, from: f.from, to: f.to, customerId: f.customerId, openBalance: f.openBalance ? true : undefined,
      invoiced: f.invoiced === "" ? undefined : f.invoiced === "yes", overdue: f.overdue ? true : undefined, page: f.page, pageSize: 25,
    }),
  });
  const filtered = f.status || f.from || f.to || f.customerId || f.openBalance || f.invoiced || f.overdue || f.search;
  return (
    <div className="stack">
      <PageHeader title="Satışlar" subtitle="Ticari satış kayıtları (resmî fatura değildir)" actions={<LinkButton to="/satislar/yeni">+ Yeni satış</LinkButton>} />
      <div className="filters">
        <TextInput label="Ara" placeholder="Satış no veya müşteri" value={text} onChange={(e) => setText(e.target.value)} />
        <Select label="Durum" value={f.status} onChange={(e) => set({ status: e.target.value })}>
          <option value="">Hepsi</option>{Object.entries(L.saleStatus).map(([k, v]) => <option key={k} value={k}>{v}</option>)}
        </Select>
        <TextInput label="Başlangıç" type="date" value={f.from} onChange={(e) => set({ from: e.target.value })} />
        <TextInput label="Bitiş" type="date" value={f.to} onChange={(e) => set({ to: e.target.value })} />
        <Select label="Fatura" value={f.invoiced} onChange={(e) => set({ invoiced: e.target.value })}>
          <option value="">Hepsi</option><option value="yes">Faturası hazırlanmış</option><option value="no">Faturası hazırlanmamış</option>
        </Select>
      </div>
      <div className="chips">
        <button className={`chip ${f.openBalance ? "on" : ""}`} onClick={() => set({ openBalance: f.openBalance ? "" : "1" })}>Tahsilatı bekleyenler</button>
        <button className={`chip ${f.overdue ? "on" : ""}`} onClick={() => set({ overdue: f.overdue ? "" : "1" })}>Vadesi geçmiş</button>
        {filtered && <button className="chip" onClick={() => { setText(""); set({ search: "", status: "", from: "", to: "", customerId: "", openBalance: "", invoiced: "", overdue: "" }); }}>Filtreleri temizle ✕</button>}
      </div>
      <Card flush>
        {isLoading ? <Spinner /> : error ? <ErrorBlock error={error} onRetry={refetch} /> : (
          <>
            <DataTable rows={data!.items} rowKey={(r) => r.id} rowHref={(r) => `/satislar/${r.id}`}
              empty={<EmptyState title={filtered ? "Filtreye uyan satış yok" : "Henüz satış yok"} text={filtered ? undefined : "İlk satışınızı oluşturun."} action={<LinkButton to="/satislar/yeni">+ Yeni satış</LinkButton>} />}
              columns={[
                { header: "Satış", title: true, cell: (r) => <><strong>{r.customerName}</strong><div className="muted small">{r.number} · {dateTr(r.saleDate)}</div></> },
                { header: "Ödeme", cell: (r) => <>{L.paymentType[r.paymentType]}{r.paymentType === "Credit" && <div className="muted small">Vade {dateTr(r.dueDate)}</div>}</> },
                { header: "Toplam (KDV dahil)", num: true, cell: (r) => <Money value={r.grandTotal} /> },
                { header: "Tahsil edilen", num: true, cell: (r) => r.status === "Draft" || r.status === "Cancelled" ? "—" : <Money value={r.paidTotal} /> },
                { header: "Kalan", num: true, cell: (r) => r.remaining > 0 ? <strong><Money value={r.remaining} /></strong> : "—" },
                { header: "Durum", cell: (r) => <Badge tone={saleTone(r.status)}>{L.saleStatus[r.status]}</Badge> },
                { header: "Fatura", cell: (r) => r.invoiceRef ? <span className="small">{r.invoiceOfficialNumber ?? r.invoiceRef}</span> : <span className="muted">—</span> },
              ]} />
            <Pager page={data!.page} pageSize={data!.pageSize} total={data!.total} onPage={(p) => set({ page: String(p) }, false)} />
          </>
        )}
      </Card>
    </div>
  );
}
