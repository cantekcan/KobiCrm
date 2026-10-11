import { useState } from "react";
import { Link, useNavigate, useParams, useSearchParams } from "react-router-dom";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api, ApiError } from "../lib/api";
import type { CustomerDetail, CustomerPrice, InvoiceListItem, Paged, Payment, SaleListItem, Statement } from "../lib/types";
import { L } from "../lib/types";
import { Badge, Balance, Button, Card, DataTable, EmptyState, ErrorBlock, FormError, KV, LinkButton, Modal, Money, NumberInput, Notice, PageHeader, Pager, Select, Spinner, Tabs, TextArea, TextInput, useConfirm, useToast } from "../components/ui";
import { StatementTable } from "../components/statement";
import { addDays, dateTr, firstOfMonthIso, money, num, parseDecimal, todayIso } from "../lib/format";

type Tab = "ledger" | "sales" | "invoices" | "payments" | "products" | "prices";

export function CustomerDetailPage() {
  const { id } = useParams();
  const nav = useNavigate();
  const [sp, setSp] = useSearchParams();
  const tab = (sp.get("tab") as Tab) ?? "ledger";
  const [modal, setModal] = useState<null | "opening" | "entry">(null);
  const { data: c, isLoading, error, refetch } = useQuery({ queryKey: ["customer", id], queryFn: () => api.get<CustomerDetail>(`/customers/${id}`) });

  if (isLoading) return <Spinner />;
  if (error || !c) return <ErrorBlock error={error} onRetry={refetch} />;

  return (
    <div className="stack">
      <PageHeader title={<>{c.name} {!c.isActive && <Badge>Pasif</Badge>}</>} back={{ to: "/musteriler", label: "Müşteriler" }}
        subtitle={<>{L.customerType[c.customerType]} · {c.phone ?? "telefon yok"} · vade {c.paymentTermDays} gün</>}
        actions={<>
          <LinkButton to={`/satislar/yeni?musteri=${c.id}`}>+ Yeni satış</LinkButton>
          <LinkButton to={`/tahsilatlar/yeni?musteri=${c.id}`} variant="secondary">+ Tahsilat gir</LinkButton>
          <LinkButton to={`/faturalar?customerId=${c.id}&hazirla=1`} variant="secondary">Yeni fatura hazırla</LinkButton>
          <LinkButton to={`/musteriler/${c.id}/duzenle`} variant="ghost">Düzenle</LinkButton>
        </>} />

      <div className="grid cols-4 cols-2-mobile">
        <div className="stat"><div className="label">Güncel bakiye</div><div className="value"><Balance value={c.balance} /></div><div className="sub">{c.balance > 0 ? "Müşteri bize borçlu" : c.balance < 0 ? "Müşteri avans/fazla ödeme yapmış" : "Borç yok"}</div></div>
        <div className={`stat ${c.overdueAmount > 0 ? "danger" : ""}`}><div className="label">Vadesi geçmiş</div><div className="value">{money(c.overdueAmount)}</div></div>
        <div className="stat"><div className="label">Toplam satışlar</div><div className="value">{money(c.totalSales)}</div><div className="sub">Açılış bakiyesi hariç</div></div>
        <div className="stat"><div className="label">Toplam tahsilatlar</div><div className="value">{money(c.totalPayments)}</div><div className="sub">{c.unallocatedCredit > 0 ? `${money(c.unallocatedCredit)} genel bakiyeden mahsup` : "Geri alınanlar hariç"}</div></div>
      </div>

      <Card title="Müşteri bilgileri" actions={!c.hasOpeningBalance ? <Button size="sm" variant="secondary" onClick={() => setModal("opening")}>Açılış bakiyesi gir</Button> : <Button size="sm" variant="secondary" onClick={() => setModal("entry")}>Cari hareket ekle (iade/düzeltme)</Button>}>
        <KV items={[
          ["Vergi dairesi / no", [c.taxOffice, c.taxNumber ?? c.nationalId].filter(Boolean).join(" / ") || "—"],
          ["Adres", [c.address, c.district, c.city].filter(Boolean).join(", ") || "—"],
          ["E-posta", c.email], ["e-Fatura durumu", <Badge tone={c.eInvoiceStatus === "Unknown" ? "warn" : "info"}>{L.einvoice[c.eInvoiceStatus]}</Badge>],
          ["Açılış bakiyesi", c.hasOpeningBalance ? <>{money(c.openingBalanceAmount)} · {dateTr(c.openingBalanceDate)} · {c.openingReference}</> : "Girilmemiş"],
          ["Not", c.note],
        ]} />
      </Card>

      <Tabs value={tab} onChange={(t) => setSp({ tab: t }, { replace: true })} tabs={[
        { id: "ledger", label: "Cari hareketler" }, { id: "sales", label: "Satışlar" }, { id: "invoices", label: "Faturalar" },
        { id: "payments", label: "Tahsilatlar" }, { id: "products", label: "Ürün geçmişi" }, { id: "prices", label: "Özel fiyatlar" },
      ]} />
      {tab === "ledger" && <LedgerTab id={c.id} />}
      {tab === "sales" && <SalesTab id={c.id} />}
      {tab === "invoices" && <InvoicesTab id={c.id} />}
      {tab === "payments" && <PaymentsTab id={c.id} />}
      {tab === "products" && <ProductsTab id={c.id} />}
      {tab === "prices" && <PricesTab id={c.id} />}

      {modal === "opening" && <OpeningModal customerId={c.id} onClose={() => setModal(null)} />}
      {modal === "entry" && <EntryModal customerId={c.id} onClose={() => setModal(null)} />}
    </div>
  );
}

