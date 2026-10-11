import { Link, useNavigate, useParams } from "react-router-dom";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "../lib/api";
import type { Paged, Payment, Sale } from "../lib/types";
import { L } from "../lib/types";
import { Badge, Button, Card, DataTable, ErrorBlock, LinkButton, Money, Notice, PageHeader, Spinner, useConfirm, useToast } from "../components/ui";
import { saleTone } from "./Sales";
import { dateTr, money, num } from "../lib/format";

export function SaleDetailPage() {
  const { id } = useParams();
  const nav = useNavigate();
  const qc = useQueryClient();
  const toast = useToast();
  const confirm = useConfirm();
  const { data: s, isLoading, error, refetch } = useQuery({ queryKey: ["sale", id], queryFn: () => api.get<Sale>(`/sales/${id}`) });
  const { data: pays } = useQuery({
    queryKey: ["sale-payments", s?.customerId], enabled: !!s,
    queryFn: () => api.get<Paged<Payment>>("/payments", { customerId: s!.customerId, pageSize: 100 }),
  });

  const approve = useMutation({
    mutationFn: () => api.post<Sale>(`/sales/${id}/approve`),
    onSuccess: (r) => { qc.invalidateQueries(); toast.success(`Satış onaylandı ve ${money(r.grandTotal)} müşterinin cari hesabına işlendi.`); r.warnings?.forEach((w) => toast.error(w)); },
  });
  const cancel = useMutation({
    mutationFn: (reason: string) => api.post<Sale>(`/sales/${id}/cancel`, { reason }),
    onSuccess: () => { qc.invalidateQueries(); toast.success("Satış iptal edildi. Cari hesaba ters kayıt işlendi, stok geri alındı."); },
  });
  const makeInvoice = useMutation({
    mutationFn: () => api.post<{ id: string }>("/invoices/draft", { saleId: id }),
    onSuccess: (inv) => { qc.invalidateQueries(); toast.success("Fatura TASLAĞI hazırlandı. Bu henüz resmî fatura değildir."); nav(`/faturalar/${inv.id}`); },
  });

  if (isLoading) return <Spinner />;
  if (error || !s) return <ErrorBlock error={error} onRetry={refetch} />;

  const myAllocs = (pays?.items ?? []).flatMap((p) => p.allocations.filter((a) => a.saleId === s.id).map((a) => ({ p, a })));
  const activeInvoice = s.invoices.find((i) => i.internalStatus !== "Cancelled");
  const commercial = s.status === "Approved" || s.status === "PartiallyPaid" || s.status === "Paid";

  const doCancel = async () => {
    const r = await confirm({
      title: "Satışı iptal et", danger: true, confirmLabel: "Satışı iptal et", reasonLabel: "İptal nedeni (zorunlu)",
      message: <div className="stack-sm">
        <p>{s.number} numaralı satış iptal edilecek:</p>
        <ul><li>Müşterinin cari hesabına <strong>ters kayıt</strong> düşülür ({money(s.grandTotal)}); orijinal kayıt silinmez.</li>
          <li>Satılan ürünler stoğa geri girer.</li>
          <li>Bu satışa yapılan tahsilatlar silinmez; <strong>müşterinin dağıtılmamış bakiyesi</strong> olarak kalır.</li>
          <li>Henüz resmîleşmemiş fatura taslakları iptal edilir.</li></ul>
        {s.invoices.some((i) => i.officialStatus === "Issued") && <Notice tone="warn">Bu satışın resmî faturası var. Önce faturanın resmî iptal sürecini tamamlayın; aksi halde iptal reddedilir.</Notice>}
      </div>,
    });
    if (r.ok) cancel.mutate(r.reason);
  };

  return (
    <div className="stack">
      <PageHeader title={<>{s.number} <Badge tone={saleTone(s.status)}>{L.saleStatus[s.status]}</Badge></>} back={{ to: "/satislar", label: "Satışlar" }}
        subtitle={<><Link to={`/musteriler/${s.customerId}`}>{s.customerName}</Link> · {dateTr(s.saleDate)} · {L.paymentType[s.paymentType]}{s.paymentType === "Credit" && ` · vade ${dateTr(s.dueDate)}`}</>}
        actions={<>
          {s.status === "Draft" && <><Button loading={approve.isPending} onClick={() => approve.mutate()}>Onayla ve cariye işle</Button><LinkButton to={`/satislar/${s.id}/duzenle`} variant="secondary">Düzenle</LinkButton></>}
          {(s.status === "Approved" || s.status === "PartiallyPaid") && <LinkButton to={`/tahsilatlar/yeni?musteri=${s.customerId}&satis=${s.id}`}>+ Tahsilat gir</LinkButton>}
          {commercial && !activeInvoice && <Button variant="secondary" loading={makeInvoice.isPending} onClick={() => makeInvoice.mutate()}>Fatura taslağı hazırla</Button>}
          {activeInvoice && <LinkButton to={`/faturalar/${activeInvoice.id}`} variant="secondary">Faturayı aç ({activeInvoice.officialNumber ?? activeInvoice.internalRef})</LinkButton>}
          {s.status !== "Cancelled" && <Button variant="outline-danger" loading={cancel.isPending} onClick={doCancel}>{s.status === "Draft" ? "Taslağı iptal et" : "Satışı iptal et"}</Button>}
        </>} />

      {s.status === "Cancelled" && <Notice tone="error" title="Bu satış iptal edildi">{s.cancelReason}</Notice>}
      {s.status === "Draft" && <Notice tone="info">Bu satış <strong>taslak</strong>: henüz müşterinin cari hesabına işlenmedi ve stok düşmedi.</Notice>}
      {s.warnings.map((w) => <Notice key={w} tone="warn">{w}</Notice>)}

      <div className="grid cols-4 cols-2-mobile">
        <div className="stat"><div className="label">Genel toplam</div><div className="value">{money(s.grandTotal)}</div><div className="sub">KDV hariç {money(s.netTotal)} · KDV {money(s.vatTotal)}</div></div>
        <div className="stat good"><div className="label">Tahsil edilen</div><div className="value">{commercial ? money(s.paidTotal) : "—"}</div></div>
        <div className={`stat ${s.remaining > 0 ? "warn" : ""}`}><div className="label">Kalan borç</div><div className="value">{commercial ? money(s.remaining) : "—"}</div></div>
        <div className="stat"><div className="label">İndirim</div><div className="value">{money(s.discountTotal)}</div></div>
      </div>

      <Card title="Kalemler" flush>
        <DataTable rows={s.items} rowKey={(i) => i.id} footer
          columns={[
            { header: "Ürün", title: true, cell: (i) => <><strong>{i.productName}</strong><div className="muted small">{i.productCode}</div></>, foot: "Toplam" },
            { header: "Miktar", num: true, cell: (i) => `${num(i.quantity)} ${i.unit}` },
            { header: "Birim fiyat", num: true, cell: (i) => <Money value={i.unitPrice} /> },
            { header: "İndirim", num: true, cell: (i) => i.discountPercent > 0 || i.discountAmount > 0 ? `${i.discountPercent > 0 ? `%${num(i.discountPercent)}` : ""} ${i.discountAmount > 0 ? money(i.discountAmount) : ""}` : "—" },
            { header: "KDV", num: true, cell: (i) => `%${num(i.vatRate)} · ${money(i.vatAmount)}`, foot: <Money value={s.vatTotal} /> },
            { header: "Satır toplamı", num: true, cell: (i) => <strong><Money value={i.lineTotal} /></strong>, foot: <Money value={s.grandTotal} /> },
          ]} />
      </Card>

      <div className="grid cols-2">
        <Card title="Tahsilatlar">
          <DataTable rows={myAllocs} rowKey={(x) => x.a.id} empty={<p className="muted">Bu satış için henüz tahsilat dağıtılmamış.</p>}
            columns={[
              { header: "Tahsilat", title: true, cell: ({ p }) => <Link to={`/tahsilatlar/${p.id}`}>{p.number}</Link> },
              { header: "Tarih", cell: ({ p }) => dateTr(p.paymentDate) },
              { header: "Tutar", num: true, cell: ({ a }) => <Money value={a.amount} /> },
              { header: "Durum", cell: ({ a }) => a.isActive ? <Badge tone="good">Geçerli</Badge> : <Badge tone="bad">Geri alındı / serbest</Badge> },
            ]} />
        </Card>
        <Card title="Faturalar">
          {s.invoices.length === 0 ? <p className="muted">Bu satış için fatura hazırlanmamış. Fatura taslağı hazırlamak resmî fatura kesmek değildir.</p> : (
            <DataTable rows={s.invoices} rowKey={(i) => i.id} rowHref={(i) => `/faturalar/${i.id}`}
              columns={[
                { header: "Fatura", title: true, cell: (i) => <strong>{i.officialNumber ?? i.internalRef}</strong> },
                { header: "Süreç", cell: (i) => <Badge tone="info">{L.internal[i.internalStatus]}</Badge> },
                { header: "Resmî belge", cell: (i) => <Badge tone={i.officialStatus === "Issued" ? "good" : "neutral"}>{L.official[i.officialStatus]}</Badge> },
              ]} />
          )}
        </Card>
      </div>
      {s.note && <Card title="Not">{s.note}</Card>}
    </div>
  );
}
