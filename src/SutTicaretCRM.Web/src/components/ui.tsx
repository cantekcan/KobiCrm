import { createContext, ReactNode, useCallback, useContext, useEffect, useMemo, useRef, useState } from "react";
import { Link, useNavigate } from "react-router-dom";
import { ApiError } from "../lib/api";
import { dateTr, money, num, parseDecimal } from "../lib/format";

// ───────────── İkonlar (sade, bağımlılıksız) ─────────────
const ic = (d: string) => ({ size = 22 }: { size?: number }) => (
  <svg width={size} height={size} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.9" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true"><path d={d} /></svg>
);
export const Icon = {
  home: ic("M3 11l9-8 9 8M5 10v10h14V10"),
  users: ic("M16 21v-2a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v2M9 11a4 4 0 1 0 0-8 4 4 0 0 0 0 8M22 21v-2a4 4 0 0 0-3-3.9M16 3.1a4 4 0 0 1 0 7.8"),
  box: ic("M21 8l-9-5-9 5v8l9 5 9-5zM3.3 7.5L12 12.5l8.7-5M12 22V12.5"),
  cart: ic("M3 3h2l2.4 12.2a2 2 0 0 0 2 1.6h8.2a2 2 0 0 0 2-1.5L21 8H6M9 21a1 1 0 1 0 0-2 1 1 0 0 0 0 2M18 21a1 1 0 1 0 0-2 1 1 0 0 0 0 2"),
  file: ic("M14 3H7a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h10a2 2 0 0 0 2-2V8zM14 3v5h5M9 13h6M9 17h6"),
  book: ic("M4 19.5A2.5 2.5 0 0 1 6.5 17H20M4 19.5V5a2 2 0 0 1 2-2h14v14H6.5A2.5 2.5 0 0 0 4 19.5"),
  cash: ic("M2 6h20v12H2zM12 15a3 3 0 1 0 0-6 3 3 0 0 0 0 6M6 12h.01M18 12h.01"),
  stock: ic("M3 7l9-4 9 4-9 4zM3 12l9 4 9-4M3 17l9 4 9-4"),
  chart: ic("M4 20V10M10 20V4M16 20v-7M22 20H2"),
  gear: ic("M12 15a3 3 0 1 0 0-6 3 3 0 0 0 0 6M19.4 15a1.7 1.7 0 0 0 .3 1.8l.1.1a2 2 0 1 1-2.8 2.8l-.1-.1a1.7 1.7 0 0 0-1.8-.3 1.7 1.7 0 0 0-1 1.5V21a2 2 0 1 1-4 0v-.1a1.7 1.7 0 0 0-1.1-1.5 1.7 1.7 0 0 0-1.8.3l-.1.1a2 2 0 1 1-2.8-2.8l.1-.1a1.7 1.7 0 0 0 .3-1.8 1.7 1.7 0 0 0-1.5-1H3a2 2 0 1 1 0-4h.1a1.7 1.7 0 0 0 1.5-1.1 1.7 1.7 0 0 0-.3-1.8l-.1-.1a2 2 0 1 1 2.8-2.8l.1.1a1.7 1.7 0 0 0 1.8.3H9a1.7 1.7 0 0 0 1-1.5V3a2 2 0 1 1 4 0v.1a1.7 1.7 0 0 0 1 1.5 1.7 1.7 0 0 0 1.8-.3l.1-.1a2 2 0 1 1 2.8 2.8l-.1.1a1.7 1.7 0 0 0-.3 1.8V9a1.7 1.7 0 0 0 1.5 1H21a2 2 0 1 1 0 4h-.1a1.7 1.7 0 0 0-1.5 1"),
  truck: ic("M1 3h15v13H1zM16 8h4l3 3v5h-7zM5.5 21a2 2 0 1 0 0-4 2 2 0 0 0 0 4M18.5 21a2 2 0 1 0 0-4 2 2 0 0 0 0 4"),
  menu: ic("M3 6h18M3 12h18M3 18h18"),
  plus: ic("M12 5v14M5 12h14"),
  back: ic("M15 18l-6-6 6-6"),
  x: ic("M18 6L6 18M6 6l12 12"),
  out: ic("M9 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h4M16 17l5-5-5-5M21 12H9"),
  print: ic("M6 9V2h12v7M6 18H4a2 2 0 0 1-2-2v-5a2 2 0 0 1 2-2h16a2 2 0 0 1 2 2v5a2 2 0 0 1-2 2h-2M6 14h12v8H6z"),
};