function LedgerTab({ id }: { id: string }) {
  const [from, setFrom] = useState("");
  const [to, setTo] = useState("");
  const { data, isLoading, error } = useQuery({ queryKey: ["statement", id, from, to], queryFn: () => api.get<Statement>(`/customers/${id}/statement`, { from, to }) });
  return (
    <Card flush>
      <div className="filters no-print" style={{ padding: "1rem 1.2rem 0" }}>
        <TextInput label="Başlangıç" type="date" value={from} onChange={(e) => setFrom(e.target.value)} />
        <TextInput label="Bitiş" type="date" value={to} onChange={(e) => setTo(e.target.value)} />
        <Button variant="secondary" onClick={() => { setFrom(""); setTo(""); }}>Temizle</Button>
        <Button variant="secondary" onClick={() => window.print()}>Yazdır</Button>
      </div>
      {isLoading ? <Spinner /> : error ? <ErrorBlock error={error} /> : <StatementTable st={data!} />}
    </Card>
  );
}

function SalesTab({ id }: { id: string }) {
  const [page, setPage] = useState(1);
  const { data, isLoading } = useQuery({ queryKey: ["sales", { customerId: id, page }], queryFn: () => api.get<Paged<SaleListItem>>("/sales", { customerId: id, page, pageSize: 10 }) });
  if (isLoading) return <Spinner />;
  return (
    <Card flush>
      <DataTable rows={data!.items} rowKey={(r) => r.id} rowHref={(r) => `/satislar/${r.id}`} empty={<EmptyState title="Bu müşteriye satış yok" action={<LinkButton to={`/satislar/yeni?musteri=${id}`}>+ Yeni satış</LinkButton>} />}
        columns={[
          { header: "Satış", title: true, cell: (r) => <><strong>{r.number}</strong><div className="muted small">{dateTr(r.saleDate)} · {L.paymentType[r.paymentType]}</div></> },
          { header: "Tutar", num: true, cell: (r) => <Money value={r.grandTotal} /> },
          { header: "Kalan", num: true, cell: (r) => r.remaining > 0 ? <Money value={r.remaining} /> : "—" },
          { header: "Durum", cell: (r) => <Badge tone={r.status === "Paid" ? "good" : r.status === "Cancelled" ? "bad" : "info"}>{L.saleStatus[r.status]}</Badge> },
          { header: "Fatura", cell: (r) => r.invoiceRef ?? "—" },
        ]} />
      <Pager page={page} pageSize={10} total={data!.total} onPage={setPage} />
    </Card>
  );
}

function InvoicesTab({ id }: { id: string }) {
  const { data, isLoading } = useQuery({ queryKey: ["invoices", { customerId: id }], queryFn: () => api.get<Paged<InvoiceListItem>>("/invoices", { customerId: id, pageSize: 50 }) });
  if (isLoading) return <Spinner />;
  return (
    <Card flush>
      <DataTable rows={data!.items} rowKey={(r) => r.id} rowHref={(r) => `/faturalar/${r.id}`} empty={<EmptyState title="Bu müşteri için fatura yok" text="Onaylı bir satıştan fatura taslağı hazırlayabilirsiniz." />}
        columns={[
          { header: "Fatura", title: true, cell: (r) => <><strong>{r.officialNumber ?? r.internalRef}</strong><div className="muted small">{dateTr(r.invoiceDate)} · {L.docType[r.documentType]}</div></> },
          { header: "Tutar", num: true, cell: (r) => <Money value={r.grandTotal} /> },
          { header: "Kalan", num: true, cell: (r) => <Money value={r.remaining} /> },
          { header: "Süreç", cell: (r) => <Badge tone="info">{L.internal[r.internalStatus]}</Badge> },
          { header: "Resmî belge", cell: (r) => <Badge tone={r.officialStatus === "Issued" ? "good" : "neutral"}>{L.official[r.officialStatus]}</Badge> },
        ]} />
    </Card>
  );
}

