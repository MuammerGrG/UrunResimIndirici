using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace UrunMotor;

// ------------------------------------------------------------------ veri modeli
public sealed class Porsiyon
{
    public string Ad { get; set; } = "";
    public double? Fiyat { get; set; }
    public Porsiyon() { }
    public Porsiyon(string ad, double? fiyat) { Ad = ad; Fiyat = fiyat; }
}

public sealed class Urun
{
    public string Ad { get; set; } = "";
    /// <summary>İnternet araması; klasörden okunanlarda resim dosyasının asıl adı.</summary>
    public string Arama { get; set; } = "";
    public double? Fiyat { get; set; }
    public List<Porsiyon> Porsiyonlar { get; set; } = new();
}

public sealed class Kategori
{
    /// <summary>"Döner/Izgara/Menü" biçiminde, '/' ile ayrılmış klasör yolu.</summary>
    public string Yol { get; set; } = "";
    public List<Urun> Urunler { get; set; } = new();
}

/// <summary>Python davranışını birebir taklit eden küçük yardımcılar.</summary>
public static class Py
{
    public static readonly CultureInfo CI = CultureInfo.InvariantCulture;

    public static bool IsSpace(char c) => char.IsWhiteSpace(c) || (c >= '\x1c' && c <= '\x1f');

    public static string Strip(string s)
    {
        if (string.IsNullOrEmpty(s)) return s ?? "";
        int a = 0, b = s.Length - 1;
        while (a <= b && IsSpace(s[a])) a++;
        while (b >= a && IsSpace(s[b])) b--;
        return s.Substring(a, b - a + 1);
    }

    public static string Strip(string s, string karakterler) => LStrip(RStrip(s, karakterler), karakterler);
    public static string LStrip(string s, string k) { int a = 0; while (a < s.Length && k.IndexOf(s[a]) >= 0) a++; return s.Substring(a); }
    public static string RStrip(string s, string k) { int b = s.Length; while (b > 0 && k.IndexOf(s[b - 1]) >= 0) b--; return s.Substring(0, b); }

    public static string LStripWs(string s) { int a = 0; while (a < s.Length && IsSpace(s[a])) a++; return s.Substring(a); }

    /// <summary>str.splitlines()</summary>
    public static List<string> SplitLines(string s)
    {
        var sonuc = new List<string>();
        if (string.IsNullOrEmpty(s)) return sonuc;
        var sb = new StringBuilder();
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c == '\r')
            {
                sonuc.Add(sb.ToString()); sb.Clear();
                if (i + 1 < s.Length && s[i + 1] == '\n') i++;
            }
            else if (c == '\n' || c == '\v' || c == '\f' || c == '\x1c' || c == '\x1d' || c == '\x1e' ||
                     c == '\x85' || c == '\u2028' || c == '\u2029')
            {
                sonuc.Add(sb.ToString()); sb.Clear();
            }
            else sb.Append(c);
        }
        if (sb.Length > 0) sonuc.Add(sb.ToString());
        return sonuc;
    }

    /// <summary>str.split() (boşluğa göre, boşları atar)</summary>
    public static List<string> SplitWs(string s)
    {
        var l = new List<string>();
        var sb = new StringBuilder();
        foreach (var c in s ?? "")
        {
            if (IsSpace(c)) { if (sb.Length > 0) { l.Add(sb.ToString()); sb.Clear(); } }
            else sb.Append(c);
        }
        if (sb.Length > 0) l.Add(sb.ToString());
        return l;
    }

    public static string Lower(string s) => (s ?? "").ToLowerInvariant();
    public static string Upper(string s) => (s ?? "").ToUpperInvariant();

    /// <summary>Python '%g' % x</summary>
    public static string G(double x) => x.ToString("G6", CI).Replace("E", "e");

    /// <summary>Python repr(float) — json.dumps için ("-50.0", "12.5")</summary>
    public static string FloatRepr(double x)
    {
        if (double.IsInfinity(x) || double.IsNaN(x)) return "null";
        if (x == Math.Floor(x) && Math.Abs(x) < 1e16) return ((long)x).ToString(CI) + ".0";
        return x.ToString("R", CI).Replace("E", "e");
    }

    /// <summary>Python'un re.IGNORECASE'i 'İ','ı','ſ','K'yı ASCII karşılıklarıyla eşler; .NET eşlemez.
    /// Bu yüzden eşleştirme, uzunluğu aynı kalan "katlanmış" kopya üzerinde yapılır.</summary>
    public static string Katla(string s)
    {
        if (s == null) return "";
        if (s.IndexOfAny(new[] { 'İ', 'ı', 'ſ', 'K' }) < 0) return s;
        var a = s.ToCharArray();
        for (int i = 0; i < a.Length; i++)
            a[i] = a[i] switch { 'İ' => 'I', 'ı' => 'i', 'ſ' => 's', 'K' => 'K', _ => a[i] };
        return new string(a);
    }
}

/// <summary>Python re.I davranışlı (İ/ı uyumlu) regex sarmalayıcı.</summary>
public sealed class Rx
{
    public readonly Regex R;
    readonly bool _i;
    public Rx(string desen, bool ignoreCase = false, bool singleline = false, bool multiline = false)
    {
        var o = RegexOptions.CultureInvariant;
        if (ignoreCase) o |= RegexOptions.IgnoreCase;
        if (singleline) o |= RegexOptions.Singleline;
        if (multiline) o |= RegexOptions.Multiline;
        R = new Regex(desen, o);
        _i = ignoreCase;
    }
    string K(string s) => _i ? Py.Katla(s) : (s ?? "");