// ───────────── Temel bileşenler ─────────────
export function Button({ variant, size, loading, children, ...rest }:
  { variant?: "secondary" | "danger" | "outline-danger" | "success" | "ghost"; size?: "sm"; loading?: boolean } & React.ButtonHTMLAttributes<HTMLButtonElement>) {
  const cls = ["btn", variant === "outline-danger" ? "danger outline" : variant, size].filter(Boolean).join(" ");
  return <button type="button" {...rest} className={`${cls} ${rest.className ?? ""}`} disabled={rest.disabled || loading}>{loading ? "Bekleyin…" : children}</button>;
}

export function LinkButton({ to, variant, size, children }: { to: string; variant?: "secondary" | "ghost"; size?: "sm"; children: ReactNode }) {
  return <Link to={to} className={["btn", variant, size].filter(Boolean).join(" ")}>{children}</Link>;
}

export function Card({ title, actions, children, flush, className }: { title?: ReactNode; actions?: ReactNode; children: ReactNode; flush?: boolean; className?: string }) {
  return (
    <section className={`card${flush ? " flush" : ""} ${className ?? ""}`}>
      {(title || actions) && <div className="card-title">{title && <h2>{title}</h2>}{actions && <div className="row">{actions}</div>}</div>}
      {children}
    </section>
  );
}

export function PageHeader({ title, subtitle, actions, back }: { title: ReactNode; subtitle?: ReactNode; actions?: ReactNode; back?: { to: string; label: string } }) {
  return (
    <div className="page-header">
      <div>
        {back && <Link to={back.to} className="back-link"><Icon.back size={16} /> {back.label}</Link>}
        <h1>{title}</h1>
        {subtitle && <div className="muted">{subtitle}</div>}
      </div>
      {actions && <div className="actions">{actions}</div>}
    </div>
  );
}

export function Field({ label, error, hint, required, children, className }: { label: string; error?: string; hint?: string; required?: boolean; children: ReactNode; className?: string }) {
  return (
    <label className={`field ${className ?? ""}`}>
      <span>{label}{required && <span className="req"> *</span>}</span>
      {children}
      {hint && !error && <span className="hint">{hint}</span>}
      {error && <span className="err" role="alert">{error}</span>}
    </label>
  );
}

export function TextInput({ label, error, hint, required, className, ...rest }: { label: string; error?: string; hint?: string; required?: boolean } & React.InputHTMLAttributes<HTMLInputElement>) {
  return <Field label={label} error={error} hint={hint} required={required} className={className}><input {...rest} aria-invalid={!!error} /></Field>;
}

/** Sayısal giriş: metin olarak tutulur ("1.234,50" yazılabilir), geçersizse uyarır. */
export function NumberInput({ label, value, onChange, error, hint, required, min, className, disabled, placeholder }:
  { label: string; value: string; onChange: (v: string) => void; error?: string; hint?: string; required?: boolean; min?: number; className?: string; disabled?: boolean; placeholder?: string }) {
  const parsed = parseDecimal(value);
  const localErr = error ?? (value.trim() !== "" && parsed === null ? "Geçerli bir sayı girin." : min !== undefined && parsed !== null && parsed < min ? `En az ${num(min)} olmalı.` : undefined);
  return (
    <Field label={label} error={localErr} hint={hint} required={required} className={className}>
      <input inputMode="decimal" value={value} onChange={(e) => onChange(e.target.value)} aria-invalid={!!localErr} disabled={disabled} placeholder={placeholder} />
    </Field>
  );
}

