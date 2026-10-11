namespace SutTicaretCRM.Api;

/// <summary>
/// Geliştirme kolaylığı: depo kökündeki .env dosyasını (git'e eklenmez) okuyup yapılandırmaya eşler.
/// Ortam değişkeni/ayar zaten tanımlıysa ona dokunmaz. Docker'da bu dosya kullanılmaz; değerler ortam değişkeniyle gelir.
/// </summary>
public static class EnvFile
{
    public static void LoadInto(ConfigurationManager config)
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        for (var i = 0; i < 5 && dir is not null; i++, dir = dir.Parent)
        {
            var path = Path.Combine(dir.FullName, ".env");
            if (!File.Exists(path)) continue;

            var env = new Dictionary<string, string>();
            foreach (var raw in File.ReadAllLines(path))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith('#')) continue;
                var idx = line.IndexOf('=');
                if (idx > 0) env[line[..idx].Trim()] = line[(idx + 1)..].Trim();
            }

            void Map(string key, string setting)
            {
                if (env.TryGetValue(key, out var v) && string.IsNullOrEmpty(config[setting])) config[setting] = v;
            }

            if (string.IsNullOrEmpty(config["ConnectionStrings:Default"]) && env.TryGetValue("POSTGRES_PASSWORD", out var pw))
            {
                var db = env.GetValueOrDefault("POSTGRES_DB", "suttcrm");
                var user = env.GetValueOrDefault("POSTGRES_USER", "suttcrm");
                var port = env.GetValueOrDefault("POSTGRES_PORT", "5433");
                config["ConnectionStrings:Default"] = $"Host=localhost;Port={port};Database={db};Username={user};Password={pw}";
            }
            Map("JWT_KEY", "Jwt:Key");
            Map("ADMIN_USERNAME", "Seed:AdminUserName");
            Map("ADMIN_PASSWORD", "Seed:AdminPassword");
            return;
        }
    }
}
