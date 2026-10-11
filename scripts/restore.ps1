<#
.SYNOPSIS
  Bir yedekten veritabanını geri yükler. DİKKAT: hedef veritabanındaki mevcut veri DEĞİŞTİRİLİR.
.DESCRIPTION
  - Önce yedek dosyasının bütünlüğü kontrol edilir.
  - Geri yüklemeden ÖNCE hedef veritabanının otomatik güvenlik yedeği alınır (backups/pre-restore-*.dump).
  - Onay için "EVET" yazmanız istenir (-Force ile atlanır).
  - Geri yüklerken uygulamayı (API) DURDURUN.
.EXAMPLE
  ./scripts/restore.ps1 -File backups/suttcrm-20261011-120000.dump
  # Üretim verisine dokunmadan denemek için ayrı bir veritabanına:
  ./scripts/restore.ps1 -File backups/suttcrm-20261011-120000.dump -Database suttcrm_restore_test -Force
#>
param(
  [Parameter(Mandatory)][string]$File,
  [string]$Database,
  [switch]$Force
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root
if (-not (Test-Path $File)) { throw "Yedek dosyası bulunamadı: $File" }
$leaf = Split-Path $File -Leaf
if ((Resolve-Path $File).Path -notlike "$((Resolve-Path backups).Path)*") { throw 'Yedek dosyası backups/ klasöründe olmalı (konteynere bu klasör bağlanır).' }

$cfg = @{}
if (Test-Path .env) { Get-Content .env | ForEach-Object { if ($_ -match '^\s*([A-Z_]+)\s*=\s*(.*)$') { $cfg[$Matches[1]] = $Matches[2].Trim() } } }
$user = if ($cfg.POSTGRES_USER) { $cfg.POSTGRES_USER } else { 'suttcrm' }
$main = if ($cfg.POSTGRES_DB) { $cfg.POSTGRES_DB } else { 'suttcrm' }
$target = if ($Database) { $Database } else { $main }

docker compose exec -T db pg_restore --list "/backups/$leaf" | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Yedek dosyası okunamıyor/bozuk. Geri yükleme yapılmadı.' }

if (-not $Force) {
  Write-Host "Hedef veritabanı: $target" -ForegroundColor Yellow
  Write-Host 'Bu işlem hedefteki MEVCUT VERİYİ yedekteki veriyle değiştirir.' -ForegroundColor Yellow
  $ans = Read-Host 'Devam etmek için EVET yazın'
  if ($ans -ne 'EVET') { Write-Host 'İptal edildi.'; exit 1 }
}

# Hedef yoksa oluştur
# SQL stdin ile verilir: Windows PowerShell 5.1 komut satırındaki çift tırnakları bozar.
$exists = "SELECT 1 FROM pg_database WHERE datname='$target'" | docker compose exec -T db psql -U $user -d postgres -tA
if (-not ($exists -match '1')) {
  ('CREATE DATABASE "' + $target + '"') | docker compose exec -T db psql -U $user -d postgres | Out-Null
  if ($LASTEXITCODE -ne 0) { throw 'Hedef veritabanı oluşturulamadı.' }
} else {
  # Güvenlik yedeği
  $pre = "pre-restore-$(Get-Date -Format 'yyyyMMdd-HHmmss').dump"
  docker compose exec -T db pg_dump -U $user -d $target -Fc -f "/backups/$pre"
  if ($LASTEXITCODE -ne 0) { throw 'Güvenlik yedeği alınamadı; geri yükleme durduruldu.' }
  Write-Host "Mevcut veri güvenlik yedeğine alındı: backups/$pre" -ForegroundColor Green
}

docker compose exec -T db pg_restore -U $user -d $target --clean --if-exists --no-owner "/backups/$leaf"
if ($LASTEXITCODE -ne 0) { Write-Warning 'pg_restore uyarılarla bitti (ilk kurulumda "does not exist" uyarıları normaldir). Sonucu kontrol edin.' }

$n = 'SELECT count(*) FROM "Customers"' | docker compose exec -T db psql -U $user -d $target -tA
Write-Host "Geri yükleme tamam. $target içinde müşteri sayısı: $($n.Trim())" -ForegroundColor Green
Write-Host 'Yüklenen dosyalar (dekont/fatura ekleri) için files-*.zip yedeğini storage/ klasörüne elle açın (README).'
