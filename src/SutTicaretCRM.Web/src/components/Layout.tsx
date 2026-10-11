import { useState } from "react";
import { NavLink, Outlet, useLocation, Link } from "react-router-dom";
import { useQuery } from "@tanstack/react-query";
import { api, auth } from "../lib/api";
import type { Settings } from "../lib/types";
import { Icon } from "./ui";

const items = [
  { to: "/", label: "Genel Bakış", icon: Icon.home, end: true },
  { to: "/musteriler", label: "Müşteriler", icon: Icon.users },
  { to: "/urunler", label: "Ürünler", icon: Icon.box },
  { to: "/satislar", label: "Satışlar", icon: Icon.cart },
  { to: "/faturalar", label: "Faturalar", icon: Icon.file },
  { to: "/cari", label: "Cari Hesaplar", icon: Icon.book },
  { to: "/tahsilatlar", label: "Tahsilatlar", icon: Icon.cash },
  { to: "/stok", label: "Stok", icon: Icon.stock },
  { to: "/tedarikciler", label: "Tedarikçiler", icon: Icon.truck },
  { to: "/raporlar", label: "Raporlar", icon: Icon.chart },
  { to: "/ayarlar", label: "Ayarlar", icon: Icon.gear },
];
const bottom = [items[0], items[1], items[3], items[6]];

export function Layout({ onLogout }: { onLogout: () => void }) {
  const [sheet, setSheet] = useState(false);
  const loc = useLocation();
  const { data: settings } = useQuery({ queryKey: ["settings"], queryFn: () => api.get<Settings>("/settings"), staleTime: 60_000 });
  const demo = settings?.databaseMode === "Demo";
  const company = settings?.company.name?.trim() || "Süt Ticaret CRM";

  return (
    <div className="app">
      <aside className="sidebar" aria-label="Ana menü">
        <div className="brand"><span className="brand-logo"><Icon.box size={20} /></span><span>{company}</span></div>
        <nav className="nav">
          {items.map((i) => <NavLink key={i.to} to={i.to} end={i.end}><i.icon />{i.label}</NavLink>)}
        </nav>
        <div className="sidebar-footer">
          <div>Giriş: <strong>{auth.user}</strong></div>
          <button className="nav nav-btn" style={{ display: "flex", color: "#cfe2ef", background: "none", border: 0, padding: ".6rem 0", cursor: "pointer", gap: ".5rem" }} onClick={onLogout}><Icon.out size={18} /> Çıkış yap</button>
        </div>
      </aside>

      <div className="topbar"><span className="brand"><span className="brand-logo"><Icon.box size={18} /></span><span>{company}</span></span></div>

      <main className="main">
                <div className="page">
          {demo && <div className="demo-banner" role="status" style={{ borderRadius: 10, marginBottom: "1rem" }}>DEMO VERİTABANI — burada görünen kayıtlar örnektir, gerçek değildir.</div>}
          <Outlet key={loc.pathname.split("/")[1]} />
        </div>
      </main>

      <nav className="bottom-nav" aria-label="Hızlı menü">
        {bottom.map((i) => <NavLink key={i.to} to={i.to} end={i.end}><i.icon />{i.label.replace("Genel Bakış", "Genel")}</NavLink>)}
        <button onClick={() => setSheet(true)}><Icon.menu />Menü</button>
      </nav>

      {sheet && (
        <div className="menu-sheet" onClick={() => setSheet(false)}>
          <div onClick={(e) => e.stopPropagation()}>
            <div className="row-between" style={{ marginBottom: ".5rem" }}><strong>Menü</strong><button className="btn ghost sm" onClick={() => setSheet(false)} aria-label="Kapat"><Icon.x size={20} /></button></div>
            {items.map((i) => <Link key={i.to} to={i.to} onClick={() => setSheet(false)}><i.icon />{i.label}</Link>)}
            <a href="#" onClick={(e) => { e.preventDefault(); onLogout(); }}><Icon.out />Çıkış yap ({auth.user})</a>
          </div>
        </div>
      )}
    </div>
  );
}
