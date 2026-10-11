import { FormEvent, useEffect, useState } from "react";
import { useNavigate, useParams } from "react-router-dom";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api, ApiError } from "../lib/api";
import { useUrlFilters } from "../lib/hooks";
import type { Category, CustomerPrice, Paged, Product, Settings } from "../lib/types";
import { Badge, Button, Card, Check, DataTable, EmptyState, ErrorBlock, FormError, LinkButton, Money, NumberInput, Notice, PageHeader, Pager, Select, Spinner, TextArea, TextInput, useConfirm, useDebounced, useToast } from "../components/ui";
import { CustomerPicker } from "../components/pickers";
import { num, parseDecimal } from "../lib/format";

export function ProductsPage() {
  const [f, set] = useUrlFilters({ search: "", active: "true", lowStock: "", page: "1" });
  const [text, setText] = useState(f.search);
  const dq = useDebounced(text);
  useEffect(() => { if (dq !== f.search) set({ search: dq }); }, [dq]); // eslint-disable-line react-hooks/exhaustive-deps
  const { data, isLoading, error, refetch } = useQuery({
    queryKey: ["products", f],
    queryFn: () => api.get<Paged<Product>>("/products", { search: f.search, active: f.active === "all" ? undefined : f.active === "true", lowStock: f.lowStock ? true : undefined, page: f.page, pageSize: 30 }),
  });
  return (
    <div className="stack">
      <PageHeader title="Ürünler" actions={<LinkButton to="/urunler/yeni">+ Yeni ürün</LinkButton>} />
      <div className="filters">
        <TextInput label="Ara" placeholder="Ürün adı veya kodu" value={text} onChange={(e) => setText(e.target.value)} />
        <Select label="Durum" value={f.active} onChange={(e) => set({ active: e.target.value })}><option value="true">Aktif</option><option value="false">Pasif</option><option value="all">Hepsi</option></Select>
        <Select label="Stok" value={f.lowStock} onChange={(e) => set({ lowStock: e.target.value })}><option value="">Hepsi</option><option value="1">Yalnızca azalanlar</option></Select>
      </div>
      <Card flush>
        {isLoading ? <Spinner /> : error ? <ErrorBlock error={error} onRetry={refetch} /> : (
          <>
            <DataTable rows={data!.items} rowKey={(r) => r.id} rowHref={(r) => `/urunler/${r.id}`}
              empty={<EmptyState title="Ürün bulunamadı" text="Satış yapabilmek için önce ürünlerinizi ekleyin." action={<LinkButton to="/urunler/yeni">+ Yeni ürün</LinkButton>} />}
              columns={[
                { header: "Ürün", title: true, cell: (r) => <><strong>{r.name}</strong> {!r.isActive && <Badge>Pasif</Badge>}<div className="muted small">{r.code}{r.categoryName ? ` · ${r.categoryName}` : ""}</div></> },
                { header: "Birim", cell: (r) => r.unit },
                { header: "Satış fiyatı (KDV hariç)", num: true, cell: (r) => <Money value={r.salePrice} /> },
                { header: "KDV", num: true, cell: (r) => `%${num(r.vatRate)}` },
                { header: "Stok", num: true, cell: (r) => !r.trackStock ? <span className="muted">Takip yok</span> : <span className={r.stockQuantity <= r.minStock ? "pos" : ""}>{num(r.stockQuantity)} {r.unit}</span> },
              ]} />
            <Pager page={data!.page} pageSize={data!.pageSize} total={data!.total} onPage={(p) => set({ page: String(p) }, false)} />
          </>
        )}
      </Card>
    </div>
  );
}

interface PF { code: string; name: string; categoryId: string; unit: string; purchasePrice: string; salePrice: string; vatRate: string; trackStock: boolean; minStock: string; description: string; isActive: boolean }

