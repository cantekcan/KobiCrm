import { Link } from "react-router-dom";
import { useQuery } from "@tanstack/react-query";
import { api } from "../lib/api";
import type { Dashboard as D } from "../lib/types";
import { L } from "../lib/types";
import { Balance, Badge, Card, DataTable, EmptyState, ErrorBlock, LinkButton, Money, PageHeader, Spinner } from "../components/ui";
import { dateTr, money, num } from "../lib/format";

function Stat({ label, value, sub, to, tone, title }: { label: string; value: string; sub?: string; to: string; tone?: "warn" | "danger" | "good"; title?: string }) {
  return (
    <Link to={to} className={`stat ${tone ?? ""}`} title={title}>
      <div className="label">{label}</div>
      <div className="value">{value}</div>
      {sub && <div className="sub">{sub}</div>}
    </Link>
  );
}

export function Dashboard() {
  const { data: d, isLoading, error, refetch } = useQuery({ queryKey: ["dashboard"], queryFn: () => api.get<D>("/dashboard") });
  if (isLoading) return <Spinner />;
  if (error || !d) return <ErrorBlock error={error} onRetry={refetch} />;

  const def = d.definitions;
  const m = `from=${d.monthFrom}&to=${d.monthTo}`;
  const first = d.recentSales.length === 0 && d.totalReceivable.amount === 0 && d.recentPayments.length === 0;

  return (
    <div className="stack">
      <PageHeader title="Genel Bakış" subtitle={`${dateTr(d.today)} · tutarlar KDV dahildir`}
        actions={<><LinkButton to="/satislar/yeni">+ Yeni satış</LinkButton><LinkButton to="/tahsilatlar/yeni" variant="secondary">+ Tahsilat gir</LinkButton></>} />

      {first && (
        <Card><EmptyState title="Hoş geldiniz" text="Başlamak için önce müşterilerinizi ve ürünlerinizi ekleyin. Geçmişten kalan müşteri borçlarını, müşteriyi kaydederken 'Açılış bakiyesi' alanına girebilirsiniz."
          action={<div className="row" style={{ justifyContent: "center" }}><LinkButton to="/musteriler/yeni">Müşteri ekle</LinkButton><LinkButton to="/urunler/yeni" variant="secondary">Ürün ekle</LinkButton></div>} /></Card>
      )}

      <div className="grid cols-4 cols-2-mobile">
        <Stat label="Bugünkü satış" value={money(d.todaySales.amount)} sub={`${d.todaySales.count} satış`} to={`/satislar?from=${d.today}&to=${d.today}`} title={def.sales} />
        <Stat label="Bu ayki satış" value={money(d.monthSales.amount)} sub={`${d.monthSales.count} satış`} to={`/satislar?${m}`} title={def.sales} />
        <Stat label="Toplam müşteri alacağı" value={money(d.totalReceivable.amount)} sub={`${d.totalReceivable.count} borçlu müşteri`} to="/musteriler?balance=debt&sort=balance&desc=1" title={def.receivable} />
        <Stat label="Vadesi geçmiş borçlar" value={money(d.overdueReceivable.amount)} sub={`${d.overdueReceivable.count} müşteri`} to="/musteriler?balance=overdue&sort=overdue&desc=1" tone={d.overdueReceivable.amount > 0 ? "danger" : undefined} title={def.overdue} />
        <Stat label="Bu ay tahsil edilen" value={money(d.monthCollected.amount)} sub={`${d.monthCollected.count} tahsilat`} to={`/tahsilatlar?${m}`} tone="good" title={def.collected} />
        <Stat label="Bekleyen tahsilatlar" value={money(d.pendingCollections.amount)} sub="Vadesi henüz gelmemiş" to="/satislar?openBalance=1" title={def.pending} />
        <Stat label="Taslak faturalar" value={String(d.draftInvoices.count)} sub={money(d.draftInvoices.amount)} to="/faturalar?internalStatus=Draft" title={def.draftInvoices} />
        <Stat label="Resmî işlem bekleyen faturalar" value={String(d.awaitingOfficialInvoices.count)} sub={`${money(d.awaitingOfficialInvoices.amount)} · iç onaylı, resmî belge yok`} to="/faturalar?awaitingOfficial=1" tone={d.awaitingOfficialInvoices.count > 0 ? "warn" : undefined} title={def.awaiting} />
        <Stat label="Bu ay hazırlanan faturalar" value={String(d.monthInvoicesCreated.count)} sub={money(d.monthInvoicesCreated.amount)} to={`/faturalar?${m}`} title={def.monthInvoices} />
        <Stat label="Azalan stoklar" value={String(d.lowStock.count)} sub="ürün minimum seviyede/altında" to="/stok?tab=low" tone={d.lowStock.count > 0 ? "warn" : undefined} />
        {d.customerAdvances > 0 && <Stat label="Müşteri avansları" value={money(d.customerAdvances)} sub="Fazla ödeme / avans bakiyesi" to="/musteriler?balance=credit" />}
      </div>

      <div className="grid cols-2">
        <Card title="Son satışlar" actions={<Link to="/satislar" className="small">Tümü</Link>} flush>
          <DataTable rows={d.recentSales} rowKey={(r) => r.id} rowHref={(r) => `/satislar/${r.id}`} empty={<EmptyState title="Henüz satış yok" text="İlk satışınızı oluşturun." action={<LinkButton to="/satislar/yeni">+ Yeni satış</LinkButton>} />}
            columns={[
              { header: "Satış", title: true, cell: (r) => <><strong>{r.customerName}</strong><div className="muted small">{r.number} · {dateTr(r.date)}</div></> },
              { header: "Tutar", num: true, cell: (r) => <Money value={r.grandTotal} /> },
              { header: "Durum", cell: (r) => <Badge tone={r.status === "Paid" ? "good" : r.status === "Cancelled" ? "bad" : r.status === "Draft" ? "neutral" : "info"}>{L.saleStatus[r.status]}</Badge> },
            ]} />
        </Card>
        <Card title="Son tahsilatlar" actions={<Link to="/tahsilatlar" className="small">Tümü</Link>} flush>
          <DataTable rows={d.recentPayments} rowKey={(r) => r.id} rowHref={(r) => `/tahsilatlar/${r.id}`} empty={<EmptyState title="Henüz tahsilat yok" />}
            columns={[
              { header: "Tahsilat", title: true, cell: (r) => <><strong>{r.customerName}</strong><div className="muted small">{r.number} · {dateTr(r.date)}</div></> },
              { header: "Yöntem", cell: (r) => L.method[r.method] },
              { header: "Tutar", num: true, cell: (r) => <Money value={r.amount} /> },
            ]} />
        </Card>
        <Card title="Son faturalar" actions={<Link to="/faturalar" className="small">Tümü</Link>} flush>
          <DataTable rows={d.recentInvoices} rowKey={(r) => r.id} rowHref={(r) => `/faturalar/${r.id}`} empty={<EmptyState title="Henüz fatura yok" text="Onaylı bir satıştan fatura taslağı hazırlayabilirsiniz." />}
            columns={[
              { header: "Fatura", title: true, cell: (r) => <><strong>{r.customerName}</strong><div className="muted small">{r.officialNumber ?? r.internalRef} · {dateTr(r.date)}</div></> },
              { header: "Tutar", num: true, cell: (r) => <Money value={r.grandTotal} /> },
              { header: "Süreç", cell: (r) => <div className="stack-sm"><Badge tone="info">{L.internal[r.internalStatus]}</Badge><Badge tone={r.officialStatus === "Issued" ? "good" : "neutral"}>{L.official[r.officialStatus]}</Badge></div> },
            ]} />
        </Card>
        <Card title="Yaklaşan ödeme vadeleri (14 gün)" flush>
          <DataTable rows={d.upcomingDues} rowKey={(r) => r.saleId} rowHref={(r) => `/satislar/${r.saleId}`} empty={<EmptyState title="Yaklaşan vade yok" />}
            columns={[
              { header: "Müşteri", title: true, cell: (r) => <><strong>{r.customerName}</strong><div className="muted small">{r.saleNumber}</div></> },
              { header: "Vade", cell: (r) => dateTr(r.dueDate) },
              { header: "Kalan", num: true, cell: (r) => <Money value={r.remaining} /> },
            ]} />
        </Card>
        <Card title="En yüksek bakiyeli müşteriler" flush>
          <DataTable rows={d.topBalances} rowKey={(r) => r.customerId} rowHref={(r) => `/musteriler/${r.customerId}`} empty={<EmptyState title="Borçlu müşteri yok" />}
            columns={[
              { header: "Müşteri", title: true, cell: (r) => <strong>{r.customerName}</strong> },
              { header: "Bakiye", num: true, cell: (r) => <Balance value={r.balance} /> },
              { header: "Vadesi geçmiş", num: true, cell: (r) => r.overdue > 0 ? <span className="pos mono">{money(r.overdue)}</span> : "—" },
            ]} />
        </Card>
        <Card title="Azalan stoklar" actions={<Link to="/stok?tab=low" className="small">Tümü</Link>} flush>
          <DataTable rows={d.lowStockItems} rowKey={(r) => r.productId} empty={<EmptyState title="Stok seviyeleri yeterli" />}
            columns={[
              { header: "Ürün", title: true, cell: (r) => <strong>{r.name}</strong> },
              { header: "Mevcut", num: true, cell: (r) => `${num(r.quantity)} ${r.unit}` },
              { header: "Minimum", num: true, cell: (r) => `${num(r.minStock)} ${r.unit}` },
            ]} />
        </Card>
      </div>
      <p className="muted small">Bakiye = müşterinin bize borcu (açılış bakiyesi dahil). Kartların üzerine gelince tanımları görürsünüz; kartlara tıklayınca ilgili liste filtreli açılır.</p>
    </div>
  );
}