    /// <summary>Eşleşme (grup değerleri asıl metinden alınır).</summary>
    public RxEs Match(string s, int baslangic = 0)
    {
        var m = R.Match(K(s), baslangic);
        return m.Success ? new RxEs(m, s) : null;
    }
    public bool IsMatch(string s) => R.IsMatch(K(s));
    public List<RxEs> Matches(string s) => R.Matches(K(s)).Select(m => new RxEs(m, s)).ToList();

    public string Replace(string s, Func<RxEs, string> f)
    {
        var ks = K(s);
        var sb = new StringBuilder();
        int son = 0;
        foreach (Match m in R.Matches(ks))
        {
            sb.Append(s, son, m.Index - son);
            sb.Append(f(new RxEs(m, s)));
            son = m.Index + m.Length;
        }
        sb.Append(s, son, s.Length - son);
        return sb.ToString();
    }
    public string Replace(string s, string sabit) => Replace(s, _ => sabit);
}

public sealed class RxEs
{
    readonly Match _m; readonly string _s;
    public RxEs(Match m, string s) { _m = m; _s = s; }
    public int Index => _m.Index;
    public int Length => _m.Length;
    public int End => _m.Index + _m.Length;
    public string Value => _s.Substring(_m.Index, _m.Length);
    public string this[int g] => _m.Groups[g].Success ? _s.Substring(_m.Groups[g].Index, _m.Groups[g].Length) : null;
    public string this[string g] => _m.Groups[g].Success ? _s.Substring(_m.Groups[g].Index, _m.Groups[g].Length) : null;
}

public static class Metin
{
    // ------------------------------------------------------------------ Türkçe
    const string TrKaynak = "İIıÇçĞğÖöŞşÜüÂâÎîÛû";
    const string TrHedef = "iiiccggoossuuaaiiuu";

    public static string TrCevir(string s)
    {
        if (string.IsNullOrEmpty(s)) return s ?? "";
        var a = s.ToCharArray();
        for (int i = 0; i < a.Length; i++)
        {
            int j = TrKaynak.IndexOf(a[i]);
            if (j >= 0) a[i] = TrHedef[j];
        }
        return new string(a);
    }

    /// <summary>_norm: Türkçe harfsiz, küçük harf, nokta→boşluk</summary>
    public static string Norm(string s) => Py.Strip(Py.Lower(TrCevir(s ?? "")).Replace(".", " "));

    static readonly Regex Bosluk = new(@"\s+", RegexOptions.CultureInvariant);
    public static string PorsNorm(string s) => Py.Strip(Bosluk.Replace(Py.Lower(TrCevir(s ?? "")), " "));

    static readonly Regex YasakKarakter = new(@"[\\/:*?""<>|]+", RegexOptions.CultureInvariant);
    public static string GuvenliAd(string s)
    {
        s = Py.RStrip(Py.Strip(YasakKarakter.Replace(s ?? "", "")), ".");
        return Bosluk.Replace(s, " ");
    }

    /// <summary>Türkçe büyük harf: i→İ, ı→I</summary>
    public static string TrBuyuk(string s) => Py.Upper((s ?? "").Replace("i", "İ").Replace("ı", "I"));

    public static string HexMetin(string s)
    {
        if (s == "") return "''";
        return "CONVERT(0x" + Convert.ToHexString(Encoding.UTF8.GetBytes(s)) + " USING utf8)";
    }

    /// <summary>MySQL utf8 (5.5) 4 baytlık karakterleri saklayamaz: emoji ve değişken seçicileri atılır.</summary>
    public static string Emojisiz(string s)
    {
        var sb = new StringBuilder();
        foreach (var r in (s ?? "").EnumerateRunes())
            if (r.Value <= 0xFFFF && !(r.Value >= 0xFE00 && r.Value <= 0xFE0F)) sb.Append(r.ToString());
        return Py.Strip(Bosluk.Replace(sb.ToString(), " "));
    }

    public static string Sayi(double d)
    {
        var t = d.ToString("F2", Py.CI).TrimEnd('0').TrimEnd('.');
        return t == "" ? "0" : t;
    }

    // ------------------------------------------------------------------ fiyat
    static readonly Rx FiyatSonda = new(
        @"^(.*?)(?:\s*[-–—:=…]+\s*|\s+)(?:₺\s*)?(\d{1,3}(?:[.\s]\d{3})*(?:,\d{1,2})?|\d+(?:[.,]\d{1,2})?)\s*(tl|₺|try)\.?$", true);
    static readonly Rx FiyatAyrac = new(
        @"^(.*?)\s*(?:[-–—:=]|\.{2,}|…)+\s*(?:₺\s*)?(\d+(?:[.,]\d{1,2})?)\s*(?:tl|₺)?\.?$", true);
    static readonly Regex BinlikNokta = new(@"^\d{1,3}(\.\d{3})+$", RegexOptions.CultureInvariant);
    static readonly Rx SadeceFiyat = new(@"^[₺\d.,\s]+(tl)?$", true);