export function ProductFormPage() {
  const { id } = useParams();
  const editing = !!id;
  const nav = useNavigate();
  const qc = useQueryClient();
  const toast = useToast();
  const [f, setF] = useState<PF>({ code: "", name: "", categoryId: "", unit: "Adet", purchasePrice: "0", salePrice: "0", vatRate: "", trackStock: true, minStock: "0", description: "", isActive: true });
  const { data: settings } = useQuery({ queryKey: ["settings"], queryFn: () => api.get<Settings>("/settings") });
  const { data: cats } = useQuery({ queryKey: ["categories"], queryFn: () => api.get<Category[]>("/products/categories") });
  const { data, isLoading, error } = useQuery({ queryKey: ["product", id], queryFn: () => api.get<Product>(`/products/${id}`), enabled: editing });

  useEffect(() => {
    if (data) setF({ code: data.code, name: data.name, categoryId: data.categoryId ?? "", unit: data.unit, purchasePrice: String(data.purchasePrice).replace(".", ","), salePrice: String(data.salePrice).replace(".", ","), vatRate: String(data.vatRate), trackStock: data.trackStock, minStock: String(data.minStock), description: data.description ?? "", isActive: data.isActive });
  }, [data]);
  useEffect(() => { if (!editing && settings && f.vatRate === "") setF((x) => ({ ...x, vatRate: String(settings.vatRates.includes(10) ? 10 : settings.vatRates[0]) })); }, [settings, editing]); // eslint-disable-line

  const up = <K extends keyof PF>(k: K, v: PF[K]) => setF((x) => ({ ...x, [k]: v }));
  const save = useMutation({
    mutationFn: () => {
      const body = { code: f.code, name: f.name, categoryId: f.categoryId || null, unit: f.unit, purchasePrice: parseDecimal(f.purchasePrice) ?? 0, salePrice: parseDecimal(f.salePrice) ?? 0, vatRate: parseDecimal(f.vatRate) ?? 0, trackStock: f.trackStock, minStock: parseDecimal(f.minStock) ?? 0, description: f.description || null, isActive: f.isActive };
      return editing ? api.put<Product>(`/products/${id}`, body) : api.post<Product>("/products", body);
    },
    onSuccess: (p) => { qc.invalidateQueries(); toast.success(editing ? "Ürün güncellendi." : "Ürün kaydedildi."); nav(editing ? "/urunler" : `/urunler/${p.id}`); },
  });
  const err = save.error instanceof ApiError ? save.error : null;
  const [newCat, setNewCat] = useState("");
  const addCat = useMutation({ mutationFn: () => api.post<Category>("/products/categories", { name: newCat }), onSuccess: (c) => { qc.invalidateQueries({ queryKey: ["categories"] }); up("categoryId", c.id); setNewCat(""); } });

  if (editing && isLoading) return <Spinner />;
  if (editing && error) return <ErrorBlock error={error} />;

  return (
    <div className="stack">
      <form className="stack" onSubmit={(e: FormEvent) => { e.preventDefault(); save.mutate(); }}>
        <PageHeader title={editing ? "Ürünü düzenle" : "Yeni ürün"} back={{ to: "/urunler", label: "Ürünler" }} />
        <Card title="Ürün bilgileri">
          <div className="form-grid">
            <TextInput label="Ürün kodu" required value={f.code} onChange={(e) => up("code", e.target.value)} error={err?.field("code")} />
            <TextInput label="Ürün adı" required value={f.name} onChange={(e) => up("name", e.target.value)} error={err?.field("name")} autoFocus={!editing} />
            <Select label="Birim" value={f.unit} onChange={(e) => up("unit", e.target.value)} error={err?.field("unit")} hint="Birimler arasında otomatik dönüşüm yapılmaz (1 koli ≠ kaç kg gibi). Her ürün tek birimle takip edilir.">
              {(settings?.units ?? ["Adet", "Koli", "Kg", "Gram", "Litre"]).map((u) => <option key={u}>{u}</option>)}
            </Select>
            <Select label="KDV oranı" value={f.vatRate} onChange={(e) => up("vatRate", e.target.value)} error={err?.field("vatRate")} hint="Ayarlar'da tanımlı oranlardan seçilir. Güncel oranı muhasebecinizle doğrulayın.">
              {(settings?.vatRates ?? [0, 1, 10, 20]).map((r) => <option key={r} value={r}>%{r}</option>)}
            </Select>
            <NumberInput label="Alış fiyatı (TL, KDV hariç)" value={f.purchasePrice} onChange={(v) => up("purchasePrice", v)} min={0} />
            <NumberInput label="Varsayılan satış fiyatı (TL, KDV hariç)" value={f.salePrice} onChange={(v) => up("salePrice", v)} min={0} hint="Müşteriye özel fiyat yoksa bu kullanılır." />
            <div>
              <Select label="Kategori" value={f.categoryId} onChange={(e) => up("categoryId", e.target.value)}>
                <option value="">Kategorisiz</option>{cats?.map((c) => <option key={c.id} value={c.id}>{c.name}</option>)}
              </Select>
              <div className="row" style={{ marginTop: ".4rem" }}>
                <input style={{ flex: 1, minHeight: 38 }} placeholder="Yeni kategori adı" value={newCat} onChange={(e) => setNewCat(e.target.value)} />
                <Button size="sm" variant="secondary" disabled={!newCat.trim()} loading={addCat.isPending} onClick={() => addCat.mutate()}>Ekle</Button>
              </div>
            </div>
            <div className="stack-sm">
              <Check checked={f.trackStock} onChange={(v) => up("trackStock", v)}>Stok takibi yap</Check>
              {f.trackStock && <NumberInput label="Minimum stok (uyarı seviyesi)" value={f.minStock} onChange={(v) => up("minStock", v)} min={0} />}
              {editing && data && data.trackStock && <div className="muted">Mevcut stok: <strong>{num(data.stockQuantity)} {data.unit}</strong> (stok hareketleri için Stok sayfası)</div>}
            </div>
            <TextArea className="span-2" label="Açıklama" value={f.description} onChange={(e) => up("description", e.target.value)} />
            <div className="span-2"><Check checked={f.isActive} onChange={(v) => up("isActive", v)}>Aktif ürün</Check></div>
          </div>
        </Card>
        <FormError error={save.error} />
        <div className="form-actions"><Button type="submit" loading={save.isPending} disabled={!f.code.trim() || !f.name.trim()}>Kaydet</Button><Button variant="secondary" onClick={() => nav(-1)}>Vazgeç</Button></div>
      </form>
      {editing && data && <CustomerPrices productId={data.id} unit={data.unit} defaultPrice={data.salePrice} />}
    </div>
  );
}