export function Select({ label, error, hint, required, children, className, ...rest }: { label: string; error?: string; hint?: string; required?: boolean; className?: string } & React.SelectHTMLAttributes<HTMLSelectElement>) {
  return <Field label={label} error={error} hint={hint} required={required} className={className}><select {...rest} aria-invalid={!!error}>{children}</select></Field>;
}

export function TextArea({ label, error, hint, className, ...rest }: { label: string; error?: string; hint?: string; className?: string } & React.TextareaHTMLAttributes<HTMLTextAreaElement>) {
  return <Field label={label} error={error} hint={hint} className={className}><textarea {...rest} /></Field>;
}

export function Check({ checked, onChange, children }: { checked: boolean; onChange: (v: boolean) => void; children: ReactNode }) {
  return <label className="check"><input type="checkbox" checked={checked} onChange={(e) => onChange(e.target.checked)} /><span>{children}</span></label>;
}

export function Badge({ tone = "neutral", children }: { tone?: "good" | "warn" | "bad" | "info" | "neutral"; children: ReactNode }) {
  return <span className={`badge ${tone}`}>{children}</span>;
}

export function Notice({ tone = "info", children, title }: { tone?: "info" | "warn" | "error" | "good"; children: ReactNode; title?: string }) {
  return <div className={`notice ${tone}`} role={tone === "error" ? "alert" : undefined}>{title && <strong>{title}</strong>}{title && <br />}{children}</div>;
}

export function Spinner() { return <div className="spinner" role="status" aria-label="Yükleniyor" />; }

export function EmptyState({ title, text, action }: { title: string; text?: string; action?: ReactNode }) {
  return <div className="empty"><h3>{title}</h3>{text && <p>{text}</p>}{action}</div>;
}

export function ErrorBlock({ error, onRetry }: { error: unknown; onRetry?: () => void }) {
  const msg = error instanceof ApiError ? error.message : "Beklenmeyen bir hata oluştu.";
  return <Notice tone="error" title="Yüklenemedi">{msg}{onRetry && <div style={{ marginTop: 8 }}><Button size="sm" variant="secondary" onClick={onRetry}>Tekrar dene</Button></div>}</Notice>;
}

/** Form gönderiminden dönen hatayı gösterir (alan hataları ayrıca alanlarda gösterilir). */
export function FormError({ error }: { error: unknown }) {
  if (!error) return null;
  if (error instanceof ApiError) {
    const fields = Object.entries(error.fieldErrors);
    return (
      <Notice tone="error" title={error.message}>
        {fields.length > 0 && <ul>{fields.map(([k, v]) => <li key={k}>{v.join(" ")}</li>)}</ul>}
      </Notice>
    );
  }
  return <Notice tone="error">Beklenmeyen bir hata oluştu.</Notice>;
}

export const Money = ({ value, className }: { value: number | null | undefined; className?: string }) => <span className={`mono ${className ?? ""}`}>{money(value)}</span>;

/** Bakiye: pozitif = müşteri bize borçlu (kırmızı), negatif = müşterinin avansı (yeşil). */
export function Balance({ value }: { value: number }) {
  if (value === 0) return <span className="mono">{money(0)}</span>;
  return <span className={`mono ${value > 0 ? "pos" : "neg"}`}>{money(Math.abs(value))}{value < 0 ? " (alacaklı)" : ""}</span>;
}

// ───────────── Rozetler ─────────────
const saleTone = { Draft: "neutral", Approved: "info", PartiallyPaid: "warn", Paid: "good", Cancelled: "bad" } as const;
export const SaleBadge = ({ s, label }: { s: keyof typeof saleTone; label: string }) => <Badge tone={saleTone[s]}>{label}</Badge>;

