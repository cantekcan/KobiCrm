import { Link } from "react-router-dom";
import { L, Statement } from "../lib/types";
import { Badge, Card, DataTable, EmptyState, Money } from "./ui";
import { dateTr, dateTimeTr } from "../lib/format";

/** Cari hesap hareket tablosu. Borç = müşterinin borcunu artırır, Alacak = azaltır. Bakiye pozitifse müşteri bize borçludur. */
export function StatementTable({ st, kind = "customer" }: { st: Statement; kind?: "customer" | "supplier" }) {
  const link = (e: Statement["entries"][number]) => {
    if (!e.sourceId) return null;
    if (e.sourceType === "Sale") return `/satislar/${e.sourceId}`;
    if (e.sourceType === "Payment") return `/tahsilatlar/${e.sourceId}`;
    return null;
  };
  return (
    <div>
      <div className="grid cols-4 cols-2-mobile" style={{ padding: "0 1.2rem 1rem" }}>
        <div><div className="muted small">Devreden bakiye</div><Money value={st.carriedBalance} /></div>
        <div><div className="muted small">Dönem borç toplamı</div><Money value={st.totalDebit} /></div>
        <div><div className="muted small">Dönem alacak toplamı</div><Money value={st.totalCredit} /></div>
        <div><div className="muted small">Kapanış bakiyesi</div><strong><Money value={st.closingBalance} /></strong></div>
      </div>
      <DataTable rows={st.entries} rowKey={(e) => e.id} empty={<EmptyState title="Bu aralıkta hareket yok" text="Tarih filtresini genişletmeyi deneyin." />}
        columns={[
          { header: "Tarih", cell: (e) => dateTr(e.date) },
          { header: "İşlem", cell: (e) => <><Badge tone={e.entryType.includes("Reversal") ? "bad" : e.entryType === "OpeningBalance" ? "info" : "neutral"}>{L.ledger[e.entryType]}</Badge>{e.isReversed && <> <Badge tone="bad">Ters kayıtla kapatıldı</Badge></>}</> },
          { header: "Referans", cell: (e) => { const l = link(e); return l ? <Link to={l} onClick={(ev) => ev.stopPropagation()}>{e.referenceNo}</Link> : e.referenceNo; } },
          { header: "Açıklama", cell: (e) => <span className="small">{e.description}</span> },
          { header: kind === "customer" ? "Borç (artış)" : "Borç (ödeme)", num: true, cell: (e) => e.debit > 0 ? <Money value={e.debit} /> : "" },
          { header: kind === "customer" ? "Alacak (azalış)" : "Alacak (alış)", num: true, cell: (e) => e.credit > 0 ? <Money value={e.credit} /> : "" },
          { header: "Bakiye", num: true, cell: (e) => <strong><Money value={e.balance} /></strong> },
          { header: "Kaydeden", hideMobile: true, cell: (e) => <span className="small muted" title={dateTimeTr(e.createdAtUtc)}>{e.createdBy}</span> },
        ]} />
      <p className="muted small" style={{ padding: "0 1.2rem 1rem", margin: 0 }}>
        {kind === "customer"
          ? "Bakiye pozitifse müşteri bize borçludur. Kayıtlar silinmez; hata düzeltmeleri ters kayıtla yapılır."
          : "Bakiye pozitifse tedarikçiye borcumuz vardır. Kayıtlar silinmez; hata düzeltmeleri ters kayıtla yapılır."}
      </p>
    </div>
  );
}
