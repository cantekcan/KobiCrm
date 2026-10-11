import { useState } from "react";
import { Link, useSearchParams } from "react-router-dom";
import { useQuery } from "@tanstack/react-query";
import { api } from "../lib/api";
import type { CollectionsReport, InvoiceStatusReport, Measures, OpenInvoices, ReceivablesReport, SalesReport, Statement, StockLevel, SupplierDebts } from "../lib/types";
import { L } from "../lib/types";
import { Badge, Balance, Button, Card, DataTable, EmptyState, ErrorBlock, Money, Notice, PageHeader, Select, Spinner, TextInput } from "../components/ui";
import { CustomerPicker } from "../components/pickers";
import { StatementTable } from "../components/statement";
import { addDays, dateTr, firstOfMonthIso, money, num, todayIso } from "../lib/format";

type Tab = "summary" | "sales" | "collections" | "receivables" | "overdue" | "statement" | "suppliers" | "openInvoices" | "invoiceStatus" | "stock";
const TABS: { id: Tab; label: string }[] = [
  { id: "summary", label: "Satış · Fatura · Tahsilat özeti" }, { id: "sales", label: "Satış raporları" }, { id: "collections", label: "Tahsilat raporu" },
  { id: "receivables", label: "Müşteri alacakları" }, { id: "overdue", label: "Vadesi geçmiş borçlar" }, { id: "statement", label: "Müşteri cari ekstresi" },
  { id: "suppliers", label: "Tedarikçi borçları" }, { id: "openInvoices", label: "Açık faturalar" }, { id: "invoiceStatus", label: "Fatura durumu" }, { id: "stock", label: "Stok durumu" },
];

function monday(iso: string) { const [y, m, d] = iso.split("-").map(Number); const dt = new Date(y, m - 1, d); const diff = (dt.getDay() + 6) % 7; return addDays(iso, -diff); }

export function ReportsPage() {
  const [sp, setSp] = useSearchParams();
  const tab = (sp.get("tab") as Tab) ?? "summary";
  const [from, setFrom] = useState(firstOfMonthIso());
  const [to, setTo] = useState(todayIso());
  const [customer, setCustomer] = useState<{ id: string; name: string } | null>(null);
  const [group, setGroup] = useState("day");
  const needsRange = ["summary", "sales", "collections", "invoiceStatus"].includes(tab);
  const needsCustomer = ["summary", "sales", "collections", "statement", "receivables", "openInvoices"].includes(tab);

  const preset = (k: "today" | "week" | "month" | "lastMonth") => {
    const t = todayIso();
    if (k === "today") { setFrom(t); setTo(t); }
    if (k === "week") { setFrom(monday(t)); setTo(t); }
    if (k === "month") { setFrom(firstOfMonthIso()); setTo(t); }
    if (k === "lastMonth") { const first = firstOfMonthIso(); const last = addDays(first, -1); setFrom(last.slice(0, 8) + "01"); setTo(last); }
  };

  return (
    <div className="stack">
      <PageHeader title="Raporlar" actions={<Button variant="secondary" onClick={() => window.print()}>Yazdır</Button>} />
      <div className="tabs no-print" role="tablist">{TABS.map((t) => <button key={t.id} role="tab" aria-selected={tab === t.id} className={tab === t.id ? "on" : ""} onClick={() => setSp({ tab: t.id }, { replace: true })}>{t.label}</button>)}</div>

      {(needsRange || needsCustomer) && (
        <Card className="no-print">
          <div className="filters" style={{ margin: 0 }}>
            {needsRange && <>
              <TextInput label="Başlangıç" type="date" value={from} onChange={(e) => setFrom(e.target.value)} />
              <TextInput label="Bitiş" type="date" value={to} onChange={(e) => setTo(e.target.value)} />
            </>}
            {needsCustomer && <div style={{ flex: "2 1 260px" }}><CustomerPicker value={customer} onChange={(c) => setCustomer(c ? { id: c.id, name: c.name } : null)} label={tab === "statement" ? "Müşteri" : "Müşteri (isteğe bağlı)"} onlyActive={false} /></div>}
          </div>
          {needsRange && <div className="chips" style={{ marginTop: ".6rem" }}>
            <button className="chip" onClick={() => preset("today")}>Bugün</button><button className="chip" onClick={() => preset("week")}>Bu hafta</button>
            <button className="chip" onClick={() => preset("month")}>Bu ay</button><button className="chip" onClick={() => preset("lastMonth")}>Geçen ay</button>
          </div>}
        </Card>
      )}
      {needsRange && from > to && <Notice tone="error">Bitiş tarihi başlangıçtan önce olamaz.</Notice>}

      {tab === "summary" && <Summary from={from} to={to} customerId={customer?.id} />}
      {tab === "sales" && <Sales from={from} to={to} customerId={customer?.id} group={group} setGroup={setGroup} />}
      {tab === "collections" && <Collections from={from} to={to} customerId={customer?.id} />}
      {tab === "receivables" && <Receivables overdue={false} customerId={customer?.id} />}
      {tab === "overdue" && <Receivables overdue customerId={undefined} />}
      {tab === "statement" && (customer ? <StatementReport customerId={customer.id} /> : <Card><EmptyState title="Müşteri seçin" text="Cari ekstre için yukarıdan bir müşteri seçin." /></Card>)}
      {tab === "suppliers" && <Suppliers />}
      {tab === "openInvoices" && <OpenInv customerId={customer?.id} />}
      {tab === "invoiceStatus" && <InvStatus from={from} to={to} />}
      {tab === "stock" && <StockReport />}
    </div>
  );
}