function PaymentsTab({ id }: { id: string }) {
  const { data, isLoading } = useQuery({ queryKey: ["payments", { customerId: id }], queryFn: () => api.get<Paged<Payment>>("/payments", { customerId: id, pageSize: 50 }) });
  if (isLoading) return <Spinner />;
  return (
    <Card flush>
      <DataTable rows={data!.items} rowKey={(r) => r.id} rowHref={(r) => `/tahsilatlar/${r.id}`} empty={<EmptyState title="Tahsilat yok" action={<LinkButton to={`/tahsilatlar/yeni?musteri=${id}`}>+ Tahsilat gir</LinkButton>} />}
        columns={[
          { header: "Tahsilat", title: true, cell: (r) => <><strong>{r.number}</strong><div className="muted small">{dateTr(r.paymentDate)} · {L.method[r.method]}</div></> },
          { header: "Tutar", num: true, cell: (r) => <Money value={r.amount} /> },
          { header: "Dağıtılmamış", num: true, cell: (r) => r.status === "Active" && r.unallocatedAmount > 0 ? <Money value={r.unallocatedAmount} /> : "—" },
          { header: "Durum", cell: (r) => r.status === "Reversed" ? <Badge tone="bad">Geri alındı</Badge> : <Badge tone="good">Geçerli</Badge> },
        ]} />
    </Card>
  );
}

function ProductsTab({ id }: { id: string }) {
  const { data, isLoading } = useQuery({ queryKey: ["product-history", id], queryFn: () => api.get<{ productId: string; productName: string; unit: string; totalQuantity: number; totalAmount: number; lastUnitPrice: number; lastSaleDate: string; saleCount: number }[]>(`/customers/${id}/product-history`) });
  if (isLoading) return <Spinner />;
  return (
    <Card flush>
      <DataTable rows={data!} rowKey={(r) => r.productId} empty={<EmptyState title="Henüz ürün geçmişi yok" text="Onaylanan satışlardaki ürünler burada özetlenir." />}
        columns={[
          { header: "Ürün", title: true, cell: (r) => <strong>{r.productName}</strong> },
          { header: "Toplam miktar", num: true, cell: (r) => `${num(r.totalQuantity)} ${r.unit}` },
          { header: "Toplam (KDV hariç)", num: true, cell: (r) => <Money value={r.totalAmount} /> },
          { header: "Son birim fiyat", num: true, cell: (r) => <Money value={r.lastUnitPrice} /> },
          { header: "Son satış", cell: (r) => dateTr(r.lastSaleDate) },
          { header: "Satış sayısı", num: true, cell: (r) => r.saleCount },
        ]} />
    </Card>
  );
}

function PricesTab({ id }: { id: string }) {
  const qc = useQueryClient();
  const toast = useToast();
  const confirm = useConfirm();
  const { data, isLoading } = useQuery({ queryKey: ["customer-prices", id], queryFn: () => api.get<CustomerPrice[]>(`/customers/${id}/prices`) });
  const del = useMutation({ mutationFn: (productId: string) => api.del(`/customers/${id}/prices/${productId}`), onSuccess: () => { qc.invalidateQueries({ queryKey: ["customer-prices", id] }); toast.success("Özel fiyat kaldırıldı."); } });
  if (isLoading) return <Spinner />;
  return (
    <Card>
      <Notice tone="info">Bu müşteriye özel fiyatlar yeni satışta otomatik kullanılır. Fiyatları ürün sayfasından (Ürünler → ürün → Müşteriye özel fiyatlar) ekleyebilirsiniz. Fiyat değişse de geçmiş satışlar değişmez.</Notice>
      <div style={{ marginTop: ".8rem" }}>
        <DataTable rows={data!} rowKey={(r) => r.id} empty={<EmptyState title="Özel fiyat tanımlı değil" />}
          columns={[
            { header: "Ürün", title: true, cell: (r) => <strong>{r.productName}</strong> },
            { header: "Özel fiyat (KDV hariç)", num: true, cell: (r) => <Money value={r.price} /> },
            { header: "Varsayılan", num: true, cell: (r) => <Money value={r.defaultPrice} /> },
            { header: "", cell: (r) => <Button size="sm" variant="outline-danger" onClick={async () => { if ((await confirm({ title: "Özel fiyatı kaldır", message: `${r.productName} için özel fiyat kaldırılacak; sonraki satışlarda varsayılan fiyat kullanılır. Geçmiş satışlar etkilenmez.`, danger: true, confirmLabel: "Kaldır" })).ok) del.mutate(r.productId); }}>Kaldır</Button> },
          ]} />
      </div>
    </Card>
  );
}

