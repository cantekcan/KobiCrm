import { useState } from "react";
import { Link, useParams } from "react-router-dom";
import { useQuery } from "@tanstack/react-query";
import { api } from "../lib/api";
import type { CustomerListItem, Paged, Statement, SupplierListItem } from "../lib/types";
import { Balance, Button, Card, DataTable, EmptyState, ErrorBlock, LinkButton, Money, PageHeader, Pager, Spinner, Tabs, TextInput, useDebounced } from "../components/ui";
import { StatementTable } from "../components/statement";
import { dateTr, firstOfMonthIso, todayIso } from "../lib/format";

export function LedgerPage() {
  const [tab, setTab] = useState<"customers" | "suppliers">("customers");
  const [text, setText] = useState("");
  const q = useDebounced(text);
  const [page, setPage] = useState(1);
  const [onlyBal, setOnlyBal] = useState(true);
  const cust = useQuery({ queryKey: ["ledger-customers", q, page, onlyBal], enabled: tab === "customers", queryFn: () => api.get<Paged<CustomerListItem>>("/customers", { search: q, page, pageSize: 25, sort: "balance", desc: "true", balance: onlyBal ? undefined : undefined }) });
  const supp = useQuery({ queryKey: ["ledger-suppliers", q], enabled: tab === "suppliers", queryFn: () => api.get<SupplierListItem[]>("/suppliers", { search: q }) });
  const rows = (cust.data?.items ?? []).filter((c) => !onlyBal || c.balance !== 0);

  return (
    <div className="stack">
      <PageHeader title="Cari hesaplar" subtitle="Müşteri alacakları ve tedarikçi borçları ayrı hesaplarda izlenir" />
      <Tabs value={tab} onChange={(t) => { setTab(t); setPage(1); }} tabs={[{ id: "customers", label: "Müşteri alacakları" }, { id: "suppliers", label: "Tedarikçi borçları" }]} />
      <div className="filters">
        <TextInput label="Ara" value={text} onChange={(e) => setText(e.target.value)} placeholder="Ad veya telefon" />
        {tab === "customers" && <label className="check" style={{ alignSelf: "center" }}><input type="checkbox" checked={onlyBal} onChange={(e) => setOnlyBal(e.target.checked)} /> Yalnızca bakiyesi olanlar</label>}
      </div>
      {tab === "customers" ? (
        <Card flush>
          {cust.isLoading ? <Spinner /> : cust.error ? <ErrorBlock error={cust.error} /> : (
            <>
              <DataTable rows={rows} rowKey={(r) => r.id} rowHref={(r) => `/cari/musteri/${r.id}`} empty={<EmptyState title="Bakiyesi olan müşteri yok" text="Satış veya açılış bakiyesi girildiğinde burada görünür." />}
                columns={[
                  { header: "Müşteri", title: true, cell: (r) => <strong>{r.name}</strong> },
                  { header: "Bakiye", num: true, cell: (r) => <Balance value={r.balance} /> },
                  { header: "Vadesi geçmiş", num: true, cell: (r) => r.overdueAmount > 0 ? <span className="pos mono">{r.overdueAmount.toLocaleString("tr-TR", { style: "currency", currency: "TRY" })}</span> : "—" },
                  { header: "Son satış", cell: (r) => dateTr(r.lastSaleDate) },
                  { header: "Son tahsilat", cell: (r) => dateTr(r.lastPaymentDate) },
                ]} />
              <Pager page={cust.data!.page} pageSize={cust.data!.pageSize} total={cust.data!.total} onPage={setPage} />
            </>
          )}
        </Card>
      ) : (
        <Card flush>
          {supp.isLoading ? <Spinner /> : (
            <DataTable rows={supp.data ?? []} rowKey={(r) => r.id} rowHref={(r) => `/cari/tedarikci/${r.id}`} empty={<EmptyState title="Tedarikçi yok" action={<LinkButton to="/tedarikciler/yeni">+ Tedarikçi ekle</LinkButton>} />}
              columns={[
                { header: "Tedarikçi", title: true, cell: (r) => <strong>{r.name}</strong> },
                { header: "Borcumuz", num: true, cell: (r) => <Money value={r.balance} /> },
                { header: "Son alış", cell: (r) => dateTr(r.lastPurchaseDate) },
              ]} />
          )}
        </Card>
      )}
    </div>
  );
}

/** Müşteri veya tedarikçi cari ekstresi (tarih filtreli, yazdırılabilir). */
export function StatementPage({ kind }: { kind: "customer" | "supplier" }) {
  const { id } = useParams();
  const [from, setFrom] = useState("");
  const [to, setTo] = useState("");
  const path = kind === "customer" ? `/customers/${id}/statement` : `/ledger/suppliers/${id}/statement`;
  const { data, isLoading, error, refetch } = useQuery({ queryKey: ["statement", kind, id, from, to], queryFn: () => api.get<Statement>(path, { from, to }) });
  const back = kind === "customer" ? `/musteriler/${id}` : `/tedarikciler/${id}`;
  return (
    <div className="stack">
      <PageHeader title={`Cari ekstre${data ? ` — ${data.accountName}` : ""}`} back={{ to: "/cari", label: "Cari hesaplar" }}
        subtitle={`${kind === "customer" ? "Müşteri" : "Tedarikçi"} · ${from || to ? `${from ? dateTr(from) : "…"} – ${to ? dateTr(to) : "…"}` : "tüm hareketler"} · düzenlenme ${dateTr(todayIso())}`}
        actions={<><Button variant="secondary" onClick={() => window.print()}>Yazdır</Button><Link className="btn secondary" to={back}>{kind === "customer" ? "Müşteri sayfası" : "Tedarikçi sayfası"}</Link></>} />
      <div className="filters no-print">
        <TextInput label="Başlangıç" type="date" value={from} onChange={(e) => setFrom(e.target.value)} />
        <TextInput label="Bitiş" type="date" value={to} onChange={(e) => setTo(e.target.value)} />
        <Button variant="secondary" onClick={() => { setFrom(firstOfMonthIso()); setTo(todayIso()); }}>Bu ay</Button>
        <Button variant="secondary" onClick={() => { setFrom(""); setTo(""); }}>Tümü</Button>
      </div>
      <Card flush>{isLoading ? <Spinner /> : error || !data ? <ErrorBlock error={error} onRetry={refetch} /> : <StatementTable st={data} kind={kind} />}</Card>
    </div>
  );
}