function Def({ text }: { text?: string }) { return text ? <Notice tone="info"><strong>Bu rapor neyi ölçer?</strong> {text}</Notice> : null; }
function Load({ q, children }: { q: { isLoading: boolean; error: unknown; refetch: () => void }; children: React.ReactNode }) { return q.isLoading ? <Spinner /> : q.error ? <ErrorBlock error={q.error} onRetry={q.refetch} /> : <>{children}</>; }

function Summary({ from, to, customerId }: { from: string; to: string; customerId?: string }) {
  const q = useQuery({ queryKey: ["rep-measures", from, to, customerId], enabled: from <= to, queryFn: () => api.get<Measures>("/reports/measures", { from, to, customerId }) });
  return (
    <Load q={q}>
      {q.data && <div className="stack">
        <Notice tone="warn">{q.data.explanation}</Notice>
        <div className="grid cols-3">
          {[q.data.commercialSales, q.data.officialInvoices, q.data.collections].map((m) => (
            <div className="stat" key={m.title}><div className="label">{m.title}</div><div className="value">{money(m.amount)}</div><div className="sub">{m.count} kayıt{m.net !== null && ` · KDV hariç ${money(m.net)}`}</div>
              <p className="small muted" style={{ marginTop: ".6rem" }}>{m.definition}</p>{m.note && <p className="small pos">{m.note}</p>}</div>
          ))}
        </div>
        <p className="muted small">{dateTr(q.data.from)} – {dateTr(q.data.to)} arası. Açılış bakiyeleri (devreden borçlar) hiçbir ölçüde yeni satış veya fatura olarak yer almaz.</p>
      </div>}
    </Load>
  );
}