function CustomerPrices({ productId, unit, defaultPrice }: { productId: string; unit: string; defaultPrice: number }) {
  const qc = useQueryClient();
  const toast = useToast();
  const confirm = useConfirm();
  const { data } = useQuery({ queryKey: ["product-prices", productId], queryFn: () => api.get<CustomerPrice[]>(`/products/${productId}/customer-prices`) });
  const [cust, setCust] = useState<{ id: string; name: string } | null>(null);
  const [price, setPrice] = useState("");
  const p = parseDecimal(price);
  const add = useMutation({
    mutationFn: () => api.put(`/customers/${cust!.id}/prices/${productId}`, { price: p }),
    onSuccess: () => { qc.invalidateQueries({ queryKey: ["product-prices", productId] }); qc.invalidateQueries({ queryKey: ["customer-prices"] }); toast.success("Müşteriye özel fiyat kaydedildi."); setCust(null); setPrice(""); },
  });
  const del = useMutation({ mutationFn: (cid: string) => api.del(`/customers/${cid}/prices/${productId}`), onSuccess: () => qc.invalidateQueries({ queryKey: ["product-prices", productId] }) });
  return (
    <Card title="Müşteriye özel fiyatlar">
      <Notice tone="info">Burada tanımlanan fiyat, o müşteriye satış yaparken otomatik gelir (satış sırasında elle değiştirilebilir). Fiyat değişikliği geçmiş satışları etkilemez. Varsayılan fiyat: <strong>{defaultPrice.toLocaleString("tr-TR")} TL / {unit}</strong></Notice>
      <div className="form-grid" style={{ margin: ".9rem 0" }}>
        <CustomerPicker value={cust} onChange={(c) => setCust(c ? { id: c.id, name: c.name } : null)} label="Müşteri" />
        <NumberInput label={`Özel fiyat (TL / ${unit}, KDV hariç)`} value={price} onChange={setPrice} min={0} />
      </div>
      <Button disabled={!cust || p === null || p < 0} loading={add.isPending} onClick={() => add.mutate()}>Fiyatı kaydet</Button>
      <FormError error={add.error} />
      <div style={{ marginTop: "1rem" }}>
        <DataTable rows={data ?? []} rowKey={(r) => r.id} empty={<EmptyState title="Özel fiyat tanımlı değil" />}
          columns={[
            { header: "Müşteri", title: true, cell: (r) => <strong>{r.customerName}</strong> },
            { header: "Özel fiyat", num: true, cell: (r) => <Money value={r.price} /> },
            { header: "Varsayılana göre", num: true, cell: (r) => { const d = r.price - r.defaultPrice; return d === 0 ? "aynı" : <span className={d < 0 ? "neg" : "pos"}>{d > 0 ? "+" : ""}{d.toLocaleString("tr-TR")} TL</span>; } },
            { header: "", cell: (r) => <Button size="sm" variant="outline-danger" onClick={async () => { if ((await confirm({ title: "Özel fiyatı kaldır", message: `${r.customerName} için özel fiyat kaldırılacak.`, danger: true, confirmLabel: "Kaldır" })).ok) del.mutate(r.customerId); }}>Kaldır</Button> },
          ]} />
      </div>
    </Card>
  );
}
