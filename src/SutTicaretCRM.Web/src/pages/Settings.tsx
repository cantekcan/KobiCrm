import { useEffect, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "../lib/api";
import type { AuditLog, Paged, ProviderInfo, Settings } from "../lib/types";
import { Badge, Button, Card, Check, DataTable, ErrorBlock, FormError, KV, NumberInput, Notice, PageHeader, Pager, Select, Spinner, Tabs, TextInput, useToast } from "../components/ui";
import { dateTimeTr, parseDecimal } from "../lib/format";

export function SettingsPage() {
  const [tab, setTab] = useState<"company" | "defs" | "invoice" | "security" | "data" | "audit">("company");
  return (
    <div className="stack">
      <PageHeader title="Ayarlar ve veri yönetimi" />
      <Tabs value={tab} onChange={setTab} tabs={[{ id: "company", label: "Firma" }, { id: "defs", label: "KDV · Birim · Stok" }, { id: "invoice", label: "Fatura yöntemi" }, { id: "security", label: "Güvenlik" }, { id: "data", label: "Yedekleme" }, { id: "audit", label: "İşlem geçmişi" }]} />
      {tab === "security" ? <Security /> : tab === "data" ? <Backup /> : tab === "audit" ? <Audit /> : <Config tab={tab} />}
    </div>
  );
}

function Config({ tab }: { tab: "company" | "defs" | "invoice" }) {
  const qc = useQueryClient();
  const toast = useToast();
  const { data, isLoading, error, refetch } = useQuery({ queryKey: ["settings"], queryFn: () => api.get<Settings>("/settings") });
  const { data: provider } = useQuery({ queryKey: ["invoice-provider"], queryFn: () => api.get<ProviderInfo>("/invoices/provider") });
  const [s, setS] = useState<Settings | null>(null);
  const [vat, setVat] = useState("");
  const [units, setUnits] = useState("");
  useEffect(() => { if (data) { setS(data); setVat(data.vatRates.join(", ")); setUnits(data.units.join(", ")); } }, [data]);
  const save = useMutation({
    mutationFn: () => api.put<Settings>("/settings", {
      company: s!.company, vatRates: vat.split(/[,;\s]+/).filter(Boolean).map((x) => parseDecimal(x)).filter((x): x is number => x !== null),
      units: units.split(",").map((x) => x.trim()).filter(Boolean), allowNegativeStock: s!.allowNegativeStock, invoiceProvider: s!.invoiceProvider,
    }),
    onSuccess: () => { qc.invalidateQueries(); toast.success("Ayarlar kaydedildi."); },
  });
  if (isLoading || !s) return <Spinner />;
  if (error) return <ErrorBlock error={error} onRetry={refetch} />;
  const c = s.company;
  const setC = (k: keyof Settings["company"], v: string) => setS({ ...s, company: { ...c, [k]: v } });
  return (
    <div className="stack">
      {tab === "company" && <Card title="Firma bilgileri"><p className="muted">Fatura önizlemelerinde satıcı olarak görünür. Bu bilgiler üçüncü taraflara gönderilmez.</p>
        <div className="form-grid">
          <TextInput className="span-2" label="Firma / işletme adı" value={c.name} onChange={(e) => setC("name", e.target.value)} />
          <TextInput label="Vergi dairesi" value={c.taxOffice ?? ""} onChange={(e) => setC("taxOffice", e.target.value)} />
          <TextInput label="Vergi / T.C. kimlik no" inputMode="numeric" value={c.taxNumber ?? ""} onChange={(e) => setC("taxNumber", e.target.value.replace(/\D/g, ""))} />
          <TextInput className="span-2" label="Adres" value={c.address ?? ""} onChange={(e) => setC("address", e.target.value)} />
          <TextInput label="Telefon" value={c.phone ?? ""} onChange={(e) => setC("phone", e.target.value)} />
          <TextInput label="E-posta" value={c.email ?? ""} onChange={(e) => setC("email", e.target.value)} />
        </div></Card>}
      {tab === "defs" && <Card title="KDV oranları, birimler ve stok">
        <Notice tone="warn">KDV oranları <strong>sabit değildir</strong>; ürün ve satış kalemlerinde yalnızca burada tanımlı oranlar seçilebilir. Uygulama yürürlükteki vergi mevzuatını kendiliğinden bilmez: <strong>oranları muhasebecinizle doğrulayın</strong> ve değişiklik olduğunda burayı güncelleyin.</Notice>
        <div className="form-grid" style={{ marginTop: "1rem" }}>
          <TextInput label="İzin verilen KDV oranları (%)" value={vat} onChange={(e) => setVat(e.target.value)} hint="Virgülle ayırın. Örn: 0, 1, 10, 20" />
          <TextInput label="Birimler" value={units} onChange={(e) => setUnits(e.target.value)} hint="Virgülle ayırın. Birimler arası otomatik dönüşüm yoktur." />
          <div className="span-2"><Check checked={s.allowNegativeStock} onChange={(v) => setS({ ...s, allowNegativeStock: v })}>Stok yetersizken de satışa izin ver (uyarı gösterilir)</Check></div>
        </div></Card>}
      {tab === "invoice" && <Card title="Fatura yöntemi">
        <Notice tone="info">Resmî fatura yalnızca <strong>GİB portalı / LUCA</strong> veya doğrulanmış bir sağlayıcı üzerinden düzenlenir. Varsayılan ve şu an doğrulanmış yöntem <strong>Manuel portal</strong>'dır. Canlı resmî gönderim <strong>kapalıdır</strong> (sunucu ayarı).</Notice>
        <div className="form-grid" style={{ marginTop: "1rem" }}>
          <Select label="Etkin yöntem" value={s.invoiceProvider} onChange={(e) => setS({ ...s, invoiceProvider: e.target.value })}>{(provider?.availableProviders ?? ["Manual"]).map((p) => <option key={p}>{p}</option>)}</Select>
          <KV items={[["Canlı gönderim", s.liveSendingEnabled ? <Badge tone="bad">AÇIK</Badge> : <Badge tone="good">Kapalı</Badge>], ["Seçili yöntem", provider?.displayName], ["Otomatik taslak/gönderim", provider?.capabilities.canCreateDraft ? "Destekler" : "Desteklemez (manuel)"], ["Açıklama", provider?.capabilities.description]]} />
        </div></Card>}
      <FormError error={save.error} />
      <div className="form-actions"><Button loading={save.isPending} onClick={() => save.mutate()}>Ayarları kaydet</Button></div>
      <p className="muted small">Veritabanı modu: <strong>{s.databaseMode === "Demo" ? "DEMO (örnek veri)" : "Canlı"}</strong></p>
    </div>
  );
}

function Security() {
  const toast = useToast();
  const [f, setF] = useState({ currentPassword: "", newPassword: "", again: "" });
  const m = useMutation({ mutationFn: () => api.post("/auth/change-password", { currentPassword: f.currentPassword, newPassword: f.newPassword }), onSuccess: () => { toast.success("Parola değiştirildi."); setF({ currentPassword: "", newPassword: "", again: "" }); } });
  return (
    <Card title="Parola değiştir">
      <div className="form-grid">
        <TextInput label="Mevcut parola" type="password" autoComplete="current-password" value={f.currentPassword} onChange={(e) => setF({ ...f, currentPassword: e.target.value })} />
        <div />
        <TextInput label="Yeni parola" type="password" autoComplete="new-password" value={f.newPassword} onChange={(e) => setF({ ...f, newPassword: e.target.value })} hint="En az 10 karakter; kolay tahmin edilebilir olmasın." />
        <TextInput label="Yeni parola (tekrar)" type="password" autoComplete="new-password" value={f.again} onChange={(e) => setF({ ...f, again: e.target.value })} error={f.again && f.again !== f.newPassword ? "Parolalar aynı değil." : undefined} />
      </div>
      <FormError error={m.error} />
      <div className="form-actions"><Button loading={m.isPending} disabled={!f.currentPassword || f.newPassword.length < 10 || f.newPassword !== f.again} onClick={() => m.mutate()}>Parolayı değiştir</Button></div>
    </Card>
  );
}

function Backup() {
  return (
    <Card title="Yedekleme ve geri yükleme">
      <Notice tone="warn"><strong>Tüm kayıtlarınız bilgisayarınızdaki veritabanında tutulur.</strong> Disk bozulması, bilgisayarın kaybı ya da yanlış bir işlem tüm veriyi yok edebilir. Düzenli yedek almak sizin sorumluluğunuzdadır; yedekleri bilgisayar dışında (harici disk / bulut) da saklayın.</Notice>
      <div className="stack-sm" style={{ marginTop: "1rem" }}>
        <p><strong>Yedek almak için</strong> (proje klasöründe, PowerShell):</p>
        <pre className="card" style={{ overflow: "auto", margin: 0 }}>{`./scripts/backup.ps1`}</pre>
        <p><strong>Geri yüklemek için</strong> (dikkat: mevcut veriyi değiştirir):</p>
        <pre className="card" style={{ overflow: "auto", margin: 0 }}>{`./scripts/restore.ps1 -File backups/<dosya>.dump`}</pre>
        <p className="muted">Ayrıntılı adımlar ve yüklenen dosyaların (dekont, fatura PDF/XML) yedeği için README → “Yedekleme ve geri yükleme” bölümüne bakın. Yedek dosyaları müşteri ve vergi bilgisi içerir; gizli tutun.</p>
      </div>
    </Card>
  );
}

function Audit() {
  const [page, setPage] = useState(1);
  const { data, isLoading, error } = useQuery({ queryKey: ["audit", page], queryFn: () => api.get<Paged<AuditLog>>("/settings/audit-logs", { page, pageSize: 30 }) });
  if (isLoading) return <Spinner />;
  if (error) return <ErrorBlock error={error} />;
  return (
    <Card flush title={<span style={{ padding: "0 1.2rem" }}>İşlem geçmişi (denetim kaydı)</span>}>
      <DataTable rows={data!.items} rowKey={(a) => String(a.id)}
        columns={[
          { header: "Zaman", cell: (a) => dateTimeTr(a.timestampUtc) }, { header: "Kullanıcı", cell: (a) => a.userName },
          { header: "İşlem", title: true, cell: (a) => <strong>{a.action}</strong> }, { header: "Ayrıntı", cell: (a) => <span className="small">{a.details}</span> },
        ]} />
      <Pager page={data!.page} pageSize={data!.pageSize} total={data!.total} onPage={setPage} />
    </Card>
  );
}