// ───────────── Modal ─────────────
export function Modal({ title, onClose, children, footer, wide }: { title: string; onClose: () => void; children: ReactNode; footer?: ReactNode; wide?: boolean }) {
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => { if (e.key === "Escape") onClose(); };
    document.addEventListener("keydown", onKey);
    return () => document.removeEventListener("keydown", onKey);
  }, [onClose]);
  return (
    <div className="modal-backdrop" onMouseDown={(e) => { if (e.target === e.currentTarget) onClose(); }}>
      <div className={`modal${wide ? " wide" : ""}`} role="dialog" aria-modal="true" aria-label={title}>
        <div className="modal-head"><h2>{title}</h2><Button variant="ghost" size="sm" onClick={onClose} aria-label="Kapat"><Icon.x size={20} /></Button></div>
        <div className="modal-body">{children}</div>
        {footer && <div className="modal-foot">{footer}</div>}
      </div>
    </div>
  );
}

type ConfirmOptions = { title: string; message: ReactNode; confirmLabel?: string; danger?: boolean; reasonLabel?: string; requireReason?: boolean };
type ConfirmFn = (o: ConfirmOptions) => Promise<{ ok: boolean; reason: string }>;
const ConfirmCtx = createContext<ConfirmFn>(async () => ({ ok: false, reason: "" }));

/** Onay penceresi: Promise döner. Finansal işlemlerde sonucu açıkça anlatan metin verilmelidir. */
export function useConfirm() { return useContext(ConfirmCtx); }

export function Providers({ children }: { children: ReactNode }) {
  const [toasts, setToasts] = useState<{ id: number; kind: "success" | "error" | "info"; text: string }[]>([]);
  const nextId = useRef(1);
  const push = useCallback((kind: "success" | "error" | "info", text: string) => {
    const id = nextId.current++;
    setToasts((t) => [...t, { id, kind, text }]);
    setTimeout(() => setToasts((t) => t.filter((x) => x.id !== id)), kind === "error" ? 9000 : 5000);
  }, []);
  const toast = useMemo(() => ({ success: (t: string) => push("success", t), error: (t: string) => push("error", t), info: (t: string) => push("info", t) }), [push]);

  const [dlg, setDlg] = useState<(ConfirmOptions & { resolve: (r: { ok: boolean; reason: string }) => void }) | null>(null);
  const [reason, setReason] = useState("");
  const confirm = useCallback<ConfirmFn>((o) => new Promise((resolve) => { setReason(""); setDlg({ ...o, resolve }); }), []);
  const close = (ok: boolean) => { dlg?.resolve({ ok, reason: reason.trim() }); setDlg(null); };

  return (
    <ToastCtx.Provider value={toast}>
      <ConfirmCtx.Provider value={confirm}>
        {children}
        <div className="toasts" aria-live="polite">
          {toasts.map((t) => <div key={t.id} className={`toast ${t.kind}`}><span>{t.text}</span><button aria-label="Kapat" onClick={() => setToasts((x) => x.filter((y) => y.id !== t.id))}>×</button></div>)}
        </div>
        {dlg && (
          <Modal title={dlg.title} onClose={() => close(false)} footer={<>
            <Button variant="secondary" onClick={() => close(false)}>Vazgeç</Button>
            <Button variant={dlg.danger ? "danger" : undefined} disabled={!!dlg.reasonLabel && dlg.requireReason !== false && reason.trim() === ""} onClick={() => close(true)}>{dlg.confirmLabel ?? "Onayla"}</Button>
          </>}>
            <div className="stack-sm">
              <div>{dlg.message}</div>
              {dlg.reasonLabel && <TextArea label={dlg.reasonLabel} value={reason} onChange={(e) => setReason(e.target.value)} autoFocus />}
            </div>
          </Modal>
        )}
      </ConfirmCtx.Provider>
    </ToastCtx.Provider>
  );
}

