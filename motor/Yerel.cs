using System.Text.RegularExpressions;

namespace UrunMotor;

/// <summary>
/// Kullanıcının kendi menü resimleri arşivinde (alt klasörler dahil) dosya adına göre arama.
/// Adı birebir tutan resim internetten önce, benzer adlı resim internet bulamazsa kullanılır.
/// </summary>
public static class Yerel
{
    sealed record Kayit(string Yol, string[] Kelimeler, string Klasor);
    sealed class Dizin { public DateTime Zaman; public List<Kayit> Kayitlar; }

    static readonly Dictionary<string, Dizin> Onbellek = new(StringComparer.OrdinalIgnoreCase);
    static readonly object Kilit = new();
    static readonly Regex HarfDisi = new("[^a-z0-9]+", RegexOptions.CultureInvariant);
    static readonly Regex KopyaEki = new(@"(\s|_|-)*(\(\d+\)|kopya|copy|\d{1,2})$", RegexOptions.CultureInvariant);
    static readonly HashSet<string> Bos = new() { "ve", "ile", "adet", "porsiyon", "pors", "jpg", "png", "resim", "foto", "img", "image" };

    public static string[] Kelimeler(string metin)
    {
        var n = HarfDisi.Replace(Metin.Norm(metin ?? ""), " ").Trim();
        n = KopyaEki.Replace(n, "").Trim();
        return Py.SplitWs(n).Where(k => k.Length >= 2 && !Bos.Contains(k)).Distinct().ToArray();
    }

    static List<Kayit> Hazirla(string kok, Action<string> log)
    {
        lock (Kilit)
        {
            if (Onbellek.TryGetValue(kok, out var d) && DateTime.UtcNow - d.Zaman < TimeSpan.FromMinutes(15)) return d.Kayitlar;
        }
        var liste = new List<Kayit>();
        var secenek = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.System };
        foreach (var yol in Directory.EnumerateFiles(kok, "*", secenek))
        {
            if (!Resim.ResimMi(yol)) continue;
            var k = Kelimeler(Path.GetFileNameWithoutExtension(yol));
            if (k.Length == 0) continue;
            liste.Add(new Kayit(yol, k, Metin.Norm(Path.GetRelativePath(kok, Path.GetDirectoryName(yol) ?? kok))));
        }
        log?.Invoke($"   (Kendi resim arşivinde {liste.Count} resim tarandı)");
        lock (Kilit) Onbellek[kok] = new Dizin { Zaman = DateTime.UtcNow, Kayitlar = liste };
        return liste;
    }

    // "ayran" ile "ayranı", "kola" ile "kolası" gibi ekleri tolere et
    static bool Benzer(string a, string b) =>
        a == b || (Math.Min(a.Length, b.Length) >= 4 && (a.StartsWith(b, StringComparison.Ordinal) || b.StartsWith(a, StringComparison.Ordinal)));

    public sealed record Eslesme(string Yol, double Puan, bool Birebir);

    /// <summary>Ürün adına en çok benzeyen resim. Bulunamazsa null.</summary>
    public static Eslesme Bul(string kok, string ad, string kategoriYolu, Action<string> log = null)
    {
        if (string.IsNullOrWhiteSpace(kok) || !Directory.Exists(kok)) return null;
        var p = Kelimeler(ad);
        if (p.Length == 0) return null;
        var baglam = Kelimeler((kategoriYolu ?? "").Replace('/', ' '));
        Eslesme en = null;
        foreach (var k in Hazirla(kok, log))
        {
            int ortak = p.Count(x => k.Kelimeler.Any(y => Benzer(x, y)));
            if (ortak == 0) continue;
            double kesinlik = (double)ortak / p.Length;            // ürün kelimelerinin kaçı dosya adında
            double kapsam = (double)ortak / k.Kelimeler.Length;     // dosya adının ne kadarı ürünle ilgili
            double puan = 0.7 * kesinlik + 0.3 * kapsam;
            if (baglam.Any(b => k.Klasor.Contains(b))) puan += 0.05;
            bool birebir = kesinlik == 1 && kapsam == 1;
            if (en == null || puan > en.Puan || (puan == en.Puan && k.Yol.Length < en.Yol.Length))
                en = new Eslesme(k.Yol, puan, birebir);
        }
        if (en == null) return null;
        // tüm ürün kelimeleri geçmeli (tek kelimelik üründe "Ayran" → "Cam Şişe Ayran" olur, "Kola" → "Kola Kutu" olur)
        return en.Puan >= 0.75 ? en : null;
    }

    public static void Temizle() { lock (Kilit) Onbellek.Clear(); }
}
