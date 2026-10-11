import { useEffect, useState } from "react";
import { Link, useParams } from "react-router-dom";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api, ApiError } from "../lib/api";
import type { DocType, Invoice, ProviderInfo } from "../lib/types";
import { L } from "../lib/types";
import { Badge, Button, Card, Check, DataTable, ErrorBlock, FormError, KV, Modal, Money, NumberInput, Notice, PageHeader, Select, Spinner, TextArea, TextInput, useConfirm, useToast } from "../components/ui";
import { SourceBadge, internalTone, officialTone } from "./Invoices";
import { dateTimeTr, dateTr, money, num, todayIso } from "../lib/format";

const LINKS = {
  luca: "https://turmobefatura.luca.com.tr/",
  earsiv: "https://earsivportal.efatura.gov.tr/",
  efatura: "https://portal.efatura.gov.tr/",
  ebelge: "https://ebelge.gib.gov.tr/",
};
const Ext = ({ href, children }: { href: string; children: React.ReactNode }) => <a href={href} target="_blank" rel="noopener noreferrer">{children} ↗</a>;

export function InvoiceDetailPage() {
  const { id } = useParams();
  const qc = useQueryClient();
  const toast = useToast();
  const confirm = useConfirm();
  const [modal, setModal] = useState<null | "official" | "cancelReq" | "cancelConfirm" | "submit" | "issue" | "preview">(null);
  const { data: inv, isLoading, error, refetch } = useQuery({ queryKey: ["invoice", id], queryFn: () => api.get<Invoice>(`/invoices/${id}`) });
  const { data: provider } = useQuery({ queryKey: ["invoice-provider"], queryFn: () => api.get<ProviderInfo>("/invoices/provider") });

  const act = <T,>(path: string, ok: string, body?: unknown) => ({
    mutationFn: () => api.post<T>(`/invoices/${id}${path}`, body),
    onSuccess: () => { qc.invalidateQueries(); toast.success(ok); },
  });
  const approve = useMutation(act("/approve", "İç onay verildi. Resmî fatura OLUŞTURULMADI; sıradaki adım resmî düzenleme."));
  const toDraft = useMutation(act("/return-to-draft", "Fatura taslağa alındı; yeniden düzenleyebilirsiniz."));
  const awaiting = useMutation(act("/awaiting-official", "İşaretlendi: resmî belge portalda düzenlenecek. Henüz resmî bir işlem yapılmadı."));
  const refresh = useMutation(act("/provider/refresh", "Sağlayıcı durumu sorgulandı."));
  const withdraw = useMutation(act("/withdraw-cancellation", "İptal talebi geri çekildi."));
  const cancelReq = useMutation({
    mutationFn: () => api.post(`/invoices/${id}/provider/cancel-request`, { confirm: true, acknowledgedGrandTotal: inv!.grandTotal, acknowledgedBuyerName: inv!.buyerName }),
    onSuccess: () => { qc.invalidateQueries(); toast.info("İptal TALEBİ portala iletildi. Belge portalda sonuçlanana kadar hâlâ geçerlidir."); },
    onError: () => qc.invalidateQueries(),
  });
  const cancelDraft = useMutation({ mutationFn: (reason: string) => api.post(`/invoices/${id}/cancel-draft`, { reason }), onSuccess: () => { qc.invalidateQueries(); toast.success("Fatura iç süreçte iptal edildi (kayıt saklanır)."); } });

  if (isLoading) return <Spinner />;
  if (error || !inv) return <ErrorBlock error={error} onRetry={refetch} />;

  const unissued = inv.officialStatus === "NotIssued" || inv.officialStatus === "Failed";
  const canProvider = !!provider?.capabilities.canCreateDraft;
  const officialClass = inv.officialStatus === "Issued" ? "ok" : inv.officialStatus === "Cancelled" || inv.officialStatus === "Failed" ? "bad" : inv.officialStatus === "ResultUnknown" || inv.officialStatus === "Submitting" ? "warn" : "";

  return (
    <div className="stack">
      <PageHeader title={inv.officialNumber ?? inv.internalRef} back={{ to: "/faturalar", label: "Faturalar" }}
        subtitle={<><Link to={`/musteriler/${inv.customerId}`}>{inv.customerName}</Link> · {L.docType[inv.documentType]} · satış <Link to={`/satislar/${inv.saleId}`}>{inv.saleNumber}</Link></>}
        actions={<><Button variant="secondary" onClick={() => setModal("preview")}>Önizleme / Yazdır</Button></>} />

      <div className="status-pair">
        <div className="status-box">
          <div className="t">İç süreç durumu (CRM)</div>
          <Badge tone={internalTone(inv.internalStatus)}>{L.internal[inv.internalStatus]}</Badge>
          <p className="small muted" style={{ marginTop: ".5rem" }}>Sizin CRM içindeki kontrol/onay aşamanız. Resmî belge anlamına gelmez.</p>
        </div>
        <div className={`status-box official ${officialClass}`}>
          <div className="t">Resmî belge durumu</div>
          <Badge tone={officialTone(inv.officialStatus)}>{L.official[inv.officialStatus]}</Badge>
          {inv.officialStatus === "Issued" && <div style={{ marginTop: ".4rem" }}><SourceBadge source={inv.officialSource} verified={inv.officialVerified} provider={inv.officialProvider} />
            {inv.officialSource === "Provider" && !inv.providerIsLegallyValid && <div className="small pos" style={{ marginTop: ".3rem" }}>TEST sağlayıcısı — bu belge yasal olarak geçerli DEĞİLDİR.</div>}</div>}
          {inv.officialStatus === "NotIssued" && <p className="small muted" style={{ marginTop: ".5rem" }}>Resmî belge henüz oluşturulmadı / kaydedilmedi.</p>}
          {inv.officialStatus === "ResultUnknown" && <p className="small" style={{ marginTop: ".5rem" }}>Gönderim sonucu alınamadı. Belgenin oluşup oluşmadığı bilinmiyor — <strong>tekrar göndermeyin</strong>, “Durumu sorgula”yı kullanın.</p>}
        </div>
      </div>

      {inv.providerLastMessage && inv.officialStatus !== "Issued" && <Notice tone="warn" title="Sağlayıcı mesajı">{inv.providerLastMessage}</Notice>}
      {inv.internalStatus === "Cancelled" && inv.officialStatus === "NotIssued" && <Notice tone="error" title="Bu fatura taslağı iptal edildi">{inv.cancelReason} <span className="muted">(Kayıt denetim izi için saklanır.)</span></Notice>}
      {inv.officialStatus === "Cancelled" && <Notice tone="error" title="Resmî olarak iptal edildi">İptal tarihi: {dateTr(inv.officialCancelDate)} · Referans: {inv.officialCancelReference}. Resmî numara: {inv.officialNumber}.
        {!inv.officialCancelVerified && " İptal, kullanıcı beyanıyla kaydedildi (GİB'den/sağlayıcıdan doğrulanmadı)."}</Notice>}

      {/* ───── Sonraki adım paneli ───── */}
      {inv.internalStatus === "Draft" && (
        <Card title="Sıradaki adım: kontrol edip iç onaya alın">
          <p>Alıcı bilgilerini, belge türünü ve tutarları kontrol edin; gerekirse aşağıdan düzenleyin. <strong>“Faturayı onayla”</strong> yalnızca CRM içindeki iç onaydır.</p>
          {inv.issues.length > 0 && <Notice tone={inv.issues.some((i) => i.isBlocking) ? "error" : "warn"} title="Kontrol sonuçları"><ul>{inv.issues.map((i, k) => <li key={k}>{i.isBlocking ? "⛔ " : "⚠ "}{i.message}</li>)}</ul></Notice>}
          <div className="form-actions">
            <Button loading={approve.isPending} disabled={inv.issues.some((i) => i.isBlocking)} onClick={async () => {
              const r = await confirm({ title: "Faturayı onayla (iç onay)", confirmLabel: "İç onayı ver", message: <div className="stack-sm">
                <p><strong>Bu işlem ne yapar?</strong> Tutarları ve alıcı bilgisini kontrol ettiğinizi CRM'e bildirir; fatura düzenlenemez hale gelir.</p>
                <p><strong>Ne yapmaz?</strong> Resmî (e-Fatura / e-Arşiv) belge oluşturmaz, GİB'e veya LUCA'ya hiçbir şey göndermez, müşteri bakiyesini değiştirmez.</p>
                <p>Sonraki adımda belgeyi {canProvider ? "sağlayıcı üzerinden veya " : ""}resmî portalda düzenleyeceksiniz.</p></div> });
              if (r.ok) approve.mutate();
            }}>Faturayı onayla (iç onay)</Button>
            <Button variant="outline-danger" onClick={async () => { const r = await confirm({ title: "Fatura taslağını iptal et", danger: true, confirmLabel: "Taslağı iptal et", reasonLabel: "İptal nedeni (zorunlu)", message: "Taslak iç süreçte iptal edilir. Kayıt silinmez, denetim izi korunur; satış ve cari hesap etkilenmez. İsterseniz yeniden taslak hazırlayabilirsiniz." }); if (r.ok) cancelDraft.mutate(r.reason); }}>Taslağı iptal et</Button>
          </div>
          <FormError error={approve.error} />
        </Card>
      )}

      {(inv.internalStatus === "Approved" || inv.internalStatus === "AwaitingOfficial" || inv.internalStatus === "Error") && unissued && (
        <Card title="Sıradaki adım: resmî belgeyi düzenleyin">
          <Notice tone="warn"><strong>İç onay verildi ama resmî fatura henüz yok.</strong> Müşteriye resmî fatura sayılacak belge, aşağıdaki adımlar tamamlanıp numarası buraya kaydedilene kadar oluşmuş kabul edilmez.</Notice>
          {inv.documentType === "Undetermined" || inv.customerEInvoiceStatus === "Unknown" ? <Notice tone="warn" title="Belge türünü doğrulayın">Müşterinin e-Fatura mükellefiyeti doğrulanmadı. Belge türü (e-Fatura / e-Arşiv) mükellefiyete göre belirlenir; <Ext href={LINKS.efatura}>GİB e-Fatura portalındaki</Ext> mükellef sorgusundan kontrol edin.</Notice> : null}
          <h3 style={{ margin: "1rem 0 .6rem" }}>Manuel portal akışı (şu an doğrulanmış yöntem)</h3>
          <ol className="steps">
            <li><div><strong>Taslağı hazır tutun.</strong> <Button size="sm" variant="secondary" onClick={() => setModal("preview")}>Önizleme / Yazdır</Button> Alıcı unvanı, vergi no, kalemler ve tutarlar aşağıda.</div></li>
            <li><div><strong>Belgeyi resmî portalda düzenleyin:</strong> <Ext href={LINKS.luca}>LUCA (TÜRMOB e-Fatura)</Ext> · <Ext href={LINKS.earsiv}>GİB e-Arşiv Portalı</Ext> · <Ext href={LINKS.efatura}>GİB e-Fatura Portalı</Ext>. Bu uygulama portala otomatik giriş yapmaz ve parolanızı saklamaz.</div></li>
            <li><div><strong>Çıkan resmî numarayı (ve varsa ETTN'i) buraya kaydedin.</strong> İsterseniz PDF/XML dosyasını da ekleyin.</div></li>
          </ol>
          <div className="form-actions">
            {inv.internalStatus === "Approved" && <Button variant="secondary" loading={awaiting.isPending} onClick={() => awaiting.mutate()}>Portalda düzenleyeceğim (işaretle)</Button>}
            <Button onClick={() => setModal("official")}>Resmî belge bilgisini kaydet</Button>
            <Button variant="secondary" loading={toDraft.isPending} onClick={() => toDraft.mutate()}>Taslağa geri al</Button>
            <Button variant="outline-danger" onClick={async () => { const r = await confirm({ title: "Faturayı iptal et", danger: true, confirmLabel: "İptal et", reasonLabel: "İptal nedeni (zorunlu)", message: "Resmî belge henüz oluşmadığı için fatura iç süreçte iptal edilir. DİKKAT: portalda belgeyi zaten düzenlediyseniz bunu yapmayın; önce resmî numarayı kaydedin." }); if (r.ok) cancelDraft.mutate(r.reason); }}>İptal et</Button>
          </div>
          <FormError error={awaiting.error ?? toDraft.error ?? cancelDraft.error} />

          {canProvider && (
            <div style={{ marginTop: "1.2rem", borderTop: "1px solid var(--border)", paddingTop: "1rem" }}>
              <h3 style={{ marginBottom: ".4rem" }}>Sağlayıcı ile gönderim: {provider!.displayName}</h3>
              <p className="muted small">{provider!.capabilities.description}</p>
              {provider!.canSubmit ? <Button onClick={() => setModal("submit")}>Sağlayıcıya gönder…</Button> : <Notice tone="warn">{provider!.submitBlockedReason}</Notice>}
            </div>
          )}
        </Card>
      )}

      {(inv.officialStatus === "Submitting" || inv.officialStatus === "ResultUnknown") && (
        <Card title="Sağlayıcıda işlemde">
          <p>Sağlayıcı: <strong>{inv.officialProvider}</strong>. {inv.officialStatus === "Submitting" ? "Sağlayıcıda TASLAK oluşturuldu; resmî belge için ayrıca imzalama/gönderme onayı gerekir." : "Sonuç alınamadı; önce durumu sorgulayın."}</p>
          <div className="form-actions">
            <Button variant="secondary" loading={refresh.isPending} onClick={() => refresh.mutate()}>Durumu sorgula</Button>
            {inv.officialStatus === "Submitting" && provider?.capabilities.canIssue && provider.canSubmit && <Button onClick={() => setModal("issue")}>Resmî belgeye dönüştür (imzala)…</Button>}
            <Button variant="secondary" onClick={() => setModal("official")}>Portaldan aldığım numarayı elle kaydet</Button>
          </div>
          <FormError error={refresh.error} />
        </Card>
      )}

      {inv.internalStatus === "Completed" && inv.officialStatus === "Issued" && (
        <Card title="Resmî belge kaydedildi">
          <KV items={[
            ["Resmî fatura no", <strong>{inv.officialNumber}</strong>], ["ETTN / UUID", inv.officialUuid], ["Düzenlenme tarihi", dateTr(inv.officialIssueDate)],
            ["Resmî belge türü", inv.officialDocumentType ? L.docType[inv.officialDocumentType] : "—"], ["Kaynak", <SourceBadge source={inv.officialSource} verified={inv.officialVerified} provider={inv.officialProvider} />],
            ["Kaydedilme", dateTimeTr(inv.officialRecordedAtUtc)],
          ]} />
          <div className="form-actions"><Button variant="outline-danger" onClick={() => setModal("cancelReq")}>Resmî iptal talebi aç…</Button></div>
        </Card>
      )}

      {inv.internalStatus === "CancellationRequired" && (
        <Card title="İptal isteği gerekiyor">
          <Notice tone="warn"><strong>Resmî belge hâlâ geçerli.</strong> “{inv.cancellationReason}” nedeniyle iptal talebi açıldı, ancak CRM'deki bir düğme resmî belgeyi iptal etmez. İlgili belge türü için geçerli resmî süreci (iptal / itiraz / ret / düzeltme) portalda tamamlayın; tamamlandığını doğruladıktan sonra aşağıdan kaydedin. Hangi sürecin uygulanacağını muhasebecinizle teyit edin.</Notice>
          <ol className="steps" style={{ marginTop: ".8rem" }}>
            <li><div>Belge: <strong>{inv.officialNumber}</strong> ({inv.officialDocumentType ? L.docType[inv.officialDocumentType] : ""}). Portal: <Ext href={LINKS.luca}>LUCA</Ext> · <Ext href={LINKS.earsiv}>e-Arşiv</Ext> · <Ext href={LINKS.efatura}>e-Fatura</Ext> · <Ext href={LINKS.ebelge}>e-Belge bilgi</Ext></div></li>
            <li><div>Portalda işlem tamamlanınca referansı/tarihi buraya kaydedin.</div></li>
          </ol>
          <div className="form-actions">
            {inv.officialSource === "Provider" && (provider?.capabilities.canQueryStatus && provider?.capabilities.canReportCancellation) ? <Button onClick={() => setModal("cancelConfirm")}>İptal edildi mi? Sağlayıcıdan doğrula…</Button> : <Button onClick={() => setModal("cancelConfirm")}>Resmî iptal tamamlandı — kaydet…</Button>}
            {inv.officialSource === "Provider" && provider?.capabilities.canCancel && provider.canSubmit && (
              <Button variant="outline-danger" loading={cancelReq.isPending} onClick={async () => { const r = await confirm({ title: "İptal talebini portala ilet", danger: true, confirmLabel: "Talebi portala ilet", message: <div className="stack-sm"><p><strong>{inv.officialNumber}</strong> için GİB e-Arşiv Portalında resmî <strong>iptal talebi</strong> oluşturulur.</p><p>Bu belgeyi iptal ETMEZ ve CRM'de \"iptal edildi\" yapmaz; sonuç portalda doğrulanınca aşağıdan kaydedilir. Talep geri alınamayabilir.</p></div> }); if (r.ok) cancelReq.mutate(); }}>İptal talebini portala ilet</Button>
            )}
            <Button variant="secondary" loading={withdraw.isPending} onClick={() => withdraw.mutate()}>İptal talebini geri çek</Button>
          </div>
          <FormError error={withdraw.error ?? cancelReq.error} />
        </Card>
      )}

      {/* ───── Taslak düzenleme / ayrıntı ───── */}
      {inv.internalStatus === "Draft" ? <DraftEditor inv={inv} /> : <ReadOnlyDetail inv={inv} />}

      <Card title="Kalemler" flush>
        <DataTable rows={inv.items} rowKey={(i) => i.id} footer
          columns={[
            { header: "Açıklama", title: true, cell: (i) => i.description, foot: "Toplam" },
            { header: "Miktar", num: true, cell: (i) => `${num(i.quantity)} ${i.unit}` },
            { header: "Birim fiyat", num: true, cell: (i) => <Money value={i.unitPrice} /> },
            { header: "KDV hariç", num: true, cell: (i) => <Money value={i.netAmount} />, foot: <Money value={inv.netTotal} /> },
            { header: "KDV", num: true, cell: (i) => `%${num(i.vatRate)} · ${money(i.vatAmount)}`, foot: <Money value={inv.vatTotal} /> },
            { header: "Toplam", num: true, cell: (i) => <Money value={i.lineTotal} />, foot: <Money value={inv.grandTotal} /> },
          ]} />
      </Card>

      <div className="grid cols-2">
        <Card title="Tahsilat durumu">
          <KV items={[["Genel toplam", <Money value={inv.grandTotal} />], ["Tahsil edilen", <Money value={inv.paidTotal} />], ["Kalan bakiye", <strong><Money value={inv.remaining} /></strong>]]} />
          <p className="muted small" style={{ marginTop: ".6rem" }}>Tahsilat, faturanın bağlı olduğu satışa dağıtılan ödemelerden hesaplanır. Fatura müşteri bakiyesini ayrıca artırmaz.</p>
          {inv.remaining > 0 && inv.internalStatus !== "Cancelled" && <Link className="btn secondary sm" to={`/tahsilatlar/yeni?musteri=${inv.customerId}&satis=${inv.saleId}`}>+ Tahsilat gir</Link>}
        </Card>
        <Attachments inv={inv} />
      </div>

      {modal === "official" && <OfficialModal inv={inv} onClose={() => setModal(null)} />}
      {modal === "cancelReq" && <CancelRequestModal inv={inv} onClose={() => setModal(null)} />}
      {modal === "cancelConfirm" && <CancelConfirmModal inv={inv} provider={provider} onClose={() => setModal(null)} />}
      {(modal === "submit" || modal === "issue") && provider && <ProviderModal inv={inv} provider={provider} mode={modal} onClose={() => setModal(null)} onNext={() => setModal("issue")} />}
      {modal === "preview" && <PreviewModal inv={inv} onClose={() => setModal(null)} />}
    </div>
  );
}

function DraftEditor({ inv }: { inv: Invoice }) {
  const qc = useQueryClient();
  const toast = useToast();
  const [f, setF] = useState(() => init(inv));
  useEffect(() => setF(init(inv)), [inv.id, inv.invoiceDate, inv.buyerName, inv.documentType]); // eslint-disable-line
  function init(i: Invoice) {
    return { invoiceDate: i.invoiceDate, dueDate: i.dueDate ?? "", documentType: i.documentType as DocType, buyerName: i.buyerName, buyerTaxOffice: i.buyerTaxOffice ?? "", buyerTaxNumber: i.buyerTaxNumber ?? "", buyerNationalId: i.buyerNationalId ?? "", buyerAddress: i.buyerAddress ?? "", buyerCity: i.buyerCity ?? "", buyerDistrict: i.buyerDistrict ?? "", buyerEmail: i.buyerEmail ?? "", note: i.note ?? "", lines: Object.fromEntries(i.items.map((x) => [x.id, x.description])) };
  }
  const up = (k: string, v: string) => setF((x) => ({ ...x, [k]: v }));
  const save = useMutation({
    mutationFn: () => api.put<Invoice>(`/invoices/${inv.id}`, { ...f, dueDate: f.dueDate || null, buyerTaxOffice: f.buyerTaxOffice || null, buyerTaxNumber: f.buyerTaxNumber || null, buyerNationalId: f.buyerNationalId || null, buyerAddress: f.buyerAddress || null, buyerCity: f.buyerCity || null, buyerDistrict: f.buyerDistrict || null, buyerEmail: f.buyerEmail || null, note: f.note || null, lines: Object.entries(f.lines).map(([id, description]) => ({ id, description })) }),
    onSuccess: () => { qc.invalidateQueries(); toast.success("Taslak kaydedildi."); },
  });
  const err = save.error instanceof ApiError ? save.error : null;
  const einv = inv.customerEInvoiceStatus;
  return (
    <Card title="Taslağı düzenle">
      <div className="notice info" style={{ marginBottom: "1rem" }}>Tutarlar satıştan gelir ve burada değiştirilemez. Tutar hatası varsa satışı iptal edip doğru tutarla yeniden girin.</div>
      <div className="form-grid">
        <Select label="Belge türü" required value={f.documentType} onChange={(e) => up("documentType", e.target.value)} error={err?.field("documentType")}
          hint={einv === "Unknown" ? "Müşterinin e-Fatura mükellefiyeti doğrulanmadı; portaldan sorgulayın." : einv === "Registered" ? "Müşteri e-Fatura mükellefi olarak kayıtlı." : "Müşteri e-Fatura mükellefi değil olarak kayıtlı."}>
          <option value="Undetermined">Seçiniz…</option><option value="EArchive">e-Arşiv Fatura</option><option value="EInvoice">e-Fatura</option>
        </Select>
        <TextInput label="Fatura tarihi" type="date" value={f.invoiceDate} onChange={(e) => up("invoiceDate", e.target.value)} error={err?.field("invoiceDate")} />
        <TextInput className="span-2" label="Alıcı unvanı / ad soyad" required value={f.buyerName} onChange={(e) => up("buyerName", e.target.value)} error={err?.field("buyerName")} />
        <TextInput label="Vergi kimlik no (10 hane)" inputMode="numeric" maxLength={10} value={f.buyerTaxNumber} onChange={(e) => up("buyerTaxNumber", e.target.value.replace(/\D/g, ""))} error={err?.field("buyerTaxNumber")} />
        <TextInput label="T.C. kimlik no (11 hane)" inputMode="numeric" maxLength={11} value={f.buyerNationalId} onChange={(e) => up("buyerNationalId", e.target.value.replace(/\D/g, ""))} error={err?.field("buyerNationalId")} />
        <TextInput label="Vergi dairesi" value={f.buyerTaxOffice} onChange={(e) => up("buyerTaxOffice", e.target.value)} />
        <TextInput label="Vade tarihi" type="date" value={f.dueDate} onChange={(e) => up("dueDate", e.target.value)} error={err?.field("dueDate")} />
        <TextInput className="span-2" label="Adres" value={f.buyerAddress} onChange={(e) => up("buyerAddress", e.target.value)} />
        <TextInput label="İl" value={f.buyerCity} onChange={(e) => up("buyerCity", e.target.value)} />
        <TextInput label="İlçe" value={f.buyerDistrict} onChange={(e) => up("buyerDistrict", e.target.value)} />
        <TextInput label="E-posta" value={f.buyerEmail} onChange={(e) => up("buyerEmail", e.target.value)} />
        <TextArea className="span-2" label="Fatura notu" value={f.note} onChange={(e) => up("note", e.target.value)} />
        <div className="span-2 stack-sm"><strong>Kalem açıklamaları</strong>
          {inv.items.map((i) => <TextInput key={i.id} label={`${i.lineNo}. kalem (${num(i.quantity)} ${i.unit})`} value={f.lines[i.id] ?? ""} onChange={(e) => setF((x) => ({ ...x, lines: { ...x.lines, [i.id]: e.target.value } }))} />)}</div>
      </div>
      <FormError error={save.error} />
      <div className="form-actions"><Button loading={save.isPending} onClick={() => save.mutate()}>Taslağı kaydet</Button></div>
    </Card>
  );
}

function ReadOnlyDetail({ inv }: { inv: Invoice }) {
  return (
    <Card title="Fatura bilgileri">
      <KV items={[
        ["İç referans", inv.internalRef], ["Fatura tarihi", dateTr(inv.invoiceDate)], ["Vade", dateTr(inv.dueDate)], ["Belge türü (taslak)", L.docType[inv.documentType]],
        ["Alıcı", inv.buyerName], ["Vergi dairesi / no", [inv.buyerTaxOffice, inv.buyerTaxNumber ?? inv.buyerNationalId].filter(Boolean).join(" / ") || "—"],
        ["Adres", [inv.buyerAddress, inv.buyerDistrict, inv.buyerCity].filter(Boolean).join(", ") || "—"], ["Not", inv.note],
      ]} />
    </Card>
  );
}

function Attachments({ inv }: { inv: Invoice }) {
  const qc = useQueryClient();
  const toast = useToast();
  const [file, setFile] = useState<File | null>(null);
  const upload = useMutation({
    mutationFn: () => api.upload(`/invoices/${inv.id}/attachments`, file!),
    onSuccess: () => { qc.invalidateQueries(); setFile(null); toast.success("Dosya fatura kaydına eklendi."); },
  });
  const disabled = inv.internalStatus === "Cancelled" && inv.officialStatus === "NotIssued";
  return (
    <Card title="Ekler (PDF / XML)">
      {inv.attachments.length === 0 ? <p className="muted">Henüz ek yok. Portaldan indirdiğiniz resmî PDF/XML dosyasını buraya ekleyebilirsiniz.</p> : (
        <ul style={{ paddingLeft: "1.1rem", margin: "0 0 .8rem" }}>{inv.attachments.map((a) => (
          <li key={a.id}><a href="#" onClick={(e) => { e.preventDefault(); api.download(`/invoices/${inv.id}/attachments/${a.id}`, a.fileName).catch((er) => toast.error(er.message)); }}>{a.fileName}</a> <span className="muted small">({a.kind.toUpperCase()}, {(a.sizeBytes / 1024).toFixed(0)} KB · {dateTimeTr(a.uploadedAtUtc)})</span></li>))}</ul>
      )}
      {!disabled && (
        <div className="stack-sm">
          <input type="file" accept=".pdf,.xml,application/pdf,text/xml,application/xml" onChange={(e) => setFile(e.target.files?.[0] ?? null)} aria-label="Dosya seç" />
          <div><Button size="sm" disabled={!file} loading={upload.isPending} onClick={() => upload.mutate()}>Yükle</Button></div>
          <FormError error={upload.error} />
          <p className="muted small">Yalnızca PDF ve XML, en fazla 10 MB. Dosya içeriği doğrulanır.</p>
        </div>
      )}
    </Card>
  );
}

function OfficialModal({ inv, onClose }: { inv: Invoice; onClose: () => void }) {
  const qc = useQueryClient();
  const toast = useToast();
  const [f, setF] = useState({ number: "", issueDate: todayIso(), uuid: "", documentType: inv.documentType === "Undetermined" ? "" : inv.documentType });
  const save = useMutation({
    mutationFn: () => api.post(`/invoices/${inv.id}/official`, { number: f.number, issueDate: f.issueDate, uuid: f.uuid || null, documentType: f.documentType }),
    onSuccess: () => { qc.invalidateQueries(); toast.success("Resmî belge bilgisi kaydedildi (kullanıcı girişi; GİB'den doğrulanmadı)."); onClose(); },
  });
  const err = save.error instanceof ApiError ? save.error : null;
  return (
    <Modal title="Resmî belge bilgisini kaydet" onClose={onClose} footer={<><Button variant="secondary" onClick={onClose}>Vazgeç</Button><Button loading={save.isPending} disabled={!f.number.trim() || !f.documentType} onClick={() => save.mutate()}>Kaydet</Button></>}>
      <div className="stack">
        <Notice tone="warn">Bu bilgiyi <strong>resmî portalda (LUCA / GİB) belgeyi düzenledikten sonra</strong> girin. CRM bu numarayı GİB ile doğrulayamaz; kayıt “kullanıcı girişi” olarak işaretlenir. Aynı resmî numara ikinci bir faturaya kaydedilemez.</Notice>
        <TextInput label="Resmî fatura numarası" required value={f.number} onChange={(e) => setF({ ...f, number: e.target.value })} error={err?.field("number")} placeholder="örn. ABC2026000000001" autoFocus />
        <TextInput label="Düzenlenme tarihi" type="date" value={f.issueDate} onChange={(e) => setF({ ...f, issueDate: e.target.value })} error={err?.field("issueDate")} />
        <TextInput label="ETTN / UUID (varsa)" value={f.uuid} onChange={(e) => setF({ ...f, uuid: e.target.value })} error={err?.field("uuid")} placeholder="3fa85f64-5717-4562-b3fc-2c963f66afa6" />
        <Select label="Resmî belge türü" required value={f.documentType} onChange={(e) => setF({ ...f, documentType: e.target.value as any })} error={err?.field("documentType")}>
          <option value="">Seçiniz…</option><option value="EArchive">e-Arşiv Fatura</option><option value="EInvoice">e-Fatura</option>
        </Select>
        <FormError error={save.error} />
      </div>
    </Modal>
  );
}

function CancelRequestModal({ inv, onClose }: { inv: Invoice; onClose: () => void }) {
  const qc = useQueryClient();
  const toast = useToast();
  const [reason, setReason] = useState("");
  const m = useMutation({ mutationFn: () => api.post(`/invoices/${inv.id}/request-cancellation`, { reason }), onSuccess: () => { qc.invalidateQueries(); toast.info("İptal talebi açıldı. Resmî belge portalda iptal edilene kadar GEÇERLİDİR."); onClose(); } });
  return (
    <Modal title="Resmî iptal talebi aç" onClose={onClose} footer={<><Button variant="secondary" onClick={onClose}>Vazgeç</Button><Button variant="danger" loading={m.isPending} disabled={!reason.trim()} onClick={() => m.mutate()}>Talebi aç</Button></>}>
      <div className="stack">
        <Notice tone="warn"><strong>{inv.officialNumber}</strong> numaralı resmî fatura için iptal ihtiyacı işaretlenir. Bu işlem belgeyi <strong>iptal etmez</strong>; resmî durum “kayıtlı” kalır. Belgenin iptali / itirazı / reddi / düzeltmesi portalda ilgili süreçle yapılmalıdır.</Notice>
        <TextArea label="Nedeni (zorunlu)" value={reason} onChange={(e) => setReason(e.target.value)} autoFocus />
        <FormError error={m.error} />
      </div>
    </Modal>
  );
}

function CancelConfirmModal({ inv, provider, onClose }: { inv: Invoice; provider?: ProviderInfo; onClose: () => void }) {
  const qc = useQueryClient();
  const toast = useToast();
  const viaProvider = inv.officialSource === "Provider" && !!(provider?.capabilities.canQueryStatus && provider?.capabilities.canReportCancellation);
  const [f, setF] = useState({ confirmed: false, reference: "", date: todayIso(), cancelSale: false });
  const m = useMutation({
    mutationFn: () => api.post(`/invoices/${inv.id}/confirm-cancellation`, f),
    onSuccess: () => { qc.invalidateQueries(); toast.success(f.cancelSale ? "Resmî iptal kaydedildi; satış, cari hesap ve stok tek işlemde düzeltildi." : "Resmî iptal kaydedildi."); onClose(); },
  });
  return (
    <Modal title={viaProvider ? "Resmî iptali sağlayıcıdan doğrula" : "Resmî iptal tamamlandı — kaydet"} onClose={onClose}
      footer={<><Button variant="secondary" onClick={onClose}>Vazgeç</Button><Button variant="danger" loading={m.isPending} disabled={!viaProvider && (!f.confirmed || !f.reference.trim())} onClick={() => m.mutate()}>{viaProvider ? "Doğrula ve kaydet" : "İptali kaydet"}</Button></>}>
      <div className="stack">
        {viaProvider ? <Notice tone="info">Sağlayıcıdan belgenin durumu sorgulanır. Yalnızca sağlayıcı “iptal edildi” derse kayıt yapılır.</Notice>
          : <Notice tone="warn"><strong>{inv.officialNumber}</strong> belgesinin iptal / itiraz / ret / düzeltme sürecini <strong>portalda tamamladığınızı</strong> doğrulayın. CRM bunu GİB'den kontrol edemez; kayıt “kullanıcı beyanı” olarak işaretlenir.</Notice>}
        {!viaProvider && <>
          <Check checked={f.confirmed} onChange={(v) => setF({ ...f, confirmed: v })}>Resmî iptal işleminin portalda tamamlandığını doğruladım.</Check>
          <TextInput label="Portal referansı / işlem açıklaması" required value={f.reference} onChange={(e) => setF({ ...f, reference: e.target.value })} hint="Örn. iptal işlem no, itiraz tarihi, onay mesajı." />
        </>}
        {viaProvider && <TextInput label="Referans (isteğe bağlı not)" value={f.reference} onChange={(e) => setF({ ...f, reference: e.target.value || "sağlayıcı doğrulaması" })} />}
        <TextInput label="İptal tarihi" type="date" value={f.date} onChange={(e) => setF({ ...f, date: e.target.value })} />
        <Check checked={f.cancelSale} onChange={(v) => setF({ ...f, cancelSale: v })}>Bağlı satış {inv.saleNumber} da iptal edilsin (mal iade edildi / satıştan vazgeçildi)</Check>
        {f.cancelSale ? <Notice tone="warn">Satış iptalinde müşterinin cari hesabına ters kayıt düşülür ve stok geri girer. Yapılan tahsilatlar silinmez, müşteri avansı olarak kalır. Hepsi tek işlemde yapılır.</Notice>
          : <Notice tone="info">Satış geçerli kalır ve müşterinin borcu değişmez; isterseniz doğru bilgilerle yeni fatura taslağı hazırlayabilirsiniz.</Notice>}
        <FormError error={m.error} />
      </div>
    </Modal>
  );
}

function ProviderModal({ inv, provider, mode, onClose, onNext }: { inv: Invoice; provider: ProviderInfo; mode: "submit" | "issue"; onClose: () => void; onNext?: () => void }) {
  const qc = useQueryClient();
  const toast = useToast();
  const [ack, setAck] = useState(false);
  const [sms, setSms] = useState("");
  const m = useMutation({
    mutationFn: () => api.post(`/invoices/${inv.id}/provider/${mode}`, { confirm: ack, acknowledgedGrandTotal: inv.grandTotal, acknowledgedBuyerName: inv.buyerName, smsCode: sms || null }),
    onSuccess: () => { qc.invalidateQueries(); toast.success(mode === "submit" ? "Portalda taslak oluşturuldu (resmî belge değil). Şimdi imzalama adımı." : "Portal resmî belgeyi düzenledi; numara kaydedildi."); if (mode === "submit" && provider.capabilities.canIssue && onNext) onNext(); else onClose(); },
    onError: () => qc.invalidateQueries(),
  });
  const live = provider.capabilities.isLiveCapable;
  const [challengeMsg, setChallengeMsg] = useState<string | null>(null);
  const challenge = useMutation({
    mutationFn: () => api.post<{ maskedPhone: string | null; message: string | null }>(`/invoices/${inv.id}/provider/issue-challenge`, { confirm: ack, acknowledgedGrandTotal: inv.grandTotal, acknowledgedBuyerName: inv.buyerName }),
    onSuccess: (r) => setChallengeMsg(`${r.message ?? "SMS kodu gönderildi."}${r.maskedPhone ? ` (${r.maskedPhone})` : ""}`),
  });
  return (
    <Modal title={mode === "submit" ? "Sağlayıcıya gönder (taslak oluştur)" : "Resmî belgeye dönüştür"} onClose={onClose} wide
      footer={<><Button variant="secondary" onClick={onClose}>Vazgeç</Button><Button variant={live ? "danger" : undefined} loading={m.isPending} disabled={!ack || (provider.capabilities.requiresSmsCode && mode === "issue" && !sms)} onClick={() => m.mutate()}>{mode === "submit" ? "Gönder" : "Resmî belgeyi düzenle"}</Button></>}>
      <div className="stack">
        {live ? <Notice tone="error" title="GERÇEK RESMÎ İŞLEM">{mode === "issue" ? "Bu adım yasal sonuç doğuran gerçek bir fatura düzenler ve geri alınamaz." : "Sağlayıcıda gerçek bir fatura taslağı oluşturulur."}</Notice>
          : <Notice tone="info" title="Test sağlayıcısı">Bu sağlayıcı sahte/test amaçlıdır; oluşan belge yasal olarak geçerli değildir.</Notice>}
        <KV items={[
          ["Sağlayıcı", provider.displayName], ["Alıcı", <strong>{inv.buyerName}</strong>], ["Vergi / T.C. no", inv.buyerTaxNumber ?? inv.buyerNationalId ?? "—"],
          ["Adres", [inv.buyerAddress, inv.buyerDistrict, inv.buyerCity].filter(Boolean).join(", ") || "—"], ["Belge türü", L.docType[inv.documentType]], ["Fatura tarihi", dateTr(inv.invoiceDate)],
          ["KDV hariç", <Money value={inv.netTotal} />], ["KDV", <Money value={inv.vatTotal} />], ["Genel toplam", <strong><Money value={inv.grandTotal} /></strong>],
        ]} />
        <DataTable rows={inv.items} rowKey={(i) => i.id} columns={[
          { header: "Kalem", title: true, cell: (i) => i.description }, { header: "Miktar", num: true, cell: (i) => `${num(i.quantity)} ${i.unit}` }, { header: "Tutar", num: true, cell: (i) => <Money value={i.lineTotal} /> },
        ]} />
        {mode === "issue" && provider.capabilities.requiresSmsCode && (
          <div className="stack-sm">
            <div><Button size="sm" variant="secondary" disabled={!ack} loading={challenge.isPending} onClick={() => challenge.mutate()}>1) Portala kayıtlı telefona SMS kodu gönder</Button></div>
            {challengeMsg && <Notice tone="good">{challengeMsg}</Notice>}
            <FormError error={challenge.error} />
            <TextInput label="2) SMS onay kodu" inputMode="numeric" value={sms} onChange={(e) => setSms(e.target.value.replace(/D/g, ""))} hint="Portalın kayıtlı telefona gönderdiği kod." />
          </div>
        )}
        <Check checked={ack} onChange={setAck}>Yukarıdaki alıcıyı, kalemleri ve <strong>{money(inv.grandTotal)}</strong> tutarını kontrol ettim; işlemin yapılmasını onaylıyorum.</Check>
        <FormError error={m.error} />
      </div>
    </Modal>
  );
}

function PreviewModal({ inv, onClose }: { inv: Invoice; onClose: () => void }) {
  const official = inv.officialStatus === "Issued";
  const { data: s } = useQuery({ queryKey: ["settings"], queryFn: () => api.get<any>("/settings") });
  const c = s?.company;
  return (
    <Modal title="Fatura önizleme" onClose={onClose} wide footer={<><Button variant="secondary" onClick={onClose}>Kapat</Button><Button onClick={() => window.print()}>Yazdır / PDF olarak kaydet</Button></>}>
      <div className="print-area">
        <div className="invoice-preview">
          {!official && <div className="watermark">TASLAK<br />Resmî fatura değildir</div>}
          <div className="row-between" style={{ alignItems: "flex-start" }}>
            <div><h2 style={{ marginBottom: 4 }}>{c?.name || "Satıcı (Ayarlar'dan firma bilgisi girin)"}</h2><div className="small muted">{[c?.address, c?.taxOffice && `VD: ${c.taxOffice}`, c?.taxNumber && `VKN/TCKN: ${c.taxNumber}`, c?.phone].filter(Boolean).join(" · ")}</div></div>
            <div className="right"><strong>{official ? L.docType[inv.officialDocumentType ?? inv.documentType] : `${L.docType[inv.documentType]} — TASLAK`}</strong>
              <div className="small">{official ? <>No: <strong>{inv.officialNumber}</strong></> : <>İç ref.: {inv.internalRef}</>}</div>
              {inv.officialUuid && <div className="small">ETTN: {inv.officialUuid}</div>}
              <div className="small">Tarih: {dateTr(official ? inv.officialIssueDate : inv.invoiceDate)}</div>{inv.dueDate && <div className="small">Vade: {dateTr(inv.dueDate)}</div>}</div>
          </div>
          <hr />
          <div><strong>Alıcı:</strong> {inv.buyerName}<div className="small">{[inv.buyerAddress, inv.buyerDistrict, inv.buyerCity].filter(Boolean).join(", ")}</div><div className="small">{[inv.buyerTaxOffice && `VD: ${inv.buyerTaxOffice}`, inv.buyerTaxNumber && `VKN: ${inv.buyerTaxNumber}`, inv.buyerNationalId && `TCKN: ${inv.buyerNationalId}`].filter(Boolean).join(" · ")}</div></div>
          <table><thead><tr><th>#</th><th>Açıklama</th><th className="num">Miktar</th><th className="num">Birim fiyat</th><th className="num">İndirim</th><th className="num">KDV hariç</th><th className="num">KDV</th><th className="num">Toplam</th></tr></thead>
            <tbody>{inv.items.map((i) => <tr key={i.id}><td>{i.lineNo}</td><td>{i.description}</td><td className="num">{num(i.quantity)} {i.unit}</td><td className="num">{money(i.unitPrice)}</td><td className="num">{i.discountPercent > 0 || i.discountAmount > 0 ? `${i.discountPercent > 0 ? "%" + num(i.discountPercent) : ""} ${i.discountAmount > 0 ? money(i.discountAmount) : ""}` : "—"}</td><td className="num">{money(i.netAmount)}</td><td className="num">%{num(i.vatRate)} {money(i.vatAmount)}</td><td className="num">{money(i.lineTotal)}</td></tr>)}</tbody></table>
          <div className="totals"><div><span>KDV hariç toplam</span><span>{money(inv.netTotal)}</span></div><div><span>Toplam KDV</span><span>{money(inv.vatTotal)}</span></div><div className="grand"><span>Ödenecek tutar</span><span>{money(inv.grandTotal)}</span></div></div>
          {inv.note && <p className="small">Not: {inv.note}</p>}
          {!official && <p className="small pos" style={{ marginTop: "1rem" }}>Bu belge CRM içinde hazırlanmış TASLAKTIR; resmî (e-Fatura / e-Arşiv) fatura değildir ve vergisel hüküm doğurmaz.</p>}
        </div>
      </div>
    </Modal>
  );
}
