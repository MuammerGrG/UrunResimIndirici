using System.Text;
using System.Text.RegularExpressions;

namespace UrunMotor;

public sealed class Sutun
{
    public string Ad, Tip, Default;
    public bool NotNull, Auto;
}

/// <summary>Python dict gibi ekleme sırasını koruyan sözlük (log çıktısı birebir olsun diye).</summary>
public sealed class SiraliSozluk
{
    public readonly List<string> Anahtarlar = new();
    readonly Dictionary<string, string> _d = new();
    public string this[string k]
    {
        get => _d.TryGetValue(k, out var v) ? v : null;
        set { if (!_d.ContainsKey(k)) Anahtarlar.Add(k); _d[k] = value; }
    }
    public bool Var(string k) => _d.ContainsKey(k);
    public IEnumerable<KeyValuePair<string, string>> Ciftler => Anahtarlar.Select(k => new KeyValuePair<string, string>(k, _d[k]));
    public static string Repr(string s) => s == null ? "None" : "'" + s.Replace("\\", "\\\\").Replace("'", "\\'") + "'";
    public override string ToString() => "{" + string.Join(", ", Anahtarlar.Select(k => $"{Repr(k)}: {Repr(_d[k])}")) + "}";
}

/// <summary>POS (MySQL 5.5 / MySQL-Front) SQL yedeğini doldurur.</summary>
public static class Sql
{
    public const string KategoriTablo = "tbl_urunkategori";
    public const string KatCarpanTablo = "tbl_urunkategoricarpan";
    public const string UrunCarpanTablo = "tbl_uruncarpan";
    public const string UrunTablo = "tbl_urun";

