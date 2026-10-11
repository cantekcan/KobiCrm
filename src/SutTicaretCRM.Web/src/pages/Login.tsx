import { FormEvent, useState } from "react";
import { api, auth, ApiError } from "../lib/api";
import { Button, Notice, TextInput, Icon } from "../components/ui";

export function Login({ onLoggedIn }: { onLoggedIn: () => void }) {
  const [userName, setUserName] = useState("");
  const [password, setPassword] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function submit(e: FormEvent) {
    e.preventDefault();
    setBusy(true);
    setError(null);
    try {
      const r = await api.post<{ token: string; userName: string }>("/auth/login", { userName, password });
      auth.set(r.token, r.userName);
      onLoggedIn();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "Giriş yapılamadı.");
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="login-wrap">
      <form className="card login-card stack" onSubmit={submit}>
        <div className="row"><span className="brand-logo" style={{ background: "var(--primary)", color: "#fff", width: 44, height: 44, borderRadius: 11, display: "grid", placeItems: "center" }}><Icon.box /></span>
          <div><h1>Süt Ticaret CRM</h1><div className="muted small">Müşteri, satış, fatura ve cari takibi</div></div></div>
        {error && <Notice tone="error">{error}</Notice>}
        <TextInput label="Kullanıcı adı" value={userName} onChange={(e) => setUserName(e.target.value)} autoComplete="username" autoFocus required />
        <TextInput label="Parola" type="password" value={password} onChange={(e) => setPassword(e.target.value)} autoComplete="current-password" required />
        <Button type="submit" loading={busy} className="block" disabled={!userName || !password}>Giriş yap</Button>
        <p className="muted small" style={{ margin: 0 }}>Parolanızı unuttuysanız uygulamayı kuran kişiden yardım isteyin (README → "Parola sıfırlama").</p>
      </form>
    </div>
  );
}
