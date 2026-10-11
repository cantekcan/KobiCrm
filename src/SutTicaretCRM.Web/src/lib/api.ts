// Tek bir yerden API erişimi. Token tarayıcıda saklanır; sunucu her istekte doğrular.

const TOKEN_KEY = "sct_token";
const USER_KEY = "sct_user";

export const auth = {
  get token() { try { return localStorage.getItem(TOKEN_KEY); } catch { return null; } },
  get user() { try { return localStorage.getItem(USER_KEY); } catch { return null; } },
  set(token: string, user: string) { try { localStorage.setItem(TOKEN_KEY, token); localStorage.setItem(USER_KEY, user); } catch { /* özel pencere */ } },
  clear() { try { localStorage.removeItem(TOKEN_KEY); localStorage.removeItem(USER_KEY); } catch { /* yoksay */ } },
};

export class ApiError extends Error {
  status: number;
  fieldErrors: Record<string, string[]>;
  constructor(status: number, message: string, fieldErrors: Record<string, string[]> = {}) {
    super(message);
    this.status = status;
    this.fieldErrors = fieldErrors;
  }
  /** Alan adı için ilk hata mesajı (büyük/küçük harf duyarsız, "items[0]" gibi indeksli alanlar dahil). */
  field(name: string): string | undefined {
    const key = Object.keys(this.fieldErrors).find((k) => k.toLowerCase() === name.toLowerCase());
    return key ? this.fieldErrors[key][0] : undefined;
  }
}

type Query = Record<string, string | number | boolean | undefined | null>;

function qs(q?: Query) {
  if (!q) return "";
  const p = new URLSearchParams();
  for (const [k, v] of Object.entries(q)) if (v !== undefined && v !== null && v !== "") p.set(k, String(v));
  const s = p.toString();
  return s ? `?${s}` : "";
}

async function request<T>(method: string, path: string, body?: unknown, query?: Query, form?: FormData): Promise<T> {
  const headers: Record<string, string> = {};
  if (auth.token) headers.Authorization = `Bearer ${auth.token}`;
  if (body !== undefined) headers["Content-Type"] = "application/json";
  let res: Response;
  try {
    res = await fetch(`/api${path}${qs(query)}`, { method, headers, body: form ?? (body !== undefined ? JSON.stringify(body) : undefined) });
  } catch {
    throw new ApiError(0, "Sunucuya ulaşılamadı. İnternet/yerel ağ bağlantınızı ve uygulamanın çalıştığını kontrol edin. İşlem kaydedilmemiş olabilir; tekrar denemeden önce listeyi kontrol edin.");
  }
  if (res.status === 401 && path !== "/auth/login") {
    auth.clear();
    window.dispatchEvent(new Event("auth:logout"));
    throw new ApiError(401, "Oturum süresi doldu. Lütfen yeniden giriş yapın.");
  }
  if (res.status === 204) return undefined as T;
  const text = await res.text();
  let data: any = null;
  try { data = text ? JSON.parse(text) : null; } catch { /* JSON değil */ }
  if (!res.ok) {
    const msg = data?.title ?? (res.status === 429 ? "Çok fazla deneme. Biraz bekleyin." : res.status === 413 ? "Dosya çok büyük." : `Beklenmeyen hata (${res.status}).`);
    throw new ApiError(res.status, msg, data?.errors ?? {});
  }
  return data as T;
}

export const api = {
  get: <T,>(path: string, query?: Query) => request<T>("GET", path, undefined, query),
  post: <T,>(path: string, body?: unknown) => request<T>("POST", path, body ?? {}),
  put: <T,>(path: string, body?: unknown) => request<T>("PUT", path, body ?? {}),
  del: <T,>(path: string) => request<T>("DELETE", path),
  upload: <T,>(path: string, file: File) => {
    const f = new FormData();
    f.append("file", file);
    return request<T>("POST", path, undefined, undefined, f);
  },
  /** Yetkili dosya indirme (Authorization başlığı gerektiği için fetch + blob). */
  async download(path: string, fallbackName: string) {
    const res = await fetch(`/api${path}`, { headers: auth.token ? { Authorization: `Bearer ${auth.token}` } : {} });
    if (!res.ok) throw new ApiError(res.status, "Dosya indirilemedi.");
    const blob = await res.blob();
    const url = URL.createObjectURL(blob);
    const a = document.createElement("a");
    a.href = url;
    a.download = fallbackName;
    a.click();
    setTimeout(() => URL.revokeObjectURL(url), 10_000);
  },
};
