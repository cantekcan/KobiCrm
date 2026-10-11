import { useCallback } from "react";
import { useSearchParams } from "react-router-dom";

/**
 * Filtreleri URL'de tutar: sayfa yenilense ya da bağlantı paylaşılsa/"geri" denilse filtre korunur,
 * Genel Bakış kartlarından filtreli listeye gitmek de bununla çalışır.
 */
export function useUrlFilters<T extends Record<string, string>>(defaults: T) {
  const [params, setParams] = useSearchParams();
  const values = { ...defaults } as Record<string, string>;
  for (const k of Object.keys(defaults)) values[k] = params.get(k) ?? defaults[k];

  const set = useCallback((patch: Partial<Record<keyof T, string>>, resetPage = true) => {
    setParams((prev) => {
      const next = new URLSearchParams(prev);
      for (const [k, v] of Object.entries(patch)) {
        if (v === undefined || v === "" || v === defaults[k]) next.delete(k);
        else next.set(k, v as string);
      }
      if (resetPage && !("page" in patch)) next.delete("page");
      return next;
    }, { replace: true });
  }, [setParams]); // eslint-disable-line react-hooks/exhaustive-deps

  return [values as T, set] as const;
}
