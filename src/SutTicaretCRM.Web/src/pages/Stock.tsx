import { useEffect, useState } from "react";
import { Link, useSearchParams } from "react-router-dom";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "../lib/api";
import type { Paged, Product, StockLevel, StockMovement } from "../lib/types";
import { L } from "../lib/types";
import { Badge, Button, Card, DataTable, EmptyState, ErrorBlock, FormError, LinkButton, Modal, Money, NumberInput, Notice, PageHeader, Pager, Select, Spinner, Tabs, TextInput, useDebounced, useToast } from "../components/ui";
import { ProductPicker } from "../components/pickers";
import { dateTr, money, num, parseDecimal, todayIso } from "../lib/format";

export function StockPage() {
  const [sp, setSp] = useSearchParams();
  const tab = (sp.get("tab") as "levels" | "low" | "moves") ?? "levels";
  const [modal, setModal] = useState(false);
  return (
    <div className="stack">
      <PageHeader title="Stok" subtitle="Basit stok takibi: alış, satış, iade ve sayım düzeltmeleri" actions={<><Button onClick={() => setModal(true)}>Stok düzeltme / açılış stoğu</Button><LinkButton to="/alislar/yeni" variant="secondary">+ Alış gir</LinkButton></>} />
      <Tabs value={tab} onChange={(t) => setSp({ tab: t }, { replace: true })} tabs={[{ id: "levels", label: "Stok durumu" }, { id: "low", label: "Azalanlar" }, { id: "moves", label: "Stok hareketleri" }]} />
      {tab === "moves" ? <Movements /> : <Levels onlyLow={tab === "low"} />}
      {modal && <AdjustModal onClose={() => setModal(false)} />}
    </div>
  );
}

function Levels({ onlyLow }: { onlyLow: boolean }) {
  const [text, setText] = useState("");
  const q = useDebounced(text);
  const { data, isLoading, error, refetch } = useQuery({ queryKey: ["stock-levels", onlyLow, q], queryFn: () => api.get<StockLevel[]>("/stock/levels", { onlyLow, search: q }) });
  const tracked = (data ?? []).filter((x) => x.trackStock);
  return (
    <>
      <div className="filters"><TextInput label="Ara" value={text} onChange={(e) => setText(e.target.value)} placeholder="Ürün adı veya kodu" /></div>
      <Card flush>
        {isLoading ? <Spinner /> : error ? <ErrorBlock error={error} onRetry={refetch} /> : (
          <DataTable rows={data!} rowKey={(r) => r.productId} empty={<EmptyState title={onlyLow ? "Azalan stok yok" : "Ürün yok"} text={onlyLow ? "Tüm ürünler minimum seviyenin üstünde." : "Önce ürün ekleyin."} />} footer={!onlyLow}
            columns={[
              { header: "Ürün", title: true, cell: (r) => <><strong>{r.name}</strong><div className="muted small">{r.code}</div></>, foot: `${tracked.length} stoklu ürün` },
              { header: "Mevcut stok", num: true, cell: (r) => !r.trackStock ? <span className="muted">Takip yok</span> : <span className={r.isLow ? "pos" : ""}>{num(r.quantity)} {r.unit}</span> },
              { header: "Minimum", num: true, cell: (r) => r.trackStock ? `${num(r.minStock)} ${r.unit}` : "—" },
              { header: "Durum", cell: (r) => !r.trackStock ? "—" : r.isLow ? <Badge tone="bad">Azalıyor</Badge> : <Badge tone="good">Yeterli</Badge> },
              { header: "Maliyet değeri", num: true, cell: (r) => r.trackStock ? <Money value={r.stockValueAtCost} /> : "—", foot: <Money value={tracked.reduce((a, b) => a + b.stockValueAtCost, 0)} /> },
            ]} />
        )}
      </Card>
      <p className="muted small">Maliyet değeri = stok × ürün kartındaki alış fiyatı (KDV hariç). Stok takibi kapalı ürünler hesaba katılmaz.</p>
    </>
  );
}

