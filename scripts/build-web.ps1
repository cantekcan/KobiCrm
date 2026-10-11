# Web arayüzünü derler ve çıktıyı API'nin wwwroot klasörüne kopyalar (tek adresten sunum için).
#   ./scripts/build-web.ps1
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$web = Join-Path $root 'src/SutTicaretCRM.Web'
$www = Join-Path $root 'src/SutTicaretCRM.Api/wwwroot'

Push-Location $web
try {
    if (-not (Test-Path 'node_modules')) { npm ci }
    npm run build
    if ($LASTEXITCODE -ne 0) { throw 'Web derlemesi başarısız.' }
} finally { Pop-Location }

if (Test-Path $www) { Remove-Item -Recurse -Force $www }
Copy-Item -Recurse (Join-Path $web 'dist') $www
Write-Host "Web arayüzü kopyalandı: $www" -ForegroundColor Green
