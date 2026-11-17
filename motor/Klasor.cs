using System.IO.Compression;
using System.Text;

namespace UrunMotor;

/// <summary>Hazır resim klasörünü (veya .zip) okur: kok/Kategori/[Pişme/[Menü/]]Ürün Adı.jpg</summary>
public static class Klasor
{
    static Klasor() { Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); }

    static bool Gizli(string ad) => ad.StartsWith("__MACOSX", StringComparison.Ordinal) || ad.StartsWith(".", StringComparison.Ordinal);

    /// <summary>Zip'i geçici klasöre açar (Türkçe adlı eski zip'ler cp857 okunur), açılan kökü döner.</summary>
    public static string ZipAc(string zip)
    {
        var gecici = Path.Combine(Path.GetTempPath(), "menu_" + Guid.NewGuid().ToString("N")[..10]);
        Directory.CreateDirectory(gecici);
        var kokTam = Path.GetFullPath(gecici) + Path.DirectorySeparatorChar;
        // .NET, UTF-8 bayrağı olmayan eski Windows zip'lerinde de adları tek kodlamayla okur:
        // adı önce UTF-8 dene, geçersizse (Türkçe Windows zip'i) cp857 ile oku.
        using (var z = new ZipArchive(File.OpenRead(zip), ZipArchiveMode.Read, false, new UTF8Encoding(false, false)))
        using (var z857 = new ZipArchive(File.OpenRead(zip), ZipArchiveMode.Read, false, Encoding.GetEncoding(857)))
        {
            for (int i = 0; i < z.Entries.Count; i++)
            {
                var e = z.Entries[i];
                var ad = e.FullName.Contains('\uFFFD') && i < z857.Entries.Count ? z857.Entries[i].FullName : e.FullName;
                ad = ad.Replace('\\', '/');
                var hedef = Path.GetFullPath(Path.Combine(gecici, ad.Replace('/', Path.DirectorySeparatorChar)));
                if (!hedef.StartsWith(kokTam, StringComparison.OrdinalIgnoreCase)) continue;
                if (ad.EndsWith("/", StringComparison.Ordinal)) { Directory.CreateDirectory(hedef); continue; }
                Directory.CreateDirectory(Path.GetDirectoryName(hedef));
                e.ExtractToFile(hedef, true);
            }
        }
        var icerik = Directory.EnumerateFileSystemEntries(gecici).Select(Path.GetFileName).Where(x => !Gizli(x)).ToList();
        if (icerik.Count == 1 && Directory.Exists(Path.Combine(gecici, icerik[0]))) return Path.Combine(gecici, icerik[0]);
        return gecici;
    }

    public sealed record Tarama(List<Kategori> Kategoriler, Dictionary<string, string> Gercek);

    public static Tarama Tara(string kok)
    {
        if (kok.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) kok = ZipAc(kok);
        var kategoriler = new List<Kategori>();
        var gercek = new Dictionary<string, string>();
        void Yuru(string klasor)
        {
            var dosyalar = Directory.GetFiles(klasor).Select(Path.GetFileName).OrderBy(x => x, StringComparer.Ordinal)
                .Where(d => Resim.ResimMi(d) && !d.StartsWith(".", StringComparison.Ordinal)).ToList();
            if (dosyalar.Count > 0)
            {
                var goreli = Path.GetRelativePath(kok, klasor);
                var parcalar = goreli == "." ? new List<string>() :
                    goreli.Split(Path.DirectorySeparatorChar).Select(p => { var e = Metin.Emojisiz(p); return e.Length > 0 ? e : "Genel"; }).ToList();
                var yol = parcalar.Count > 0 ? string.Join("/", parcalar) : "Genel";
                var kat = kategoriler.FirstOrDefault(k => k.Yol == yol);
                if (kat == null) { kat = new Kategori { Yol = yol }; kategoriler.Add(kat); }
                gercek[yol] = klasor;
                foreach (var d in dosyalar)
                {
                    var adHam = d.Substring(0, d.Length - Path.GetExtension(d).Length);
                    var c = Metin.SatiriCoz(adHam);
                    var ad = Metin.Emojisiz(c.Ad);
                    if (ad.Length > 0 && !kat.Urunler.Any(u => Py.Lower(u.Ad) == Py.Lower(ad)))
                        kat.Urunler.Add(new Urun { Ad = ad, Arama = adHam, Fiyat = c.Fiyat });
                }
            }
            foreach (var alt in Directory.GetDirectories(klasor).Select(Path.GetFileName).Where(a => !Gizli(a)).OrderBy(x => x, StringComparer.Ordinal))
                Yuru(Path.Combine(klasor, alt));
        }
        Yuru(kok);
        foreach (var k in kategoriler) k.Urunler = Metin.PorsiyonGrupla(k.Urunler);
        return new Tarama(kategoriler, gercek);
    }

    /// <summary>Menü okuma için verilen yollardaki resim/PDF dosyalarını toplar.</summary>
    public static List<string> MenuDosyalari(IEnumerable<string> yollar)
    {
        var l = new List<string>();
        bool Uygun(string a) => Resim.ResimMi(a) || a.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);
        foreach (var y in yollar)
        {
            if (Directory.Exists(y))
                l.AddRange(Directory.EnumerateFiles(y, "*", SearchOption.AllDirectories).Where(Uygun)
                    .OrderBy(x => Path.GetDirectoryName(x), StringComparer.Ordinal).ThenBy(x => Path.GetFileName(x), StringComparer.Ordinal));
            else if (Uygun(y)) l.Add(y);
        }
        return l;
    }
}