function OpeningModal({ customerId, onClose }: { customerId: string; onClose: () => void }) {
  const qc = useQueryClient();
  const toast = useToast();
  const [f, setF] = useState({ amount: "", date: todayIso(), dueDate: "", description: "Geçmiş dönemden devreden müşteri borcu" });
  const amount = parseDecimal(f.amount);
  const m = useMutation({
    mutationFn: () => api.post(`/customers/${customerId}/opening-balance`, { amount, date: f.date, dueDate: f.dueDate || null, description: f.description || null }),
    onSuccess: () => { qc.invalidateQueries(); toast.success(`${money(amount!)} açılış bakiyesi cari hesaba işlendi.`); onClose(); },
  });
  return (
    <Modal title="Açılış bakiyesi gir" onClose={onClose} footer={<><Button variant="secondary" onClick={onClose}>Vazgeç</Button><Button loading={m.isPending} disabled={!amount} onClick={() => m.mutate()}>Cari hesaba işle</Button></>}>
      <div className="stack">
        <Notice tone="info">Müşterinin geçmişten kalan borcu cari hesaba <strong>açılış hareketi</strong> olarak işlenir. Fatura oluşmaz, satış raporuna girmez. <strong>Müşteri başına yalnızca bir kez</strong> girilebilir; sonradan değişiklik “Düzeltme” hareketiyle yapılır.</Notice>
        <NumberInput label="Açılış borcu (TL)" required value={f.amount} onChange={(v) => setF({ ...f, amount: v })} hint="Müşterinin bize olan borcu" />
        <TextInput label="Açılış tarihi" type="date" value={f.date} onChange={(e) => setF({ ...f, date: e.target.value })} />
        <TextInput label="Vade tarihi (isteğe bağlı)" type="date" value={f.dueDate} onChange={(e) => setF({ ...f, dueDate: e.target.value })} />
        <TextInput label="Açıklama" value={f.description} onChange={(e) => setF({ ...f, description: e.target.value })} />
        <FormError error={m.error} />
      </div>
    </Modal>
  );
}

function EntryModal({ customerId, onClose }: { customerId: string; onClose: () => void }) {
  const qc = useQueryClient();
  const toast = useToast();
  const [f, setF] = useState({ entryType: "Adjustment", direction: "decrease", amount: "", date: todayIso(), description: "" });
  const amount = parseDecimal(f.amount);
  const m = useMutation({
    mutationFn: () => api.post(`/customers/${customerId}/ledger-entries`, { entryType: f.entryType, date: f.date, increasesDebt: f.entryType === "Return" ? false : f.direction === "increase", amount, description: f.description }),
    onSuccess: () => { qc.invalidateQueries(); toast.success("Cari hareket eklendi."); onClose(); },
  });
  return (
    <Modal title="Cari hareket ekle" onClose={onClose} footer={<><Button variant="secondary" onClick={onClose}>Vazgeç</Button><Button loading={m.isPending} disabled={!amount || !f.description.trim()} onClick={() => m.mutate()}>Kaydet</Button></>}>
      <div className="stack">
        <Notice tone="warn">Mevcut kayıtlar değiştirilmez; bu işlem cari hesaba <strong>yeni bir hareket</strong> ekler. Yanlış girilmiş bir tahsilat veya satış için ilgili kaydın kendi ekranından “geri al / iptal et” kullanın.</Notice>
        <Select label="Hareket türü" value={f.entryType} onChange={(e) => setF({ ...f, entryType: e.target.value })}>
          <option value="Adjustment">Düzeltme</option><option value="Return">İade (müşterinin borcunu azaltır)</option><option value="Manual">Diğer manuel hareket</option>
        </Select>
        {f.entryType !== "Return" && (
          <Select label="Etkisi" value={f.direction} onChange={(e) => setF({ ...f, direction: e.target.value })}>
            <option value="decrease">Müşterinin borcunu azalt</option><option value="increase">Müşterinin borcunu artır</option>
          </Select>
        )}
        <NumberInput label="Tutar (TL)" required value={f.amount} onChange={(v) => setF({ ...f, amount: v })} />
        <TextInput label="Tarih" type="date" value={f.date} onChange={(e) => setF({ ...f, date: e.target.value })} />
        <TextArea label="Açıklama (zorunlu)" value={f.description} onChange={(e) => setF({ ...f, description: e.target.value })} />
        <FormError error={m.error} />
      </div>
    </Modal>
  );
}
