import { FormEvent, useEffect, useState } from "react";
import { useNavigate, useParams } from "react-router-dom";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api, ApiError } from "../lib/api";
import type { CustomerDetail, CustomerForm as CF } from "../lib/types";
import { L } from "../lib/types";
import { Button, Card, Check, ErrorBlock, FormError, NumberInput, Notice, PageHeader, Select, Spinner, TextArea, TextInput, useToast } from "../components/ui";
import { parseDecimal, todayIso } from "../lib/format";

const empty: CF = { name: "", customerType: "Company", taxOffice: "", taxNumber: "", nationalId: "", phone: "", email: "", address: "", city: "", district: "", paymentTermDays: 0, eInvoiceStatus: "Unknown", note: "", isActive: true };

export function CustomerFormPage() {
  const { id } = useParams();
  const editing = !!id;
  const nav = useNavigate();
  const qc = useQueryClient();
  const toast = useToast();
  const [form, setForm] = useState<CF>(empty);
  const [withOpening, setWithOpening] = useState(false);
  const [opening, setOpening] = useState({ amount: "", date: todayIso(), dueDate: "", description: "Geçmiş dönemden devreden müşteri borcu" });

  const { data, isLoading, error } = useQuery({ queryKey: ["customer", id], queryFn: () => api.get<CustomerDetail>(`/customers/${id}`), enabled: editing });
  useEffect(() => {
    if (data) setForm({ name: data.name, customerType: data.customerType, taxOffice: data.taxOffice ?? "", taxNumber: data.taxNumber ?? "", nationalId: data.nationalId ?? "", phone: data.phone ?? "", email: data.email ?? "", address: data.address ?? "", city: data.city ?? "", district: data.district ?? "", paymentTermDays: data.paymentTermDays, eInvoiceStatus: data.eInvoiceStatus, note: data.note ?? "", isActive: data.isActive });
  }, [data]);

  const up = <K extends keyof CF>(k: K, v: CF[K]) => setForm((f) => ({ ...f, [k]: v }));
  const amount = parseDecimal(opening.amount);

  const save = useMutation({
    mutationFn: () => {
      const body: any = { ...form, taxOffice: form.taxOffice || null, taxNumber: form.taxNumber || null, nationalId: form.nationalId || null, phone: form.phone || null, email: form.email || null, address: form.address || null, city: form.city || null, district: form.district || null, note: form.note || null };
      if (!editing && withOpening && amount) body.openingBalance = { amount, date: opening.date, dueDate: opening.dueDate || null, description: opening.description || null };
      return editing ? api.put<CustomerDetail>(`/customers/${id}`, body) : api.post<CustomerDetail>("/customers", body);
    },
    onSuccess: (c) => { qc.invalidateQueries(); toast.success(editing ? "Müşteri bilgileri güncellendi." : withOpening && amount ? `Müşteri kaydedildi. ${opening.amount} TL açılış bakiyesi cari hesaba işlendi.` : "Müşteri kaydedildi."); nav(`/musteriler/${c.id}`); },
  });
  const err = save.error instanceof ApiError ? save.error : null;

  if (editing && isLoading) return <Spinner />;
  if (editing && error) return <ErrorBlock error={error} />;

  const submit = (e: FormEvent) => { e.preventDefault(); save.mutate(); };

  return (
    <form className="stack" onSubmit={submit}>
      <PageHeader title={editing ? "Müşteriyi düzenle" : "Yeni müşteri"} back={{ to: editing ? `/musteriler/${id}` : "/musteriler", label: editing ? "Müşteri" : "Müşteriler" }} />
      <Card title="Müşteri bilgileri">
        <div className="form-grid">
          <TextInput className="span-2" label="Firma adı veya ad soyad" required value={form.name} onChange={(e) => up("name", e.target.value)} error={err?.field("name")} autoFocus />
          <Select label="Müşteri tipi" value={form.customerType} onChange={(e) => up("customerType", e.target.value as any)}>
            <option value="Company">{L.customerType.Company}</option><option value="Individual">{L.customerType.Individual}</option>
          </Select>
          <TextInput label="Telefon" type="tel" inputMode="tel" value={form.phone} onChange={(e) => up("phone", e.target.value)} />
          <TextInput label="Vergi dairesi" value={form.taxOffice} onChange={(e) => up("taxOffice", e.target.value)} />
          <TextInput label="Vergi kimlik no (10 hane)" inputMode="numeric" maxLength={10} value={form.taxNumber} onChange={(e) => up("taxNumber", e.target.value.replace(/\D/g, ""))} error={err?.field("taxNumber")} hint="Fatura kesmek için gerekir; şimdi boş bırakabilirsiniz." />
          {form.customerType === "Individual" && <TextInput label="T.C. kimlik no (11 hane)" inputMode="numeric" maxLength={11} value={form.nationalId} onChange={(e) => up("nationalId", e.target.value.replace(/\D/g, ""))} error={err?.field("nationalId")} />}
          <TextInput label="E-posta" type="email" value={form.email} onChange={(e) => up("email", e.target.value)} error={err?.field("email")} />
          <TextInput className="span-2" label="Adres" value={form.address} onChange={(e) => up("address", e.target.value)} />
          <TextInput label="İl" value={form.city} onChange={(e) => up("city", e.target.value)} />
          <TextInput label="İlçe" value={form.district} onChange={(e) => up("district", e.target.value)} />
          <NumberInput label="Ödeme vadesi (gün)" value={String(form.paymentTermDays)} onChange={(v) => up("paymentTermDays", Math.max(0, Math.round(parseDecimal(v) ?? 0)))} hint="Vadeli satışlarda vade tarihi bu kadar gün sonrası olarak önerilir. Peşin çalışıyorsa 0." />
          <Select label="e-Fatura mükellefiyeti" value={form.eInvoiceStatus} onChange={(e) => up("eInvoiceStatus", e.target.value as any)} hint="Bilmiyorsanız 'Bilinmiyor' bırakın. Fatura hazırlarken portaldan sorgulamanız istenir.">
            {Object.entries(L.einvoice).map(([k, v]) => <option key={k} value={k}>{v}</option>)}
          </Select>
          <TextArea className="span-2" label="Açıklama / not" value={form.note} onChange={(e) => up("note", e.target.value)} />
          <div className="span-2"><Check checked={form.isActive} onChange={(v) => up("isActive", v)}>Aktif müşteri (pasif müşteriye yeni satış yapılamaz)</Check></div>
        </div>
      </Card>

      {!editing && (
        <Card title="Geçmişten gelen borç (açılış bakiyesi)">
          <div className="stack-sm">
            <Check checked={withOpening} onChange={setWithOpening}>Bu müşterinin uygulamaya geçmeden önceki borcu var</Check>
            {withOpening && (
              <>
                <Notice tone="info">Bu tutar <strong>cari hesaba “Açılış bakiyesi” olarak</strong> işlenir. Fatura veya satış oluşturulmaz, satış raporlarına yeni satış gibi girmez. Her müşteri için yalnızca bir kez girilebilir.</Notice>
                <div className="form-grid">
                  <NumberInput label="Açılış borcu (TL)" required value={opening.amount} onChange={(v) => setOpening({ ...opening, amount: v })} hint="Müşterinin bize olan borcu. Örn: 100.000" error={err?.field("openingBalance")} />
                  <TextInput label="Açılış tarihi" type="date" required value={opening.date} onChange={(e) => setOpening({ ...opening, date: e.target.value })} hint="Borcun devredildiği tarih (örn. uygulamaya geçiş günü)." />
                  <TextInput label="Vade tarihi (isteğe bağlı)" type="date" value={opening.dueDate} onChange={(e) => setOpening({ ...opening, dueDate: e.target.value })} hint="Boş bırakırsanız açılış tarihi kabul edilir; borç hemen 'vadesi geçmiş' sayılır." />
                  <TextInput label="Açıklama" value={opening.description} onChange={(e) => setOpening({ ...opening, description: e.target.value })} />
                </div>
              </>
            )}
          </div>
        </Card>
      )}

      <FormError error={save.error} />
      <div className="form-actions">
        <Button type="submit" loading={save.isPending} disabled={!form.name.trim() || (withOpening && !editing && (!amount || amount === 0))}>Kaydet</Button>
        <Button variant="secondary" onClick={() => nav(-1)}>Vazgeç</Button>
      </div>
    </form>
  );
}