function Movements() {
  const [product, setProduct] = useState<{ id: string; name: string } | null>(null);
  const [from, setFrom] = useState("");
  const [to, setTo] = useState("");
  const [page, setPage] = useState(1);
  const { data, isLoading, error } = useQuery({ queryKey: ["stock-moves", product?.id, from, to, page], queryFn: () => api.get<Paged<StockMovement>>("/stock/movements", { productId: product?.id, from, to, page, pageSize: 30 }) });
  return (
    <>
      <div className="filters">
        <div>{product ? <div className="picker-selected"><strong>{product.name}</strong><Button size="sm" variant="secondary" onClick={() => setProduct(null)}>Temizle</Button></div> : <ProductPicker label="Ürüne göre filtrele" onPick={(p) => { setProduct({ id: p.id, name: p.name }); setPage(1); }} showStock={false} />}</div>
        <TextInput label="Başlangıç" type="date" value={from} onChange={(e) => { setFrom(e.target.value); setPage(1); }} />
        <TextInput label="Bitiş" type="date" value={to} onChange={(e) => { setTo(e.target.value); setPage(1); }} />
      </div>
      <Card flush>
        {isLoading ? <Spinner /> : error ? <ErrorBlock error={error} /> : (
          <>
            <DataTable rows={data!.items} rowKey={(m) => m.id} empty={<EmptyState title="Stok hareketi yok" />}
              columns={[
                { header: "Tarih", cell: (m) => dateTr(m.date) },
                { header: "Ürün", title: true, cell: (m) => <><strong>{m.productName}</strong><div className="muted small">{m.productCode}</div></> },
                { header: "Tür", cell: (m) => <Badge tone={m.quantityDelta > 0 ? "good" : "warn"}>{L.stockMove[m.movementType]}</Badge> },
                { header: "Miktar", num: true, cell: (m) => <span className={m.quantityDelta < 0 ? "pos" : "neg"}>{m.quantityDelta > 0 ? "+" : ""}{num(m.quantityDelta)} {m.unit}</span> },
                { header: "Açıklama", cell: (m) => <span className="small">{m.note ?? ""}{m.sourceType === "Sale" && m.sourceId && <> · <Link to={`/satislar/${m.sourceId}`}>satış</Link></>}</span> },
              ]} />
            <Pager page={data!.page} pageSize={data!.pageSize} total={data!.total} onPage={setPage} />
          </>
        )}
      </Card>
    </>
  );
}

function AdjustModal({ onClose }: { onClose: () => void }) {
  const qc = useQueryClient();
  const toast = useToast();
  const [product, setProduct] = useState<Product | null>(null);
  const [kind, setKind] = useState<"count" | "Opening" | "Return" | "Adjustment">("count");
  const [value, setValue] = useState("");
  const [date, setDate] = useState(todayIso());
  const [note, setNote] = useState("");
  const v = parseDecimal(value);
  const m = useMutation({
    mutationFn: () => api.post("/stock/adjust", {
      productId: product!.id, date, movementType: kind === "count" ? "Adjustment" : kind,
      quantityDelta: kind === "count" ? null : kind === "Adjustment" ? v : v, countedQuantity: kind === "count" ? v : null, note: note || null,
    }),
    onSuccess: () => { qc.invalidateQueries(); toast.success("Stok hareketi kaydedildi."); onClose(); },
  });
  return (
    <Modal title="Stok düzeltme / açılış stoğu" onClose={onClose} footer={<><Button variant="secondary" onClick={onClose}>Vazgeç</Button><Button loading={m.isPending} disabled={!product || v === null} onClick={() => m.mutate()}>Kaydet</Button></>}>
      <div className="stack">
        {product ? <div className="picker-selected"><span><strong>{product.name}</strong> · mevcut: {product.trackStock ? `${num(product.stockQuantity)} ${product.unit}` : "takip yok"}</span><Button size="sm" variant="secondary" onClick={() => setProduct(null)}>Değiştir</Button></div> : <ProductPicker label="Ürün" onPick={setProduct} />}
        <Select label="İşlem" value={kind} onChange={(e) => setKind(e.target.value as any)}>
          <option value="count">Sayım sonucu (mevcut stoğu şuna eşitle)</option>
          <option value="Opening">Açılış stoğu (stok ekle)</option>
          <option value="Return">İade girişi (stok ekle)</option>
          <option value="Adjustment">Elle artır/azalt (+ / −)</option>
        </Select>
        <NumberInput label={kind === "count" ? `Sayılan miktar${product ? ` (${product.unit})` : ""}` : `Miktar${product ? ` (${product.unit})` : ""}`} value={value} onChange={setValue} hint={kind === "Adjustment" ? "Azaltmak için eksi işareti kullanın, ör. -5" : kind === "count" ? "Fark otomatik hesaplanır." : undefined} />
        <TextInput label="Tarih" type="date" value={date} onChange={(e) => setDate(e.target.value)} />
        <TextInput label="Açıklama" value={note} onChange={(e) => setNote(e.target.value)} placeholder="Örn. sayım, fire, ilk stok" />
        {product && !product.trackStock && <Notice tone="warn">Bu ürün için stok takibi kapalı; önce ürün kartından açın.</Notice>}
        <FormError error={m.error} />
      </div>
    </Modal>
  );
}