function Sales({ from, to, customerId, group, setGroup }: { from: string; to: string; customerId?: string; group: string; setGroup: (g: string) => void }) {
  const q = useQuery({ queryKey: ["rep-sales", from, to, customerId, group], enabled: from <= to, queryFn: () => api.get<SalesReport>("/reports/sales", { from, to, customerId, groupBy: group }) });
  const groups: [string, string][] = [["day", "Günlük"], ["week", "Haftalık"], ["month", "Aylık"], ["customer", "Müşteri bazında"], ["product", "Ürün bazında"]];
  return (
    <div className="stack">
      <div className="chips no-print">{groups.map(([k, l]) => <button key={k} className={`chip ${group === k ? "on" : ""}`} onClick={() => setGroup(k)}>{l}</button>)}</div>
      <Load q={q}>{q.data && <>
        <Def text={q.data.definition} />
        <div className="grid cols-3 cols-2-mobile"><div className="stat"><div className="label">Toplam satış (KDV dahil)</div><div className="value">{money(q.data.grandTotal)}</div><div className="sub">{q.data.saleCount} satış</div></div>
          <div className="stat"><div className="label">KDV hariç</div><div className="value">{money(q.data.netTotal)}</div></div><div className="stat"><div className="label">KDV</div><div className="value">{money(q.data.vatTotal)}</div></div></div>
        <Card flush><DataTable rows={q.data.rows} rowKey={(r) => r.key} footer empty={<EmptyState title="Bu aralıkta satış yok" />}
          columns={[
            { header: group === "customer" ? "Müşteri" : group === "product" ? "Ürün" : "Dönem", title: true, cell: (r) => <strong>{r.label}</strong>, foot: "Toplam" },
            { header: "Satış sayısı", num: true, cell: (r) => r.saleCount },
            ...(group === "product" ? [{ header: "Miktar", num: true, cell: (r: SalesReport["rows"][number]) => `${num(r.quantity)} ${r.unit}` }] : []),
            { header: "KDV hariç", num: true, cell: (r) => <Money value={r.netTotal} />, foot: <Money value={q.data!.netTotal} /> },
            { header: "KDV", num: true, cell: (r) => <Money value={r.vatTotal} />, foot: <Money value={q.data!.vatTotal} /> },
            { header: "Toplam", num: true, cell: (r) => <strong><Money value={r.grandTotal} /></strong>, foot: <Money value={q.data!.grandTotal} /> },
          ]} /></Card></>}</Load>
    </div>
  );
}

function Collections({ from, to, customerId }: { from: string; to: string; customerId?: string }) {
  const q = useQuery({ queryKey: ["rep-coll", from, to, customerId], enabled: from <= to, queryFn: () => api.get<CollectionsReport>("/reports/collections", { from, to, customerId }) });
  return (
    <Load q={q}>{q.data && <div className="stack">
      <Def text={q.data.definition} />
      <div className="grid cols-4 cols-2-mobile"><div className="stat good"><div className="label">Toplam tahsilat</div><div className="value">{money(q.data.total)}</div><div className="sub">{q.data.rows.length} tahsilat</div></div>
        {Object.entries(q.data.byMethod).map(([k, v]) => <div className="stat" key={k}><div className="label">{L.method[k as keyof typeof L.method] ?? k}</div><div className="value">{money(v)}</div></div>)}</div>
      <Card flush><DataTable rows={q.data.rows} rowKey={(r) => r.paymentId} rowHref={(r) => `/tahsilatlar/${r.paymentId}`} empty={<EmptyState title="Bu aralıkta tahsilat yok" />}
        columns={[
          { header: "Tarih", cell: (r) => dateTr(r.date) }, { header: "Tahsilat", title: true, cell: (r) => <><strong>{r.customerName}</strong><div className="muted small">{r.number}</div></> },
          { header: "Yöntem", cell: (r) => L.method[r.method] }, { header: "Tutar", num: true, cell: (r) => <Money value={r.amount} /> }, { header: "Dağıtılmamış", num: true, cell: (r) => r.unallocated > 0 ? <Money value={r.unallocated} /> : "—" },
        ]} /></Card></div>}</Load>
  );
}

