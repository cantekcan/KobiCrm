// Gönderim durumu deposu. submissionKey → { status, ettn, date, internalRef, ... }
// Amaç: ağ hatasında aynı anahtarla tekrar gelen istek yeni bir portal taslağı oluşturmasın (mükerrer belge önlemi).
import { existsSync, mkdirSync, readFileSync, renameSync, writeFileSync } from "node:fs";
import { dirname } from "node:path";

export class StateStore {
  #file; #data;
  constructor(file) {
    this.#file = file;
    this.#data = { drafts: {} };
    if (file && existsSync(file)) {
      try { this.#data = JSON.parse(readFileSync(file, "utf8")); } catch { throw new Error(`Durum dosyası okunamadı/bozuk: ${file}. Elle kontrol edin; üzerine yazılmadı.`); }
    }
  }
  get(key) { return this.#data.drafts[key]; }
  set(key, value) { this.#data.drafts[key] = { ...(this.#data.drafts[key] ?? {}), ...value, updatedAt: new Date().toISOString() }; this.#save(); return this.#data.drafts[key]; }
  delete(key) { delete this.#data.drafts[key]; this.#save(); }
  #save() {
    if (!this.#file) return; // bellek içi (test)
    mkdirSync(dirname(this.#file), { recursive: true });
    const tmp = `${this.#file}.tmp`;
    writeFileSync(tmp, JSON.stringify(this.#data, null, 2), { mode: 0o600 });
    renameSync(tmp, this.#file); // atomik
  }
}