    /// <summary>'250', '87,50', '1.250,00', '₺ 99' → sayı; olmazsa null</summary>
    public static double? FiyatCoz(string metin)
    {
        if (metin == null) return null;
        var t = Py.Strip(metin).ToLowerInvariant().Replace("₺", "").Replace("tl", "").Replace(" ", "");
        if (t.Length == 0) return null;
        if (t.Contains(',') && t.Contains('.')) t = t.Replace(".", "").Replace(",", ".");
        else if (t.Contains(',')) t = t.Replace(",", ".");
        else if (BinlikNokta.IsMatch(t)) t = t.Replace(".", "");
        if (!PyFloat(t, out var v)) return null;
        return v > 0 ? v : null;
    }

    /// <summary>Python float(): baş/son boşluk, +/- işareti, "1e3", "inf" gibi biçimler.</summary>
    static bool PyFloat(string t, out double v)
    {
        v = 0;
        t = Py.Strip(t).Replace("_", "");
        if (t.Length == 0) return false;
        if (!Regex.IsMatch(t, @"^[+-]?(\d+\.?\d*([eE][+-]?\d+)?|\.\d+([eE][+-]?\d+)?|inf(inity)?|nan)$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) return false;
        var tl = t.ToLowerInvariant();
        if (tl.EndsWith("inf", StringComparison.Ordinal) || tl.EndsWith("infinity", StringComparison.Ordinal)) { v = tl.StartsWith("-", StringComparison.Ordinal) ? double.NegativeInfinity : double.PositiveInfinity; return true; }
        if (tl.EndsWith("nan", StringComparison.Ordinal)) { v = double.NaN; return true; }
        return double.TryParse(t, NumberStyles.Float, Py.CI, out v);
    }

    // ------------------------------------------------------------------ porsiyon
    const string PorsKelime = @"az|yar[iı]m|tam|tek|normal|standart|orta|k[uü][cç][uü]k|b[uü]y[uü]k|mini|midi|maxi|" +
                              @"mega|duble|double|[cç]ift|bir\s*bu[cç]uk|1\s*[.,]\s*5|0\s*[.,]\s*5|1½";
    public static string PorsKelimeDesen => PorsKelime;
    static readonly Rx PorsSon = new(
        @"^(?<kok>.*?)[\s\-–/,(]+(?<etiket>(?:" + PorsKelime + @")(?:\s*(?:porsiyon|pors\.?|p\.?|ki[sş]ilik))?|" +
        @"[1-3]\s*(?:porsiyon|pors\.?|ki[sş]ilik)|porsiyon)\)?\s*$", true);
    static readonly string[] PorsNormal = { "tam", "normal", "tek", "standart", "1 porsiyon", "porsiyon", "orta" };
    static readonly Rx PorsKisalt = new(@"\s*(porsiyon|pors\.?|p\.)$", true);
    static readonly Regex SadeceRakam = new(@"^\d+$", RegexOptions.CultureInvariant);
    static readonly Regex NoktaliOndalik = new(@"(\d)\s*[.]\s*(\d)", RegexOptions.CultureInvariant);
    static readonly Regex VirgulluOndalik = new(@"(\d)\s*,\s*(\d)", RegexOptions.CultureInvariant);

    /// <summary>'1.5 porsiyon' → '1,5', 'az' → 'Az'</summary>
    public static string PorsiyonEtiketi(string metin)
    {
        var m = Py.Strip(Bosluk.Replace(metin ?? "", " "));
        var kisa = Py.Strip(PorsKisalt.Replace(m, ""));
        if (kisa.Length > 0 && !SadeceRakam.IsMatch(kisa)) m = kisa;
        m = NoktaliOndalik.Replace(m, "$1,$2");
        m = VirgulluOndalik.Replace(m, "$1,$2");
        return m.Length == 0 ? m : char.ToUpperInvariant(m[0]) + m.Substring(1);
    }

    public sealed record PorsAyrim(string Kok, string Etiket, bool CiplakPorsiyon);

    /// <summary>'Adana Dürüm Az' → ('Adana Dürüm', 'Az', false); porsiyon yoksa null</summary>
    public static PorsAyrim PorsiyonAyir(string ad)
    {
        var m = PorsSon.Match(Py.Strip(ad ?? ""));
        if (m == null || Py.Strip(m["kok"]).Length == 0) return null;
        var etiket = Py.Strip(m["etiket"]);
        return new PorsAyrim(Py.Strip(m["kok"], " -–/,("), PorsiyonEtiketi(etiket), PorsNorm(etiket) == "porsiyon");
    }

    static readonly Regex PorsBol = new(@"[;/]|,(?!\d)", RegexOptions.CultureInvariant);
    static readonly Rx PorsParca = new(@"^\s*(.+?)\s*[=:]\s*(?:₺\s*)?([\d.,]+)\s*(?:tl|₺)?\s*$", true);
    static readonly Regex EsitVeyaIkiNokta = new("[=:]", RegexOptions.CultureInvariant);

    /// <summary>'Az=100; 1,5=200' → [(Az,100),(1,5,200)]; fiyatsız 'Duble' → (Duble, null)</summary>
    public static List<Porsiyon> PorsiyonlariCoz(string metin)
    {
        var sonuc = new List<Porsiyon>();
        foreach (var parca in PorsBol.Split(metin ?? ""))
        {
            var m = PorsParca.Match(parca);
            if (m != null)
            {
                var f = FiyatCoz(Py.RStrip(m[2], ",."));
                if (f.HasValue) sonuc.Add(new Porsiyon(PorsiyonEtiketi(m[1]), f));
            }
            else if (Py.Strip(parca).Length > 0 && !EsitVeyaIkiNokta.IsMatch(parca) && Py.Strip(parca).Length <= 20)
                sonuc.Add(new Porsiyon(PorsiyonEtiketi(Py.Strip(parca)), null));
        }
        return sonuc;
    }

    public static string PorsiyonlariYaz(IEnumerable<Porsiyon> pors) =>
        string.Join("; ", pors.Select(p => p.Fiyat.HasValue && p.Fiyat.Value != 0 ? $"{p.Ad}={Py.G(p.Fiyat.Value)}" : p.Ad));

    /// <summary>'Adana Az 100', 'Adana Tam 150' → tek ürün + porsiyonlar.</summary>
    public static List<Urun> PorsiyonGrupla(List<Urun> urunler)
    {
        var bilgi = urunler.Select(u => u.Porsiyonlar.Count == 0 ? PorsiyonAyir(u.Ad) : null).ToList();
        var gruplar = new List<KeyValuePair<string, List<int>>>();
        List<int> Bul(string k) => gruplar.FirstOrDefault(g => g.Key == k).Value;
        for (int i = 0; i < urunler.Count; i++)
        {
            if (bilgi[i] == null) continue;
            var k = PorsNorm(bilgi[i].Kok);
            var l = Bul(k);
            if (l == null) gruplar.Add(new(k, new List<int> { i })); else l.Add(i);
        }
        for (int i = 0; i < urunler.Count; i++)
        {
            if (bilgi[i] != null || urunler[i].Porsiyonlar.Count > 0) continue;
            var l = Bul(PorsNorm(urunler[i].Ad));
            l?.Insert(0, i);
        }
        var birlesen = new HashSet<int>();
        var sonucYeri = new Dictionary<int, Urun>();
        foreach (var (_, idx) in gruplar)
        {
            if (idx.Count < 2) continue;
            var uyeler = idx.Select(i => (i, u: urunler[i], ay: bilgi[i])).ToList();
            (int i, Urun u, PorsAyrim ay)? ana = null;
            foreach (var x in uyeler) if (x.ay == null) { ana = x; break; }
            if (ana == null)
                foreach (var tercih in PorsNormal)
                {
                    foreach (var x in uyeler) if (x.ay != null && PorsNorm(x.ay.Etiket) == tercih) { ana = x; break; }
                    if (ana != null) break;
                }
            if (ana == null)
            {
                var fiyatli = uyeler.Where(x => x.u.Fiyat.HasValue && x.u.Fiyat.Value != 0).ToList();
                if (fiyatli.Count > 0)
                {
                    var en = fiyatli[0];
                    foreach (var x in fiyatli) if (x.u.Fiyat.Value < en.u.Fiyat.Value) en = x;
                    ana = en;
                }
                else ana = uyeler[0];
            }
            var (i0, u0, ay0) = ana.Value;
            var urunAdi = ay0 == null || ay0.CiplakPorsiyon ? u0.Ad : ay0.Kok;
            var arama = PorsNorm(u0.Arama) != PorsNorm(u0.Ad) ? u0.Arama : urunAdi;
            var pors = new List<Porsiyon>();
            var gorulen = new HashSet<string>();
            foreach (var (i, u, ay) in uyeler)
            {
                if (i == i0 || ay == null) continue;
                var e = ay.Etiket;
                if (!gorulen.Add(PorsNorm(e))) continue;
                pors.Add(new Porsiyon(e, (u.Fiyat.HasValue && u.Fiyat.Value != 0) ? u.Fiyat : u0.Fiyat));
            }
            foreach (var i in idx) birlesen.Add(i);
            sonucYeri[idx.Min()] = new Urun { Ad = urunAdi, Arama = arama, Fiyat = u0.Fiyat, Porsiyonlar = pors };
        }
        var sonuc = new List<Urun>();
        for (int i = 0; i < urunler.Count; i++)
        {
            if (sonucYeri.TryGetValue(i, out var y)) sonuc.Add(y);
            else if (!birlesen.Contains(i)) sonuc.Add(urunler[i]);
        }
        return sonuc;
    }

    // ------------------------------------------------------------------ satır
    public sealed record Satir(string Ad, string Arama, double? Fiyat, List<Porsiyon> Porsiyonlar);

    /// <summary>'Ad | arama | fiyat | Az=100', 'Ad - 250 TL', 'Ad 250₺' → (ad, arama, fiyat, porsiyonlar)</summary>
    public static Satir SatiriCoz(string s)
    {
        var parcalar = s.Split('|').Select(Py.Strip).ToArray();
        string ad = parcalar[0], arama = "";
        double? fiyat = null;
        var pors = new List<Porsiyon>();
        if (parcalar.Length >= 4) pors = PorsiyonlariCoz(parcalar[3]);
        if (parcalar.Length >= 3) { arama = parcalar[1]; fiyat = FiyatCoz(parcalar[2]); }
        else if (parcalar.Length == 2)
        {
            if (FiyatCoz(parcalar[1]).HasValue && SadeceFiyat.IsMatch(parcalar[1])) fiyat = FiyatCoz(parcalar[1]);
            else arama = parcalar[1];
        }
        if (!fiyat.HasValue)
        {
            var m = FiyatSonda.Match(ad) ?? FiyatAyrac.Match(ad);
            if (m != null && Py.Strip(m[1]).Length > 0) { ad = Py.Strip(m[1]); fiyat = FiyatCoz(m[2]); }
        }
        return new Satir(ad, arama, fiyat, pors);
    }

    // ------------------------------------------------------------------ ödeme
    static readonly Rx OdemeSatiriRx = new(
        @"^\s*[-•*]?\s*(?:ödeme|odeme)(?:\s*(?:tipleri|yöntemleri|yontemleri|seçenekleri|secenekleri))?\s*[:=]", true);
    public static readonly (string Anahtar, string Kod)[] OdemeEsleme =
    {
        ("pluxee", "SODEXO"), ("sodexo", "SODEXO"), ("multinet", "MULTINET"),
        ("edenred", "SMARTICKET"), ("ticket", "SMARTICKET"), ("setcard", "SETCARD"), ("set kart", "SETCARD"),
        ("metropol", "METROPOL"), ("paye", "PAYEKART"), ("tokenflex", "TOKENFLEX"),
        ("win win", "WINWIN"), ("winwin", "WINWIN"), ("yemekmatik", "YEMEKMATIK"), ("cio card", "CIOCARD"),
        ("ciocard", "CIOCARD"), ("en kart", "ENCARD"), ("encard", "ENCARD"), ("teb kart", "TEBCARD"),
        ("bistra", "BISTRACARD"), ("maxima", "MAXIMACARD"), ("advantage", "ADVANTAGE"), ("maximum", "MAXIMUM"),
        ("kredi kart", "KREDIKARTI"), ("banka kart", "KREDIKARTI"), ("nakit", "NAKIT"),
    };
    public static readonly string[] YemekKartiGenel = { "SODEXO", "MULTINET", "SMARTICKET", "SETCARD", "METROPOL" };

    public static bool OdemeSatiriMi(string satir) => OdemeSatiriRx.IsMatch(TrCevir(satir ?? ""));

    static readonly Regex HarfDisi = new("[^a-z0-9]+", RegexOptions.CultureInvariant);
    static readonly Regex IlkAyrac = new("[:=]", RegexOptions.CultureInvariant);

    public static List<string> OdemeKodlariBul(string metin)
    {
        var kodlar = new List<string>();
        foreach (var satir in Py.SplitLines(metin ?? ""))
        {
            if (!OdemeSatiriMi(satir)) continue;
            var son = IlkAyrac.Split(satir, 2).Last();
            var n = " " + HarfDisi.Replace(Norm(son), " ") + " ";
            foreach (var (anahtar, kod) in OdemeEsleme)
                if (n.Contains(" " + anahtar + " ") || (anahtar.Length > 5 && n.Replace(" ", "").Contains(anahtar.Replace(" ", ""))))
                    if (!kodlar.Contains(kod)) kodlar.Add(kod);
            if ((n.Contains(" yemek kart") || n.Contains(" yemek ceki")) && !kodlar.Any(k => YemekKartiGenel.Contains(k)))
                kodlar.AddRange(YemekKartiGenel.Where(k => !kodlar.Contains(k)));
        }
        return kodlar;
    }

    // ------------------------------------------------------------------ liste
    static readonly Regex Baslik = new(@"^(#+)\s*(.*)$", RegexOptions.CultureInvariant);

    public static bool BasliksizMi(string metin)
    {
        var satirlar = Py.SplitLines(metin ?? "").Select(Py.Strip).Where(x => x.Length > 0).ToList();
        return satirlar.Count > 0 && !satirlar.Any(x => x.StartsWith("#", StringComparison.Ordinal));
    }

    /// <summary>Liste metni → sıralı kategoriler (porsiyonlar gruplanmış).</summary>
    public static List<Kategori> ListeyiAyristir(string metin)
    {
        var kategoriler = new List<Kategori>();
        var yigin = new List<string>();
        foreach (var satir in Py.SplitLines(metin ?? ""))
        {
            var st = Py.Strip(satir);
            var s = st.StartsWith("#", StringComparison.Ordinal) ? st : Py.Strip(Py.LStrip(st, "-•* "));
            if (s.Length == 0 || s.StartsWith("```", StringComparison.Ordinal) || OdemeSatiriMi(s)) continue;
            var m = Baslik.Match(s);
            if (m.Success)
            {
                int seviye = m.Groups[1].Length;
                var ad0 = GuvenliAd(m.Groups[2].Value);
                if (ad0.Length == 0) ad0 = "Genel";
                if (yigin.Count > seviye - 1) yigin = yigin.Take(seviye - 1).ToList();
                while (yigin.Count < seviye - 1) yigin.Add("Genel");
                yigin.Add(ad0);
                continue;
            }
            var c = SatiriCoz(s);
            var ad = GuvenliAd(c.Ad);
            var arama = Py.Strip(c.Arama);
            if (arama.Length == 0) arama = ad;
            if (ad.Length == 0) continue;
            var yol = yigin.Count > 0 ? string.Join("/", yigin) : "Genel";
            var kat = kategoriler.FirstOrDefault(k => k.Yol == yol);
            if (kat == null) { kat = new Kategori { Yol = yol }; kategoriler.Add(kat); }
            if (!kat.Urunler.Any(u => Py.Lower(u.Ad) == Py.Lower(ad)))
                kat.Urunler.Add(new Urun { Ad = ad, Arama = arama, Fiyat = c.Fiyat, Porsiyonlar = c.Porsiyonlar });
        }
        foreach (var k in kategoriler) k.Urunler = PorsiyonGrupla(k.Urunler);
        return kategoriler.Where(k => k.Urunler.Count > 0).ToList();
    }

    // ------------------------------------------------------------------ yerleşik sınıflandırıcı
    static readonly Dictionary<string, string> Duzelt = new()
    {
        ["havuc"] = "Havuç", ["salatalik"] = "Salatalık", ["mandalin"] = "Mandalina",
        ["kavacik"] = "kavacık", ["sogan"] = "Soğan", ["sarimsak"] = "Sarımsak",
        ["patlican"] = "Patlıcan", ["pirasa"] = "Pırasa", ["ispanak"] = "Ispanak",
    };

    static readonly (string, string[])[] Manav =
    {
        ("Yeşillikler", new[] { "maydanoz", "roka", "dereotu", "nane", "tere", "taze sogan", "fesleğen",
            "feslegen", "semizotu", "kuzukulagi", "reyhan", "kisnis", "taze nane" }),
        ("Sebzeler", new[] { "domates", "salatalik", "biber", "patlican", "kabak", "patates", "sogan",
            "sarimsak", "marul", "havuc", "turp", "lahana", "pirasa", "kereviz", "ispanak",
            "pancar", "fasulye", "bezelye", "bamya", "enginar", "brokoli", "karnabahar",
            "mantar", "misir", "pazi", "bakla", "zencefil", "tatli patates", "kapya",
            "kuskonmaz", "brüksel", "bruksel", "lahana tursusu", "barbunya", "borulce" }),
        ("Meyveler", new[] { "elma", "armut", "portakal", "mandalina", "mandalin", "greyfurt", "limon", "nar",
            "ayva", "muz", "kivi", "hurma", "cilek", "uzum", "mersin", "ananas", "karpuz",
            "kavun", "seftali", "kayisi", "erik", "kiraz", "visne", "incir", "avokado",
            "mango", "nektarin", "dut", "bogurtlen", "ahududu", "frambuaz", "papaya",
            "pitaya", "ejder meyvesi", "hindistan cevizi", "misket limonu", "lime",
            "altin cilek", "yaban mersini", "trabzon hurmasi", "cennet hurmasi" }),
        ("Kuruyemiş", new[] { "findik", "fistik", "ceviz", "badem", "leblebi", "kaju", "cekirdek", "kuru uzum",
            "kuru kayisi", "kuru incir" }),
    };

    static readonly (string, string[])[] Restoran =
    {
        ("Döner", new[] { "doner", "iskender", "durum doner" }),
        ("Burger", new[] { "burger", "hamburger", "cheeseburger" }),
        ("Pizza", new[] { "pizza" }),
        ("Pide & Lahmacun", new[] { "pide", "lahmacun" }),
        ("Kebap", new[] { "kebap", "kebab", "adana", "urfa", "beyti", "sis", "kofte", "kanat", "pirzola", "cop sis" }),
        ("Tavuk", new[] { "tavuk", "nugget", "chicken", "schnitzel" }),
        ("Uzak Doğu", new[] { "ramen", "sushi", "noodle", "wok", "udon", "gyoza", "dumpling" }),
        ("Makarna", new[] { "makarna", "spagetti", "penne", "fettuccine", "lazanya", "manti" }),
        ("Çorbalar", new[] { "corba", "mercimek corbasi", "iskembe" }),
        ("Salatalar", new[] { "salata", "sezar" }),
        ("Tost & Sandviç", new[] { "tost", "sandvic", "wrap", "kumru", "hot dog", "sosisli" }),
        ("Kahvaltı", new[] { "kahvalti", "menemen", "omlet", "sucuklu yumurta", "simit", "pogaca", "borek" }),
        ("Tatlılar", new[] { "tatli", "baklava", "kunefe", "sutlac", "kazandibi", "pasta", "cheesecake",
            "brownie", "dondurma", "tiramisu", "waffle", "puding", "katmer", "trilece" }),
        ("Alkollü İçecekler", new[] { "bira", "efes", "tuborg", "carlsberg", "rakı", "raki", "sarap", "vodka",
            "viski", "whisky", "cin", "gin", "tekila", "kokteyl", "bomonti", "corona" }),
        ("İçecekler", new[] { "kola", "cola", "fanta", "sprite", "ayran", "su", "soda", "gazoz", "meyve suyu",
            "limonata", "cay", "kahve", "latte", "americano", "espresso", "ice tea", "salgam",
            "redbull", "red bull", "enerji", "milkshake", "smoothie", "nescafe", "salep" }),
        ("Yan Ürünler", new[] { "patates kizartmasi", "cips", "sogan halkasi", "sos", "ekmek", "pilav", "tursu" }),
    };

    static readonly (string, string[])[] Pisme =
    {
        ("Kızartma", new[] { "kizartma", "crispy", "citir", "nugget", "kanat", "schnitzel", "patates kizartmasi",
            "sogan halkasi", "cips" }),
        ("Izgara", new[] { "izgara", "burger", "kebap", "kebab", "adana", "urfa", "sis", "kofte", "pirzola",
            "doner", "iskender", "tavuk" }),
        ("Fırın", new[] { "firin", "pizza", "pide", "lahmacun", "lazanya", "borek", "pogaca", "simit" }),
        ("Haşlama", new[] { "ramen", "makarna", "spagetti", "noodle", "manti", "corba", "udon" }),
        ("Tava", new[] { "tava", "wok", "menemen", "omlet", "sote" }),
        ("Soğuk", new[] { "salata", "sezar", "cacik", "humus", "meze", "sushi" }),
    };

    static (string, int) EnUzun(string urunN, (string, string[])[] sozluk)
    {
        string en = null; int uzunluk = 0;
        var kelimeler = " " + urunN + " ";
        foreach (var (anahtar, liste) in sozluk)
            foreach (var k in liste)
            {
                var kn = Norm(k);
                if ((kelimeler.Contains(" " + kn + " ") || (urunN.Contains(kn) && kn.Length >= 5)) && kn.Length > uzunluk)
                { en = anahtar; uzunluk = kn.Length; }
            }
        return (en, uzunluk);
    }

    static readonly Regex TekNokta = new(@"(?<!\d)\.|\.(?!\d)", RegexOptions.CultureInvariant);

    public static string DuzeltAd(string ad)
    {
        var parcalar = new List<string>();
        foreach (var kelime0 in Py.SplitWs(TekNokta.Replace(ad ?? "", " ")))
        {
            var kelime = kelime0;
            if (Duzelt.TryGetValue(Norm(kelime), out var d))
                kelime = kelime.Length > 0 && char.IsUpper(kelime[0]) ? d : Py.Lower(d);
            parcalar.Add(kelime);
        }
        return string.Join(" ", parcalar);
    }

    /// <summary>Başlıksız listeyi internetsiz olarak kategorilere ayırır.</summary>
    public static string YerelSiniflandir(string metin)
    {
        var odemeSatirlari = new List<string>();
        var gruplar = new Dictionary<string, List<Urun>>();
        var sira = new List<string[]>();
        var gorulen = new HashSet<string>();
        foreach (var satir in Py.SplitLines(metin ?? ""))
        {
            var s = Py.Strip(Py.LStrip(Py.Strip(satir), "-•* "));
            if (s.Length == 0 || s.StartsWith("#", StringComparison.Ordinal)) continue;
            if (OdemeSatiriMi(s)) { odemeSatirlari.Add(s); continue; }
            var c = SatiriCoz(s);
            var ad = DuzeltAd(GuvenliAd(c.Ad));
            if (ad.Length == 0 || gorulen.Contains(Norm(ad))) continue;
            gorulen.Add(Norm(ad));
            var n = Norm(ad);
            var ay = PorsiyonAyir(ad);
            if (ay != null) n = Norm(ay.Kok);
            var (manav, mUz) = EnUzun(n, Manav);
            var (restoran, rUz) = EnUzun(n, Restoran);
            string[] yol;
            if (manav != null && mUz >= rUz) yol = new[] { manav };
            else if (restoran != null)
            {
                var pisme = EnUzun(n, Pisme).Item1 ??
                            (restoran is "İçecekler" or "Alkollü İçecekler" or "Tatlılar" ? "Hazır" : "Diğer");
                var alt = (" " + n + " ").Contains(" menu ") || n.Contains("menusu") ? "Menü" : "Sadece Yemek";
                yol = new[] { restoran, pisme, alt };
            }
            else yol = new[] { "Diğer" };
            var anahtar = string.Join("\u0001", yol);
            if (!gruplar.ContainsKey(anahtar)) { gruplar[anahtar] = new List<Urun>(); sira.Add(yol); }
            var arama = Py.Strip(c.Arama);
            gruplar[anahtar].Add(new Urun { Ad = ad, Arama = arama.Length > 0 ? arama : ad, Fiyat = c.Fiyat, Porsiyonlar = c.Porsiyonlar });
        }

        (int, int) IlkGorulme(string[] y)
        {
            for (int i = 0; i < sira.Count; i++)
                if (sira[i][0] == y[0]) return (i, sira.IndexOf(y));
            return (sira.Count, 0);
        }
        var sirali = sira.Select((y, i) => (y, i))
            .OrderBy(t => t.y.Length == 1 && t.y[0] == "Diğer" ? 1 : 0)
            .ThenBy(t => IlkGorulme(t.y).Item1).ThenBy(t => IlkGorulme(t.y).Item2).ThenBy(t => t.i)
            .Select(t => t.y).ToList();

        var cikti = new List<string>();
        string[] onceki = Array.Empty<string>();
        foreach (var yol in sirali)
        {
            for (int seviye = 0; seviye < yol.Length; seviye++)
            {
                if (!onceki.Take(seviye + 1).SequenceEqual(yol.Take(seviye + 1)))
                {
                    if (seviye == 0 && cikti.Count > 0) cikti.Add("");
                    cikti.Add(new string('#', seviye + 1) + " " + yol[seviye]);
                }
            }
            foreach (var u in PorsiyonGrupla(gruplar[string.Join("\u0001", yol)]))
            {
                var satir = u.Ad;
                bool fiyatVar = u.Fiyat.HasValue && u.Fiyat.Value != 0;
                if (fiyatVar || u.Porsiyonlar.Count > 0 || PorsNorm(u.Arama) != PorsNorm(u.Ad)) satir += " | " + u.Arama;
                if (fiyatVar || u.Porsiyonlar.Count > 0) satir += " | " + (fiyatVar ? Py.G(u.Fiyat.Value) : "");
                if (u.Porsiyonlar.Count > 0) satir += " | " + PorsiyonlariYaz(u.Porsiyonlar);
                cikti.Add(satir);
            }
            onceki = yol;
        }
        if (odemeSatirlari.Count > 0) { cikti.Add(""); cikti.AddRange(odemeSatirlari); }
        return string.Join("\n", cikti);
    }

    // ------------------------------------------------------------------ liste ↔ broşür karşılaştırma
    static readonly Regex Kelime3 = new("[a-z0-9]{3,}", RegexOptions.CultureInvariant);
    static readonly Rx PorsiyonSatiri = new(@"^(?:" + PorsKelime + @"|bu[cç]uk|[0-9 ]+)(?:\s*(?:porsiyon|pors|parca|gram|gr))*$", true);
    static readonly HashSet<string> EksikDisi = new() { "gram", "porsiyon", "yarim", "ekmek", "parca", "tek", "tam",
        "bucuk", "duble", "double", "buyuk", "kucuk", "orta", "cift" };

    /// <summary>Yazılı listede fiyatıyla geçip sonuçta bulunamayan satırlar.</summary>
    public static List<string> ListeEksikKontrol(string listeMetni, string sonucMetni)
    {
        var urunler = ListeyiAyristir(sonucMetni).SelectMany(k => k.Urunler).Select(u => Norm(u.Ad)).ToList();
        var kelimeler = urunler.Select(u => Kelime3.Matches(u).Select(m => m.Value).ToHashSet()).ToList();
        var eksik = new List<string>();
        foreach (var satir in Py.SplitLines(listeMetni ?? ""))
        {
            var s = Py.Strip(Py.LStrip(Py.Strip(satir), "-•*·"));
            if (s.Length == 0 || s.StartsWith("#", StringComparison.Ordinal) || OdemeSatiriMi(s)) continue;
            var c = SatiriCoz(s);
            if (!c.Fiyat.HasValue || c.Fiyat.Value == 0) continue;
            var n = Norm(c.Ad);
            if (n.Length == 0 || PorsiyonSatiri.IsMatch(n)) continue;
            var k = Kelime3.Matches(n).Select(m => m.Value).ToHashSet();
            k.ExceptWith(EksikDisi);
            if (k.Count == 0) continue;
            double enIyi = 0;
            foreach (var w in kelimeler) enIyi = Math.Max(enIyi, (double)k.Count(w.Contains) / k.Count);
            if (enIyi < 0.75) eksik.Add(c.Ad);
        }
        return eksik;
    }

    // ------------------------------------------------------------------ yardımcı
    /// <summary>Kategorileri arama terimleriyle birlikte liste metnine çevirir ("Ad | arama | fiyat | porsiyonlar").</summary>
    public static string ListeYaz(List<Kategori> kategoriler)
    {
        var cikti = new List<string>();
        string[] onceki = Array.Empty<string>();
        foreach (var k in kategoriler)
        {
            var yol = k.Yol.Split('/');
            for (int s = 0; s < yol.Length; s++)
                if (!onceki.Take(s + 1).SequenceEqual(yol.Take(s + 1)))
                {
                    if (s == 0 && cikti.Count > 0) cikti.Add("");
                    cikti.Add(new string('#', s + 1) + " " + yol[s]);
                }
            foreach (var u in k.Urunler)
            {
                var satir = u.Ad;
                bool fv = u.Fiyat.HasValue && u.Fiyat.Value != 0;
                if (fv || u.Porsiyonlar.Count > 0 || PorsNorm(u.Arama) != PorsNorm(u.Ad)) satir += " | " + u.Arama;
                if (fv || u.Porsiyonlar.Count > 0) satir += " | " + (fv ? Py.G(u.Fiyat.Value) : "");
                if (u.Porsiyonlar.Count > 0) satir += " | " + PorsiyonlariYaz(u.Porsiyonlar);
                cikti.Add(satir);
            }
            onceki = yol;
        }
        return string.Join("\n", cikti);
    }

    public static string KategorileriYaz(List<Kategori> kategoriler)
    {
        // Kategorileri yeniden liste metnine çevirir (klasör/zip taramasından gelenler için).
        var cikti = new List<string>();
        string[] onceki = Array.Empty<string>();
        foreach (var k in kategoriler)
        {
            var yol = k.Yol.Split('/');
            for (int s = 0; s < yol.Length; s++)
                if (!onceki.Take(s + 1).SequenceEqual(yol.Take(s + 1)))
                {
                    if (s == 0 && cikti.Count > 0) cikti.Add("");
                    cikti.Add(new string('#', s + 1) + " " + yol[s]);
                }
            foreach (var u in k.Urunler)
            {
                var satir = u.Ad;
                bool fv = u.Fiyat.HasValue && u.Fiyat.Value != 0;
                if (fv || u.Porsiyonlar.Count > 0) satir += $" | {u.Ad} | {(fv ? Py.G(u.Fiyat.Value) : "")}";
                if (u.Porsiyonlar.Count > 0) satir += " | " + PorsiyonlariYaz(u.Porsiyonlar);
                cikti.Add(satir);
            }
            onceki = yol;
        }
        return string.Join("\n", cikti);
    }
}