function Receivables({ overdue, customerId }: { overdue: boolean; customerId?: string }) {
  const q = useQuery({ queryKey: ["rep-rec", overdue, customerId], queryFn: () => api.get<ReceivablesReport>("/reports/receivables", { overdueOnly: overdue, customerId }) });
  return (
    <Load q={q}>{q.data && <div className="stack">
      <Def text={q.data.definition} />
      <div className="grid cols-3 cols-2-mobile"><div className="stat"><div className="label">Toplam müşteri alacağı</div><div className="value">{money(q.data.totalReceivable)}</div></div>
        <div className="stat danger"><div className="label">Vadesi geçmiş</div><div className="value">{money(q.data.totalOverdue)}</div></div>
        {q.data.customerAdvances > 0 && <div className="stat"><div className="label">Müşteri avansları (ayrı)</div><div className="value">{money(q.data.customerAdvances)}</div></div>}</div>
      <Card flush><DataTable rows={q.data.rows} rowKey={(r) => r.customerId} rowHref={(r) => `/musteriler/${r.customerId}`} footer empty={<EmptyState title={overdue ? "Vadesi geçmiş borç yok" : "Alacak yok"} />}
        columns={[
          { header: "Müşteri", title: true, cell: (r) => <><strong>{r.customerName}</strong><div className="muted small">{r.phone ?? ""}</div></>, foot: "Toplam" },
          { header: "Bakiye", num: true, cell: (r) => <Balance value={r.balance} />, foot: <Money value={q.data!.totalReceivable} /> },
          { header: "Vadesi geçmiş", num: true, cell: (r) => r.overdue > 0 ? <span className="pos mono">{money(r.overdue)}</span> : "—", foot: <Money value={q.data!.totalOverdue} /> },
          { header: "En eski vadesi geçen satış", cell: (r) => dateTr(r.oldestOverdueDate) },
          { header: "Son tahsilat", cell: (r) => dateTr(r.lastPaymentDate) },
        ]} /></Card></div>}</Load>
  );
}

function StatementReport({ customerId }: { customerId: string }) {
  const [from, setFrom] = useState("");
  const [to, setTo] = useState("");
  const q = useQuery({ queryKey: ["rep-stmt", customerId, from, to], queryFn: () => api.get<Statement>(`/reports/statement/${customerId}`, { from, to }) });
  return (
    <div className="stack">
      <div className="filters no-print"><TextInput label="Ekstre başlangıç" type="date" value={from} onChange={(e) => setFrom(e.target.value)} /><TextInput label="Ekstre bitiş" type="date" value={to} onChange={(e) => setTo(e.target.value)} /></div>
      <Load q={q}>{q.data && <Card flush title={<span style={{ padding: "0 1.2rem" }}>{q.data.accountName} — cari ekstre</span>}><StatementTable st={q.data} /></Card>}</Load>
    </div>
  );
}

function Suppliers() {
  const q = useQuery({ queryKey: ["rep-supp"], queryFn: () => api.get<SupplierDebts>("/reports/supplier-debts") });
  return (
    <Load q={q}>{q.data && <div className="stack"><Def text={q.data.definition} />
      <div className="stat warn" style={{ maxWidth: 320 }}><div className="label">Tedarikçilere toplam borcumuz</div><div className="value">{money(q.data.total)}</div></div>
      <Card flush><DataTable rows={q.data.rows} rowKey={(r) => r.supplierId} rowHref={(r) => `/tedarikciler/${r.supplierId}`} empty={<EmptyState title="Tedarikçi borcu yok" />}
        columns={[{ header: "Tedarikçi", title: true, cell: (r) => <strong>{r.supplierName}</strong> }, { header: "Telefon", cell: (r) => r.phone ?? "—" }, { header: "Borcumuz", num: true, cell: (r) => <Money value={r.balance} /> }]} /></Card></div>}</Load>
  );
}

