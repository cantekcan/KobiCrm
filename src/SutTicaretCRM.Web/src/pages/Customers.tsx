import { useQuery } from "@tanstack/react-query";
import { useEffect, useState } from "react";
import { api } from "../lib/api";
import { useUrlFilters } from "../lib/hooks";
import type { CustomerListItem, Paged } from "../lib/types";
import { Badge, Balance, Card, DataTable, EmptyState, ErrorBlock, LinkButton, PageHeader, Pager, Select, Spinner, TextInput, useDebounced } from "../components/ui";
import { dateTr, money } from "../lib/format";

export function CustomersPage() {
  const [f, set] = useUrlFilters({ search: "", sort: "name", desc: "", balance: "", active: "true", page: "1" });
  const [text, setText] = useState(f.search);
  const dq = useDebounced(text);
  useEffect(() => { if (dq !== f.search) set({ search: dq }); }, [dq]); // eslint-disable-line react-hooks/exhaustive-deps

  const { data, isLoading, error, refetch } = useQuery({
    queryKey: ["customers", f],
    queryFn: () => api.get<Paged<CustomerListItem>>("/customers", {
      search: f.search, sort: f.sort, desc: f.desc ? "true" : undefined, balance: f.balance, active: f.active === "all" ? undefined : f.active === "true", page: f.page, pageSize: 25,
    }),
  });

  const chips: [string, string][] = [["", "Tümü"], ["debt", "Borçlular"], ["overdue", "Vadesi geçmiş"], ["clear", "Borcu yok"], ["credit", "Avansı olanlar"]];

  return (
    <div className="stack">
      <PageHeader title="Müşteriler" actions={<LinkButton to="/musteriler/yeni">+ Yeni müşteri</LinkButton>} />
      <div className="filters">
        <TextInput label="Ara" placeholder="Ad, telefon veya vergi no" value={text} onChange={(e) => setText(e.target.value)} />
        <Select label="Sırala" value={`${f.sort}${f.desc ? ":desc" : ""}`} onChange={(e) => { const [s, d] = e.target.value.split(":"); set({ sort: s, desc: d ? "1" : "" }); }}>
          <option value="name">Ada göre (A→Z)</option>
          <option value="balance:desc">Borca göre (çoktan aza)</option>
          <option value="overdue:desc">Vadesi geçmiş borca göre</option>
          <option value="lastSale:desc">Son satışa göre</option>
          <option value="lastPayment:desc">Son tahsilata göre</option>
        </Select>
        <Select label="Durum" value={f.active} onChange={(e) => set({ active: e.target.value })}>
          <option value="true">Aktif</option><option value="false">Pasif</option><option value="all">Hepsi</option>
        </Select>
      </div>
      <div className="chips">{chips.map(([v, l]) => <button key={v} className={`chip ${f.balance === v ? "on" : ""}`} onClick={() => set({ balance: v })}>{l}</button>)}</div>

      <Card flush>
        {isLoading ? <Spinner /> : error ? <div style={{ padding: "1rem" }}><ErrorBlock error={error} onRetry={refetch} /></div> : (
          <>
            <DataTable rows={data!.items} rowKey={(r) => r.id} rowHref={(r) => `/musteriler/${r.id}`}
              empty={<EmptyState title={f.search || f.balance ? "Aramanıza uyan müşteri yok" : "Henüz müşteri eklenmemiş"} text={f.search || f.balance ? "Filtreleri değiştirin." : "İlk müşterinizi ekleyin. Geçmiş borcu varsa açılış bakiyesi olarak girebilirsiniz."} action={!f.search && !f.balance ? <LinkButton to="/musteriler/yeni">+ Yeni müşteri</LinkButton> : undefined} />}
              columns={[
                { header: "Müşteri", title: true, cell: (r) => <><strong>{r.name}</strong> {!r.isActive && <Badge>Pasif</Badge>}<div className="muted small">{r.phone ?? "Telefon yok"}</div></> },
                { header: "Toplam borç", num: true, cell: (r) => <Balance value={r.balance} /> },
                { header: "Vadesi geçmiş", num: true, cell: (r) => r.overdueAmount > 0 ? <span className="pos mono">{money(r.overdueAmount)}</span> : <span className="muted">—</span> },
                { header: "Son satış", cell: (r) => dateTr(r.lastSaleDate) },
                { header: "Son tahsilat", cell: (r) => dateTr(r.lastPaymentDate) },
              ]} />
            <Pager page={data!.page} pageSize={data!.pageSize} total={data!.total} onPage={(p) => set({ page: String(p) }, false)} />
          </>
        )}
      </Card>
    </div>
  );
}
