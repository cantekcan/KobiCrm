<#
.SYNOPSIS
  PostgreSQL veritabanını ve yüklenen dosyaları (dekont, fatura PDF/XML) yedekler.
.DESCRIPTION
  - Veritabanı: docker compose içindeki 'db' konteynerinde pg_dump (özel biçim, sıkıştırılmış) -> backups/suttcrm-<tarih>.dump
  - Dosyalar:   storage/ klasörü (yerel çalıştırma) -> backups/files-<tarih>.zip
                Docker'daki API birimi için -DockerFiles kullanın.
  - Yedek alındıktan sonra bütünlüğü doğrulanır (pg_restore --list). Son -Keep adet yedek tutulur.
  Yedek dosyaları müşteri ve vergi bilgisi içerir: gizli tutun, bilgisayar DIŞINDA da (harici disk/şifreli bulut) saklayın.
.EXAMPLE
  ./scripts/backup.ps1
  ./scripts/backup.ps1 -Keep 30 -DockerFiles
#>
param(
  [string]$Database,
  [int]$Keep = 14,
  [switch]$DockerFiles,
  [string]$StorageDir = 'src/SutTicaretCRM.Api/storage'
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

# .env oku (yalnızca POSTGRES_* anahtarları)
$cfg = @{}
if (Test-Path .env) { Get-Content .env | ForEach-Object { if ($_ -match '^\s*([A-Z_]+)\s*=\s*(.*)$') { $cfg[$Matches[1]] = $Matches[2].Trim() } } }
$user = if ($cfg.POSTGRES_USER) { $cfg.POSTGRES_USER } else { 'suttcrm' }
$db   = if ($Database) { $Database } elseif ($cfg.POSTGRES_DB) { $cfg.POSTGRES_DB } else { 'suttcrm' }

New-Item -ItemType Directory -Force backups | Out-Null
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$dump = "suttcrm-$stamp.dump"

Write-Host "Veritabanı yedekleniyor: $db -> backups/$dump"
docker compose exec -T db pg_dump -U $user -d $db -Fc -f "/backups/$dump"
if ($LASTEXITCODE -ne 0) { throw 'pg_dump başarısız. Veritabanı konteyneri çalışıyor mu? (docker compose up -d db)' }

# Bütünlük doğrulaması: arşiv okunabilir mi ve beklenen tablolar var mı?
$list = docker compose exec -T db pg_restore --list "/backups/$dump"
if ($LASTEXITCODE -ne 0 -or -not ($list -match 'TABLE DATA public Customers')) { throw "Yedek doğrulanamadı: backups/$dump (silmeyin, inceleyin)." }
$size = [math]::Round((Get-Item "backups/$dump").Length / 1KB)
Write-Host "  OK ($size KB, doğrulandı)" -ForegroundColor Green

# Dosyalar
if ($DockerFiles) {
  Write-Host 'Docker birimindeki dosyalar yedekleniyor (suttcrm-files)...'
  $vol = (docker volume ls --format '{{.Name}}' | Where-Object { $_ -like '*suttcrm-files' } | Select-Object -First 1)
  if ($vol) {
    docker run --rm -v "${vol}:/data:ro" -v "${PWD}/backups:/backups" alpine tar czf "/backups/files-$stamp.tar.gz" -C /data .
    if ($LASTEXITCODE -ne 0) { throw 'Dosya yedeği başarısız.' }
    Write-Host "  OK backups/files-$stamp.tar.gz" -ForegroundColor Green
  } else { Write-Warning 'suttcrm-files birimi bulunamadı; dosya yedeği atlandı.' }
} elseif (Test-Path $StorageDir) {
  Compress-Archive -Path (Join-Path $StorageDir '*') -DestinationPath "backups/files-$stamp.zip" -Force
  Write-Host "Dosyalar yedeklendi: backups/files-$stamp.zip" -ForegroundColor Green
} else {
  Write-Host "Yüklenmiş dosya klasörü yok ($StorageDir); dosya yedeği atlandı."
}

# Eski yedekleri temizle (yalnızca bu betiğin adlandırdığı dosyalar)
foreach ($pattern in 'suttcrm-*.dump', 'files-*.zip', 'files-*.tar.gz') {
  Get-ChildItem backups -Filter $pattern | Sort-Object LastWriteTime -Descending | Select-Object -Skip $Keep | Remove-Item -Force
}
Write-Host "Tamam. Yedekleri bilgisayar dışında da saklayın." -ForegroundColor Cyan