function OpenInv({ customerId }: { customerId?: string }) {
  const q = useQuery({ queryKey: ["rep-openinv", customerId], queryFn: () => api.get<OpenInvoices>("/reports/open-invoices", { customerId }) });
  return (
    <Load q={q}>{q.data && <div className="stack"><Def text={q.data.definition} />
      <div className="stat" style={{ maxWidth: 320 }}><div className="label">Toplam açık fatura bakiyesi</div><div className="value">{money(q.data.totalRemaining)}</div></div>
      <Card flush><DataTable rows={q.data.rows} rowKey={(r) => r.id} rowHref={(r) => `/faturalar/${r.id}`} empty={<EmptyState title="Açık fatura yok" />}
        columns={[
          { header: "Fatura", title: true, cell: (r) => <><strong>{r.officialNumber ?? r.internalRef}</strong><div className="muted small">{dateTr(r.invoiceDate)}</div></> }, { header: "Müşteri", cell: (r) => r.customerName },
          { header: "Toplam", num: true, cell: (r) => <Money value={r.grandTotal} /> }, { header: "Tahsil", num: true, cell: (r) => <Money value={r.paidTotal} /> }, { header: "Kalan", num: true, cell: (r) => <strong><Money value={r.remaining} /></strong> },
          { header: "Resmî belge", cell: (r) => <Badge tone={r.officialStatus === "Issued" ? "good" : "neutral"}>{L.official[r.officialStatus]}</Badge> },
        ]} /></Card></div>}</Load>
  );
}

function InvStatus({ from, to }: { from: string; to: string }) {
  const q = useQuery({ queryKey: ["rep-invstatus", from, to], enabled: from <= to, queryFn: () => api.get<InvoiceStatusReport>("/reports/invoice-status", { from, to }) });
  return (
    <Load q={q}>{q.data && <div className="stack"><Def text={q.data.definition} />
      <Card flush><DataTable rows={q.data.rows} rowKey={(r) => r.internalStatus + r.officialStatus} footer empty={<EmptyState title="Bu aralıkta fatura yok" />}
        columns={[
          { header: "İç süreç durumu", title: true, cell: (r) => <Badge tone="info">{L.internal[r.internalStatus]}</Badge>, foot: "Toplam" }, { header: "Resmî belge durumu", cell: (r) => <Badge tone={r.officialStatus === "Issued" ? "good" : "neutral"}>{L.official[r.officialStatus]}</Badge> },
          { header: "Adet", num: true, cell: (r) => r.count, foot: q.data!.rows.reduce((a, b) => a + b.count, 0) }, { header: "Tutar", num: true, cell: (r) => <Money value={r.grandTotal} />, foot: <Money value={q.data!.rows.reduce((a, b) => a + b.grandTotal, 0)} /> },
        ]} /></Card></div>}</Load>
  );
}

function StockReport() {
  const q = useQuery({ queryKey: ["rep-stock"], queryFn: () => api.get<StockLevel[]>("/stock/levels") });
  return (
    <Load q={q}>{q.data && <Card flush><DataTable rows={q.data} rowKey={(r) => r.productId} footer empty={<EmptyState title="Ürün yok" />}
      columns={[
        { header: "Ürün", title: true, cell: (r) => <><strong>{r.name}</strong><div className="muted small">{r.code}</div></>, foot: "Toplam maliyet değeri" },
        { header: "Stok", num: true, cell: (r) => r.trackStock ? <span className={r.isLow ? "pos" : ""}>{num(r.quantity)} {r.unit}</span> : <span className="muted">Takip yok</span> },
        { header: "Minimum", num: true, cell: (r) => r.trackStock ? `${num(r.minStock)} ${r.unit}` : "—" },
        { header: "Durum", cell: (r) => !r.trackStock ? "—" : r.isLow ? <Badge tone="bad">Azalıyor</Badge> : <Badge tone="good">Yeterli</Badge> },
        { header: "Maliyet değeri", num: true, cell: (r) => r.trackStock ? <Money value={r.stockValueAtCost} /> : "—", foot: <Money value={q.data!.reduce((a, b) => a + b.stockValueAtCost, 0)} /> },
      ]} /></Card>}</Load>
  );
}