const ToastCtx = createContext({ success: (_: string) => {}, error: (_: string) => {}, info: (_: string) => {} });
export const useToast = () => useContext(ToastCtx);

// ───────────── Tablo ─────────────
export interface Column<T> { header: string; cell: (row: T) => ReactNode; num?: boolean; title?: boolean; hideMobile?: boolean; foot?: ReactNode; /** Yalnızca geniş ekranda sütun olarak gösterilir (dar masaüstünde gizlenir; mobilde kartta görünür). */ wide?: boolean }

export function DataTable<T>({ rows, columns, rowKey, onRowClick, rowHref, empty, footer }: {
  rows: T[]; columns: Column<T>[]; rowKey: (r: T) => string; onRowClick?: (r: T) => void; rowHref?: (r: T) => string; empty?: ReactNode; footer?: boolean;
}) {
  const nav = useNavigate();
  if (rows.length === 0) return <>{empty ?? <EmptyState title="Kayıt yok" />}</>;
  const go = (r: T) => (onRowClick ? onRowClick(r) : rowHref ? nav(rowHref(r)) : undefined);
  const clickable = !!(onRowClick || rowHref);
  return (
    <div className="table-wrap">
      <table className="table">
        <thead><tr>{columns.map((c, i) => <th key={i} className={`${c.num ? "num" : ""} ${c.wide ? "col-wide" : ""}`}>{c.header}</th>)}</tr></thead>
        <tbody>
          {rows.map((r) => (
            <tr key={rowKey(r)} className={clickable ? "clickable" : ""} onClick={() => go(r)}>
              {columns.map((c, i) => <td key={i} data-label={c.title ? "" : c.header} className={`${c.num ? "num" : ""} ${c.title ? "cell-title" : ""} ${c.wide ? "col-wide" : ""}`}>{c.cell(r)}</td>)}
            </tr>
          ))}
        </tbody>
        {footer && <tfoot><tr>{columns.map((c, i) => <td key={i} className={`${c.num ? "num" : ""} ${c.wide ? "col-wide" : ""}`}>{c.foot ?? ""}</td>)}</tr></tfoot>}
      </table>
    </div>
  );
}

export function Pager({ page, pageSize, total, onPage }: { page: number; pageSize: number; total: number; onPage: (p: number) => void }) {
  const pages = Math.max(1, Math.ceil(total / pageSize));
  if (total <= pageSize) return <div className="pager muted small">{total} kayıt</div>;
  return (
    <div className="pager">
      <span className="muted small">{total} kayıt · Sayfa {page}/{pages}</span>
      <div className="row">
        <Button size="sm" variant="secondary" disabled={page <= 1} onClick={() => onPage(page - 1)}>‹ Önceki</Button>
        <Button size="sm" variant="secondary" disabled={page >= pages} onClick={() => onPage(page + 1)}>Sonraki ›</Button>
      </div>
    </div>
  );
}

export function Tabs<T extends string>({ tabs, value, onChange }: { tabs: { id: T; label: string }[]; value: T; onChange: (t: T) => void }) {
  return <div className="tabs" role="tablist">{tabs.map((t) => <button key={t.id} role="tab" aria-selected={t.id === value} className={t.id === value ? "on" : ""} onClick={() => onChange(t.id)}>{t.label}</button>)}</div>;
}

export function KV({ items }: { items: [string, ReactNode][] }) {
  return <dl className="kv" style={{ margin: 0 }}>{items.map(([k, v]) => <div key={k} style={{ display: "contents" }}><dt>{k}</dt><dd>{v ?? "—"}</dd></div>)}</dl>;
}

export { dateTr };

/** Arama kutusu: yazmayı bıraktıktan 350 ms sonra değeri bildirir. */
export function useDebounced<T>(value: T, ms = 350): T {
  const [v, setV] = useState(value);
  useEffect(() => { const t = setTimeout(() => setV(value), ms); return () => clearTimeout(t); }, [value, ms]);
  return v;
}
