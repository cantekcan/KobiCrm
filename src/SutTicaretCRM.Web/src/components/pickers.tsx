import { useEffect, useRef, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { api } from "../lib/api";
import type { CustomerListItem, Paged, Product } from "../lib/types";
import { Field, useDebounced, Balance, Button } from "./ui";
import { num } from "../lib/format";

function useOutsideClose(ref: React.RefObject<HTMLElement>, onClose: () => void) {
  useEffect(() => {
    const h = (e: MouseEvent | TouchEvent) => { if (ref.current && !ref.current.contains(e.target as Node)) onClose(); };
    document.addEventListener("mousedown", h);
    document.addEventListener("touchstart", h);
    return () => { document.removeEventListener("mousedown", h); document.removeEventListener("touchstart", h); };
  }, [ref, onClose]);
}

/** Hızlı müşteri seçimi: yazdıkça arar, bakiyeyi gösterir. */
export function CustomerPicker({ value, onChange, label = "Müşteri", error, required, onlyActive = true, autoFocus }:
  { value: { id: string; name: string } | null; onChange: (c: CustomerListItem | null) => void; label?: string; error?: string; required?: boolean; onlyActive?: boolean; autoFocus?: boolean }) {
  const [text, setText] = useState("");
  const [open, setOpen] = useState(false);
  const q = useDebounced(text, 250);
  const ref = useRef<HTMLDivElement>(null);
  useOutsideClose(ref, () => setOpen(false));
  const { data, isFetching } = useQuery({
    queryKey: ["picker-customers", q, onlyActive],
    queryFn: () => api.get<Paged<CustomerListItem>>("/customers", { search: q, pageSize: 20, active: onlyActive ? true : undefined }),
    enabled: open,
    staleTime: 10_000,
  });

  if (value) {
    return (
      <Field label={label} required={required} error={error}>
        <div className="picker-selected"><strong>{value.name}</strong><Button size="sm" variant="secondary" onClick={() => { onChange(null); setText(""); setOpen(true); }}>Değiştir</Button></div>
      </Field>
    );
  }
  return (
    <div className="picker" ref={ref}>
      <Field label={label} required={required} error={error}>
        <input value={text} autoFocus={autoFocus} placeholder="Müşteri adı, telefon veya vergi no ile arayın…" onFocus={() => setOpen(true)} onChange={(e) => { setText(e.target.value); setOpen(true); }} />
      </Field>
      {open && (
        <div className="picker-list" role="listbox">
          {isFetching && !data && <div className="picker-item muted">Aranıyor…</div>}
          {data?.items.map((c) => (
            <div key={c.id} role="option" className="picker-item" onMouseDown={(e) => e.preventDefault()} onClick={() => { onChange(c); setOpen(false); }}>
              <span><strong>{c.name}</strong>{c.phone && <span className="muted small"> · {c.phone}</span>}</span>
              <Balance value={c.balance} />
            </div>
          ))}
          {data && data.items.length === 0 && <div className="picker-item muted">Müşteri bulunamadı.</div>}
        </div>
      )}
    </div>
  );
}

export function ProductPicker({ onPick, label = "Ürün ekle", showStock = true }: { onPick: (p: Product) => void; label?: string; showStock?: boolean }) {
  const [text, setText] = useState("");
  const [open, setOpen] = useState(false);
  const q = useDebounced(text, 200);
  const ref = useRef<HTMLDivElement>(null);
  useOutsideClose(ref, () => setOpen(false));
  const { data } = useQuery({
    queryKey: ["picker-products", q],
    queryFn: () => api.get<Paged<Product>>("/products", { search: q, active: true, pageSize: 20 }),
    enabled: open,
    staleTime: 10_000,
  });
  return (
    <div className="picker" ref={ref}>
      <Field label={label}>
        <input value={text} placeholder="Ürün adı veya kodu yazın…" onFocus={() => setOpen(true)} onChange={(e) => { setText(e.target.value); setOpen(true); }} />
      </Field>
      {open && (
        <div className="picker-list" role="listbox">
          {data?.items.map((p) => (
            <div key={p.id} role="option" className="picker-item" onMouseDown={(e) => e.preventDefault()} onClick={() => { onPick(p); setText(""); setOpen(false); }}>
              <span><strong>{p.name}</strong> <span className="muted small">{p.code}</span></span>
              <span className="small muted">{showStock && p.trackStock ? `Stok: ${num(p.stockQuantity)} ${p.unit}` : p.unit}</span>
            </div>
          ))}
          {data && data.items.length === 0 && <div className="picker-item muted">Ürün bulunamadı.</div>}
        </div>
      )}
    </div>
  );
}

export function SupplierSelect({ value, onChange, error }: { value: string; onChange: (id: string) => void; error?: string }) {
  const { data } = useQuery({ queryKey: ["suppliers-all"], queryFn: () => api.get<{ id: string; name: string }[]>("/suppliers", { active: true }) });
  return (
    <Field label="Tedarikçi" required error={error}>
      <select value={value} onChange={(e) => onChange(e.target.value)}>
        <option value="">Seçin…</option>
        {data?.map((s) => <option key={s.id} value={s.id}>{s.name}</option>)}
      </select>
    </Field>
  );
}