    static Sql() { Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); }

    const RegexOptions CI = RegexOptions.CultureInvariant;
    const RegexOptions CII = RegexOptions.CultureInvariant | RegexOptions.IgnoreCase;

    // ------------------------------------------------------------ dump okuma
    public static (string metin, bool crlf) DumpOku(string yol)
    {
        var ham = File.ReadAllBytes(yol);
        string metin;
        try
        {
            var bas = ham.Length >= 3 && ham[0] == 0xEF && ham[1] == 0xBB && ham[2] == 0xBF ? 3 : 0;
            metin = new UTF8Encoding(false, true).GetString(ham, bas, ham.Length - bas);
        }
        catch (DecoderFallbackException)
        {
            try { metin = Encoding.GetEncoding(1254, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback).GetString(ham); }
            catch (DecoderFallbackException) { metin = Encoding.Latin1.GetString(ham); }
        }
        bool crlf = false;
        for (int i = 0; i + 1 < Math.Min(ham.Length, 5000); i++)
            if (ham[i] == '\r' && ham[i + 1] == '\n') { crlf = true; break; }
        return (metin.Replace("\r\n", "\n"), crlf);
    }

    public static List<Sutun> SutunlariBul(string dump, string tablo)
    {
        var m = Regex.Match(dump, @"CREATE TABLE\s+`?" + Regex.Escape(tablo) + @"`?\s*\((.*?)\n\)\s*[^;]*;", CII | RegexOptions.Singleline);
        if (!m.Success) throw new HataMesaji($"Dump içinde '{tablo}' tablosu bulunamadı.");
        var sutunlar = new List<Sutun>();
        foreach (var satir in Py.SplitLines(m.Groups[1].Value))
        {
            var s = Py.RStrip(Py.Strip(satir), ",");
            var sm = Regex.Match(s, @"^`([^`]+)`\s+(\w+(?:\([^)]*\))?(?:\s+unsigned)?)(.*)", CII | RegexOptions.Singleline);
            if (!sm.Success) continue;
            var geri = sm.Groups[3].Value;
            var dm = Regex.Match(geri, @"default\s+('(?:[^'\\]|\\.|'')*'|[^\s,]+)", CII);
            sutunlar.Add(new Sutun
            {
                Ad = sm.Groups[1].Value, Tip = sm.Groups[2].Value.ToLowerInvariant(),
                NotNull = Regex.IsMatch(geri, "not null", CII),
                Default = dm.Success ? dm.Groups[1].Value : null,
                Auto = Regex.IsMatch(geri, "auto_increment", CII),
            });
        }
        return sutunlar;
    }

    static readonly string[] Metin_ = { "char", "text" };
    static readonly string[] Sayi_ = { "int", "decimal", "float", "double", "numeric", "bit" };
    static readonly string[] Blob_ = { "blob", "binary" };

    static string Bul(List<Sutun> sutunlar, string[] desenler, string[] tipler = null, string haric = null)
    {
        foreach (var desen in desenler)
            foreach (var s in sutunlar)
            {
                if (s.Ad == haric) continue;
                if (tipler != null && !tipler.Any(t => s.Tip.Contains(t))) continue;
                if (Regex.IsMatch(s.Ad, "^(?:" + desen + @")\z", CII)) return s.Ad;
            }
        return null;
    }

    public static SiraliSozluk Eslestir(List<Sutun> sutunlar, bool urunMu, Dictionary<string, string> elle = null)
    {
        var e = new SiraliSozluk();
        e["id"] = (sutunlar.FirstOrDefault(s => s.Auto) ?? sutunlar[0]).Ad;
        if (urunMu)
        {
            e["ad"] = Bul(sutunlar, new[] { "urun_?ad[ıi]?", "urun_?ism[ıi]", "urun_?isim", "ad[ıi]?", "isim", "name", "urun" }, Metin_);
            e["kategori"] = Bul(sutunlar, new[] { "(urun)?_?kategori_?id", "kategori_?(no|kod)?", "kat_?id", "grup_?id" }, Sayi_, e["id"]);
            e["fiyat"] = Bul(sutunlar, new[] { "urun_?fiyat[ıi]?", @"(satis_?)?fiyat[ıi]?(_?1)?", @"satis_?fiyat\w*", @"fiyat\w*", "price" }, Sayi_);
            e["sira"] = Bul(sutunlar, new[] { "urun_?sira", @"sira\w*" }, Sayi_);
            e["resim"] = Bul(sutunlar, new[] { "urun_?resim", @"resim\w*", "image", @"foto\w*" }, Blob_.Concat(Metin_).ToArray());
        }
        else
        {
            e["ad"] = Bul(sutunlar, new[] { "kategori_?ad[ıi]?", "kategori_?ism[ıi]", "kategori_?isim", "ad[ıi]?", "isim", "name", "kategori" }, Metin_);
            e["sira"] = Bul(sutunlar, new[] { "kategori_?sira", @"sira\w*" }, Sayi_);
            e["resim"] = Bul(sutunlar, new[] { "kategori_?resim", @"resim\w*", "image", @"foto\w*" }, Blob_.Concat(Metin_).ToArray());
        }
        e["kdv"] = Bul(sutunlar, new[] { @"\w*kdv\w*", @"vergi\w*" }, Sayi_);
        if (elle != null)
            foreach (var (k, v) in elle) if (!string.IsNullOrEmpty(v)) e[k] = v;
        return e;
    }

    // ------------------------------------------------------------ ayrıştırma
    public static List<string> AlanlariAyir(string ic)
    {
        var alanlar = new List<string>();
        var buf = new StringBuilder();
        bool tirnak = false;
        for (int i = 0; i < ic.Length; i++)
        {
            char c = ic[i];
            if (tirnak)
            {
                buf.Append(c);
                if (c == '\\' && i + 1 < ic.Length) { buf.Append(ic[i + 1]); i++; continue; }
                if (c == '\'')
                {
                    if (i + 1 < ic.Length && ic[i + 1] == '\'') { buf.Append('\''); i++; continue; }
                    tirnak = false;
                }
            }
            else if (c == '\'') { tirnak = true; buf.Append(c); }
            else if (c == ',') { alanlar.Add(Py.Strip(buf.ToString())); buf.Clear(); }
            else buf.Append(c);
        }
        alanlar.Add(Py.Strip(buf.ToString()));
        return alanlar;
    }

    public static List<string> DemetleriAyir(string govde)
    {
        var sonuc = new List<string>();
        int derinlik = 0, bas = 0;
        bool tirnak = false;
        for (int i = 0; i < govde.Length; i++)
        {
            char c = govde[i];
            if (tirnak)
            {
                if (c == '\\') { i++; continue; }
                if (c == '\'')
                {
                    if (i + 1 < govde.Length && govde[i + 1] == '\'') { i++; continue; }
                    tirnak = false;
                }
            }
            else if (c == '\'') tirnak = true;
            else if (c == '(') { if (derinlik == 0) bas = i + 1; derinlik++; }
            else if (c == ')') { derinlik--; if (derinlik == 0) sonuc.Add(govde.Substring(bas, i - bas)); }
        }
        return sonuc;
    }

    public static string OdemeTipleriniSec(string dump, IList<string> kodlar, Action<string> log)
    {
        List<string> sut;
        try { sut = SutunlariBul(dump, "tbl_odemetipi").Select(x => x.Ad).ToList(); }
        catch (HataMesaji) { log("Uyarı: dump'ta tbl_odemetipi yok, ödeme tipleri seçilemedi."); return dump; }
        if (!sut.Contains("odemekod") || !sut.Contains("odemesec"))
        {
            log("Uyarı: tbl_odemetipi'nde odemekod/odemesec sütunu yok.");
            return dump;
        }
        int iKod = sut.IndexOf("odemekod"), iSec = sut.IndexOf("odemesec"), iGizle = sut.IndexOf("odemegizle");
        var secilen = new List<string>();
        dump = Regex.Replace(dump, @"^(INSERT INTO `tbl_odemetipi`(?:\s*\([^)]*\))?\s+VALUES\s*)(.*);[ \t]*$", m =>
        {
            var yeni = new List<string>();
            foreach (var d in DemetleriAyir(m.Groups[2].Value))
            {
                var alan = AlanlariAyir(d);
                if (alan.Count == sut.Count && kodlar.Contains(Py.Upper(Py.Strip(alan[iKod], "'"))))
                {
                    alan[iSec] = "'Y'";
                    if (iGizle >= 0) alan[iGizle] = "'N'";
                    secilen.Add(Py.Strip(alan[iKod], "'"));
                }
                yeni.Add("(" + string.Join(",", alan) + ")");
            }
            return m.Groups[1].Value + string.Join(",", yeni) + ";";
        }, CI | RegexOptions.Multiline);
        if (secilen.Count > 0) log("Ödeme tipleri seçili: " + string.Join(", ", secilen));
        var eksik = kodlar.Where(k => !secilen.Contains(k)).ToList();
        if (eksik.Count > 0) log("Uyarı: dump'ta bulunamayan ödeme tipi: " + string.Join(", ", eksik));
        return dump;
    }

    static bool AsciiDisi(string s) { foreach (var c in s) if (c > 127) return true; return false; }

    public static string VarsayilanDeger(Sutun s)
    {
        if (s.Default != null && s.Default.ToUpperInvariant() != "NULL")
        {
            var d = s.Default;
            if (d.ToUpperInvariant() is "CURRENT_TIMESTAMP" or "NOW()") return "NOW()";
            if (d.StartsWith("'", StringComparison.Ordinal))
            {
                var ic = d.Length >= 2 ? d.Substring(1, d.Length - 2).Replace("''", "'") : "";
                return AsciiDisi(ic) ? Metin.HexMetin(ic) : d;
            }
            return d;
        }
        if (!s.NotNull) return "NULL";
        var t = s.Tip;
        if (Sayi_.Any(t.Contains)) return "0";
        if (t.Contains("datetime") || t.Contains("timestamp")) return "NOW()";
        if (t.Contains("date")) return "CURDATE()";
        if (t.Contains("time")) return "'00:00:00'";
        return "''";
    }

    public static string InsertSatiri(string tablo, List<Sutun> sutunlar, Dictionary<string, string> degerler)
    {
        var adlar = string.Join(",", sutunlar.Select(s => $"`{s.Ad}`"));
        var vals = string.Join(",", sutunlar.Select(s => degerler.TryGetValue(s.Ad, out var v) ? v : VarsayilanDeger(s)));
        return $"INSERT INTO `{tablo}` ({adlar}) VALUES ({vals});";
    }

    // ------------------------------------------------------------ eski satırlardaki Türkçe
    static readonly Regex Literal = new(@"'((?:[^'\\]|\\.|'')*)'", CI);
    static readonly Regex Kacis = new(@"\\(.)", CI | RegexOptions.Singleline);

    public static string Coz(string ic)
    {
        ic = ic.Replace("''", "'");
        return Kacis.Replace(ic, m => m.Groups[1].Value switch
        {
            "0" => "\0", "n" => "\n", "r" => "\r", "t" => "\t", "Z" => "\x1a", "b" => "\b", var x => x,
        });
    }

    public static string EskiInsertleriDuzelt(string dump)
    {
        var satirlar = dump.Split('\n');
        for (int i = 0; i < satirlar.Length; i++)
        {
            var s = satirlar[i];
            if (!Py.Upper(Py.LStripWs(s)).StartsWith("INSERT INTO", StringComparison.Ordinal)) continue;
            satirlar[i] = Literal.Replace(s, m => AsciiDisi(m.Groups[1].Value) ? Metin.HexMetin(Coz(m.Groups[1].Value)) : m.Value);
        }
        return string.Join("\n", satirlar);
    }

    // ------------------------------------------------------------ veri bölümünü doldurma
    public static string BolumuDoldur(string dump, string tablo, List<string> satirlar)
    {
        var t = Regex.Escape(tablo);
        dump = Regex.Replace(dump, @"^INSERT INTO `?" + t + @"`?\s.*?;\s*$\n?", "", CII | RegexOptions.Multiline);
        var blok = string.Join("\n", satirlar) + "\n";
        var baslik = Regex.Match(dump, @"^#\s*Data for table [`""]?" + t + @"[`""]?\s*\n#\s*\n\n?", CII | RegexOptions.Multiline);
        if (baslik.Success)
        {
            int son = baslik.Index + baslik.Length;
            return dump.Substring(0, son) + blok + dump.Substring(son);
        }
        var ct = Regex.Match(dump, @"CREATE TABLE\s+`?" + t + @"`?\s*\(.*?\n\)\s*[^;]*;\n", CII | RegexOptions.Singleline);
        if (!ct.Success) throw new HataMesaji($"'{tablo}' için ekleme yeri bulunamadı.");
        int e = ct.Index + ct.Length;
        return dump.Substring(0, e) + $"\n#\n# Data for table \"{tablo}\"\n#\n\n" + blok + dump.Substring(e);
    }

    public static string FirmaAdiBul(string dumpYolu)
    {
        string dump;
        try { dump = DumpOku(dumpYolu).metin; } catch { return ""; }
        var m = Regex.Match(dump, @"INSERT INTO `?tbl_ayar`?\s+VALUES\s*\(\s*[^,]*,\s*'((?:[^'\\]|\\.|'')*)'", CI);
        if (m.Success) return Metin.GuvenliAd(Coz(m.Groups[1].Value));
        m = Regex.Match(dump, @"INSERT INTO `?tbl_ayar`?\s+VALUES\s*\(\s*[^,]*,\s*CONVERT\(0x([0-9A-Fa-f]+)", CI);
        if (m.Success)
        {
            try
            {
                var hex = m.Groups[1].Value;
                if (hex.Length % 2 == 0)
                    return Metin.GuvenliAd(new UTF8Encoding(false, true).GetString(Convert.FromHexString(hex)));
            }
            catch { }
        }
        return "";
    }

    // ------------------------------------------------------------ resim
    /// <summary>Klasörde ürünün resmini bulur (jpg/png/webp..., büyük-küçük harf fark etmez).</summary>
    public static string ResimBul(string klasor, string ad)
    {
        foreach (var aday in new[] { ad + ".jpg", ad + "_1.jpg" })
        {
            var yol = Path.Combine(klasor, aday);
            if (File.Exists(yol)) return yol;
        }
        if (!Directory.Exists(klasor)) return null;
        var hedef = new HashSet<string> { Py.Lower(ad), Py.Lower(ad + "_1") };
        foreach (var dosya in Directory.GetFiles(klasor).Select(Path.GetFileName).OrderBy(x => x, StringComparer.Ordinal))
        {
            var uz = Path.GetExtension(dosya).ToLowerInvariant();
            var kok = dosya.Substring(0, dosya.Length - Path.GetExtension(dosya).Length);
            if (Resim.Uzantilar.Contains(uz) && hedef.Contains(Py.Lower(kok))) return Path.Combine(klasor, dosya);
        }
        return null;
    }

    public static string KlasorYolu(string resimKok, string yol) =>
        Path.Combine(resimKok, yol.Replace('/', Path.DirectorySeparatorChar));

    // ------------------------------------------------------------ ortak: kategorileri SQL seviyesine topla
    public sealed record UrunKaydi(string Ad, string Klasor, double? Fiyat, string ResimAdi, List<Porsiyon> Porsiyonlar);

    public static (List<string> sira, Dictionary<string, List<UrunKaydi>> urunler) KategorileriTopla(
        List<Kategori> kategoriler, string resimKok, int seviye, Dictionary<string, string> gercek)
    {
        var sira = new List<string>();
        var d = new Dictionary<string, List<UrunKaydi>>();
        foreach (var k in kategoriler)
        {
            var katAd = string.Join(" ", k.Yol.Split('/').Take(seviye));
            if (!d.ContainsKey(katAd)) { sira.Add(katAd); d[katAd] = new List<UrunKaydi>(); }
            string klasor = null;
            if (gercek != null) gercek.TryGetValue(k.Yol, out klasor);
            if (string.IsNullOrEmpty(klasor)) klasor = KlasorYolu(resimKok, k.Yol);
            foreach (var u in k.Urunler)
                d[katAd].Add(new UrunKaydi(u.Ad, klasor, u.Fiyat, gercek != null ? u.Arama : u.Ad, u.Porsiyonlar ?? new()));
        }
        return (sira, d);
    }

    // ------------------------------------------------------------ SQL oluştur
    public sealed record Sonuc(int Kategori, int Urun, List<string> Eksik);

    public static Sonuc SqlOlustur(string dumpYolu, List<Kategori> kategoriler, string resimKok, string ciktiYolu,
        double fiyat = 1.0, double kdv = 10, int kategoriSeviye = 1, Dictionary<string, Dictionary<string, string>> elle = null,
        Action<string> log = null, Dictionary<string, string> gercekKlasorler = null, IList<string> ekOdemeler = null,
        Dictionary<string, Dictionary<string, string>> sabitler = null, CancellationToken iptal = default)
    {
        log ??= _ => { };
        if (fiyat <= 0) throw new HataMesaji("Fiyat 0 veya eksi olamaz (yazar kasa kabul etmez).");
        elle ??= new();
        sabitler ??= new();
        var (dump, crlf) = DumpOku(dumpYolu);
        var kSut = SutunlariBul(dump, KategoriTablo);
        var uSut = SutunlariBul(dump, UrunTablo);
        var kEs = Eslestir(kSut, false, elle.GetValueOrDefault(KategoriTablo));
        var uEs = Eslestir(uSut, true, elle.GetValueOrDefault(UrunTablo));
        log($"{KategoriTablo} eşleşme: {kEs}");
        log($"{UrunTablo} eşleşme: {uEs}");
        foreach (var (gerekli, es, tablo) in new[] { ("ad", kEs, KategoriTablo), ("ad", uEs, UrunTablo), ("kategori", uEs, UrunTablo) })
            if (string.IsNullOrEmpty(es[gerekli]))
                throw new HataMesaji($"{tablo} içinde '{gerekli}' sütunu bulunamadı; ayarlardan sütun eşlemesini belirt.");

        var (katSira, katUrunler) = KategorileriTopla(kategoriler, resimKok, kategoriSeviye, gercekKlasorler);

        var kSatir = new List<string>(); var uSatir = new List<string>();
        var kcSatir = new List<Dictionary<string, string>>(); var cSatir = new List<Dictionary<string, string>>();
        int uid = 0, fiyatli = 0, kcid = 0, cid = 0;
        var eksik = new List<string>();
        for (int kid = 1; kid <= katSira.Count; kid++)
        {
            iptal.ThrowIfCancellationRequested();
            var katAd = katSira[kid - 1];
            string ilkResim = null;
            foreach (var u in katUrunler[katAd]) { ilkResim = ResimBul(u.Klasor, u.ResimAdi); if (ilkResim != null) break; }
            var d = new Dictionary<string, string> { [kEs["id"]] = kid.ToString(), [kEs["ad"]] = Metin.HexMetin(Metin.TrBuyuk(katAd)) };
            if (!string.IsNullOrEmpty(kEs["sira"])) d[kEs["sira"]] = kid.ToString();
            if (!string.IsNullOrEmpty(kEs["kdv"])) d[kEs["kdv"]] = Metin.Sayi(kdv);
            if (!string.IsNullOrEmpty(kEs["resim"])) d[kEs["resim"]] = Resim.ResimHex(ilkResim);
            foreach (var (k, v) in sabitler.GetValueOrDefault(KategoriTablo) ?? new()) d[k] = v;
            kSatir.Add(InsertSatiri(KategoriTablo, kSut, d));

            int katSiraNo = 0;
            var katPors = new Dictionary<string, int>();
            foreach (var u in katUrunler[katAd])
            {
                uid++; katSiraNo++;
                double uFiyat;
                if (u.Fiyat.HasValue && u.Fiyat.Value > 0) { fiyatli++; uFiyat = u.Fiyat.Value; } else uFiyat = fiyat;
                var r = ResimBul(u.Klasor, u.ResimAdi);
                if (r == null) eksik.Add(u.Ad);
                d = new Dictionary<string, string> { [uEs["id"]] = uid.ToString(), [uEs["ad"]] = Metin.HexMetin(Metin.TrBuyuk(u.Ad)), [uEs["kategori"]] = kid.ToString() };
                if (!string.IsNullOrEmpty(uEs["fiyat"])) d[uEs["fiyat"]] = Metin.Sayi(uFiyat);
                if (!string.IsNullOrEmpty(uEs["sira"])) d[uEs["sira"]] = katSiraNo.ToString();
                if (!string.IsNullOrEmpty(uEs["kdv"])) d[uEs["kdv"]] = Metin.Sayi(kdv);
                if (!string.IsNullOrEmpty(uEs["resim"])) d[uEs["resim"]] = Resim.ResimHex(r);
                foreach (var (k, v) in sabitler.GetValueOrDefault(UrunTablo) ?? new()) d[k] = v;
                uSatir.Add(InsertSatiri(UrunTablo, uSut, d));
                foreach (var p in u.Porsiyonlar)
                {
                    var anahtar = Metin.PorsNorm(p.Ad);
                    if (!katPors.ContainsKey(anahtar))
                    {
                        kcid++;
                        katPors[anahtar] = kcid;
                        kcSatir.Add(new()
                        {
                            ["kategoricarpanid"] = kcid.ToString(), ["kategoriid"] = kid.ToString(),
                            ["kategoricarpantip"] = "'Porsiyon'", ["kategoricarpanad"] = Metin.HexMetin(Metin.TrBuyuk(p.Ad)),
                            ["kategoricarpansira"] = katPors.Count.ToString(), ["kategoricarpansil"] = "'N'", ["kategoricarpanyeni"] = "'N'",
                        });
                    }
                    cid++;
                    cSatir.Add(new()
                    {
                        ["carpanid"] = cid.ToString(), ["kategoricarpanid"] = katPors[anahtar].ToString(), ["kategoriid"] = kid.ToString(),
                        ["urunid"] = uid.ToString(), ["carpantip"] = "'Porsiyon'", ["carpanad"] = Metin.HexMetin(Metin.TrBuyuk(p.Ad)),
                        ["carpanfiyat"] = Metin.Sayi(p.Fiyat.HasValue && p.Fiyat.Value != 0 ? p.Fiyat.Value : uFiyat), ["carpansil"] = "'N'",
                    });
                }
            }
        }

        var kodlar = new List<string> { "NAKIT", "KREDIKARTI" };
        foreach (var k in ekOdemeler ?? Array.Empty<string>()) if (k != "NAKIT" && k != "KREDIKARTI") kodlar.Add(k);
        dump = OdemeTipleriniSec(dump, kodlar, log);
        dump = EskiInsertleriDuzelt(dump);
        dump = BolumuDoldur(dump, KategoriTablo, kSatir);
        dump = BolumuDoldur(dump, UrunTablo, uSatir);
        var sayac = new List<(string, int)> { (KategoriTablo, katSira.Count), (UrunTablo, uid) };
        foreach (var (tablo, satirlar, n) in new[] { (KatCarpanTablo, kcSatir, kcid), (UrunCarpanTablo, cSatir, cid) })
        {
            List<Sutun> sut;
            try { sut = SutunlariBul(dump, tablo); }
            catch (HataMesaji)
            {
                if (satirlar.Count > 0) log($"Uyarı: dump'ta {tablo} yok, porsiyonlar yazılamadı.");
                continue;
            }
            var adlar = sut.Select(x => x.Ad).ToHashSet();
            var yazilacak = satirlar.Select(r => InsertSatiri(tablo, sut, r.Where(kv => adlar.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value))).ToList();
            dump = BolumuDoldur(dump, tablo, yazilacak);
            sayac.Add((tablo, n));
        }
        if (cid > 0) log($"Porsiyonlar: {kcid} porsiyon tipi, {cid} ürün porsiyonu yazıldı.");
        foreach (var (tablo, n) in sayac)
            dump = Regex.Replace(dump, @"(CREATE TABLE\s+`?" + Regex.Escape(tablo) + @"`?\s*\(.*?\n\)[^;]*?AUTO_INCREMENT=)\d+",
                m => m.Groups[1].Value + (n + 1), CII | RegexOptions.Singleline);

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(ciktiYolu)));
        if (crlf) dump = dump.Replace("\n", "\r\n");
        int kalan = 0;
        foreach (var r in dump.EnumerateRunes()) if (r.Value > 127) kalan++;
        var cp = Encoding.GetEncoding(1254, new EncoderReplacementFallback("?"), new DecoderReplacementFallback("?"));
        File.WriteAllBytes(ciktiYolu, cp.GetBytes(dump));
        log($"SQL yazıldı: {ciktiYolu}  ({katSira.Count} kategori, {uid} ürün, {fiyatli} ürün menü fiyatıyla, {uid - fiyatli} ürün {Metin.Sayi(fiyat)} TL)");
        if (kalan > 0) log($"Not: INSERT dışındaki yorum satırlarında {kalan} Türkçe karakter kaldı (önemsiz).");
        if (eksik.Count > 0)
            log($"Resmi olmayan {eksik.Count} ürün (urunresim NULL): " + string.Join(", ", eksik.Take(15)) + (eksik.Count > 15 ? " ..." : ""));
        return new Sonuc(katSira.Count, uid, eksik);
    }
}

/// <summary>Kullanıcıya olduğu gibi gösterilecek hata.</summary>
public sealed class HataMesaji : Exception
{
    public HataMesaji(string m) : base(m) { }
}
