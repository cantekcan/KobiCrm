import { useEffect, useState } from "react";
import { Navigate, Route, Routes } from "react-router-dom";
import { useQueryClient } from "@tanstack/react-query";
import { auth } from "./lib/api";
import { Layout } from "./components/Layout";
import { Login } from "./pages/Login";
import { Dashboard } from "./pages/Dashboard";
import { CustomersPage } from "./pages/Customers";
import { CustomerFormPage } from "./pages/CustomerForm";
import { CustomerDetailPage } from "./pages/CustomerDetail";
import { ProductsPage, ProductFormPage } from "./pages/Products";
import { SalesPage } from "./pages/Sales";
import { SaleFormPage } from "./pages/SaleForm";
import { SaleDetailPage } from "./pages/SaleDetail";
import { InvoicesPage } from "./pages/Invoices";
import { InvoiceDetailPage } from "./pages/InvoiceDetail";
import { LedgerPage, StatementPage } from "./pages/Ledger";
import { PaymentsPage, PaymentFormPage, PaymentDetailPage } from "./pages/Payments";
import { StockPage } from "./pages/Stock";
import { SuppliersPage, SupplierFormPage, SupplierDetailPage, PurchaseFormPage } from "./pages/Suppliers";
import { ReportsPage } from "./pages/Reports";
import { SettingsPage } from "./pages/Settings";

export default function App() {
  const [token, setToken] = useState(auth.token);
  const qc = useQueryClient();

  useEffect(() => {
    const h = () => { setToken(null); qc.clear(); };
    window.addEventListener("auth:logout", h);
    return () => window.removeEventListener("auth:logout", h);
  }, [qc]);

  if (!token) return <Login onLoggedIn={() => setToken(auth.token)} />;
  const logout = () => { auth.clear(); setToken(null); qc.clear(); };

  return (
    <Routes>
      <Route element={<Layout onLogout={logout} />}>
        <Route index element={<Dashboard />} />
        <Route path="musteriler" element={<CustomersPage />} />
        <Route path="musteriler/yeni" element={<CustomerFormPage />} />
        <Route path="musteriler/:id" element={<CustomerDetailPage />} />
        <Route path="musteriler/:id/duzenle" element={<CustomerFormPage />} />
        <Route path="urunler" element={<ProductsPage />} />
        <Route path="urunler/yeni" element={<ProductFormPage />} />
        <Route path="urunler/:id" element={<ProductFormPage />} />
        <Route path="satislar" element={<SalesPage />} />
        <Route path="satislar/yeni" element={<SaleFormPage />} />
        <Route path="satislar/:id" element={<SaleDetailPage />} />
        <Route path="satislar/:id/duzenle" element={<SaleFormPage />} />
        <Route path="faturalar" element={<InvoicesPage />} />
        <Route path="faturalar/:id" element={<InvoiceDetailPage />} />
        <Route path="cari" element={<LedgerPage />} />
        <Route path="cari/musteri/:id" element={<StatementPage kind="customer" />} />
        <Route path="cari/tedarikci/:id" element={<StatementPage kind="supplier" />} />
        <Route path="tahsilatlar" element={<PaymentsPage />} />
        <Route path="tahsilatlar/yeni" element={<PaymentFormPage />} />
        <Route path="tahsilatlar/:id" element={<PaymentDetailPage />} />
        <Route path="stok" element={<StockPage />} />
        <Route path="tedarikciler" element={<SuppliersPage />} />
        <Route path="tedarikciler/yeni" element={<SupplierFormPage />} />
        <Route path="tedarikciler/:id" element={<SupplierDetailPage />} />
        <Route path="tedarikciler/:id/duzenle" element={<SupplierFormPage />} />
        <Route path="alislar/yeni" element={<PurchaseFormPage />} />
        <Route path="raporlar" element={<ReportsPage />} />
        <Route path="ayarlar" element={<SettingsPage />} />
        <Route path="*" element={<Navigate to="/" replace />} />
      </Route>
    </Routes>
  );
}
