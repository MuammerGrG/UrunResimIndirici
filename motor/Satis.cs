using System.Text;
using System.Text.RegularExpressions;
using SkiaSharp;

namespace UrunMotor;

/// <summary>
/// SiparişMatik Satış (market / perakende, MySQL 5.5 / MySQL-Front) yedeğini doldurur.
///   tbl_urunkategori      → kategoriler
///   tbl_urun              → ürünler (barkodsuz: "002063" + 6 haneli ürün no), porsiyonlar ayrı ürün
///   tbl_hizlikategori     → hızlı satış buton grupları (kategorilerle aynı)
///   tbl_hizlikategoriurun → hızlı satış butonları; resim 217×123 JPEG'in base64 metni (blob)
/// Eski ürünler, kategoriler ve butonlar silinir.
/// </summary>
public static class Satis
{
    public const string KategoriTablo = "tbl_urunkategori";
    public const string UrunTablo = "tbl_urun";
    public const string HizliKategoriTablo = "tbl_hizlikategori";
    public const string HizliUrunTablo = "tbl_hizlikategoriurun";
    public const string BarkodOnEk = "002063";
    public const int ButonGenislik = 217, ButonYukseklik = 123;
    const string Personel = "SiparişMatik";

    static Satis() { Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); }

    /// <summary>Ürün resmini hızlı satış butonu boyutuna (217×123) getirip base64 metninin hex'i: X'...'</summary>
    public static string ButonResmi(string yol)
    {
        if (string.IsNullOrEmpty(yol) || !File.Exists(yol)) return "NULL";
        using var img = Resim.Coz(File.ReadAllBytes(yol));
        if (img == null) return "NULL";
        double oran = Math.Min((double)ButonGenislik / img.Width, (double)ButonYukseklik / img.Height);
        int w = Math.Max(1, (int)Math.Round(img.Width * oran)), h = Math.Max(1, (int)Math.Round(img.Height * oran));
        using var tuval = new SKBitmap(ButonGenislik, ButonYukseklik);
        using (var c = new SKCanvas(tuval))
        {
            c.Clear(SKColors.White);
            using var p = new SKPaint { FilterQuality = SKFilterQuality.High, IsAntialias = true };
            c.DrawBitmap(img, SKRect.Create((ButonGenislik - w) / 2, (ButonYukseklik - h) / 2, w, h), p);
        }
        var b64 = Convert.ToBase64String(Resim.Jpeg(tuval, 85));
        return "X'" + Convert.ToHexString(Encoding.ASCII.GetBytes(b64)) + "'";
    }

    /// <summary>"1,5" gibi yalnız sayı olan porsiyon etiketine "PORSİYON" eklenir.</summary>
    public static string PorsiyonluAd(string ad, string etiket) =>
        Regex.IsMatch(etiket, @"^[\d,.½ ]+$") ? $"{ad} {etiket} PORSİYON" : $"{ad} {etiket}";

    public sealed record Sonuc(int Kategori, int Urun, int Buton, List<string> Eksik);

    public static Sonuc Olustur(string dumpYolu, List<Kategori> kategoriler, string resimKok, string ciktiYolu,
        double fiyat = 1.0, int kategoriSeviye = 1, Action<string> log = null, Dictionary<string, string> gercekKlasorler = null,
        CancellationToken iptal = default)
    {
        log ??= _ => { };
        if (fiyat <= 0) throw new HataMesaji("Fiyat 0 veya eksi olamaz (yazar kasa kabul etmez).");
        var (dump, crlf) = Sql.DumpOku(dumpYolu);
        List<Sutun> Sut(string t)
        {
            try { return Sql.SutunlariBul(dump, t); }
            catch (HataMesaji) { throw new HataMesaji($"Seçilen dosya SiparişMatik Satış yedeği değil ({t} tablosu yok)."); }
        }
        var kSut = Sut(KategoriTablo); var uSut = Sut(UrunTablo); var hkSut = Sut(HizliKategoriTablo); var huSut = Sut(HizliUrunTablo);
        if (!uSut.Any(s => s.Ad == "urunbarkod"))
            throw new HataMesaji("Seçilen dosya SiparişMatik Satış yedeği değil (tbl_urun'da urunbarkod yok). POS yedeği için 'SiparişMatik (.sql)' seçeneğini kullan.");

        var (katSira, katUrunler) = Sql.KategorileriTopla(kategoriler, resimKok, kategoriSeviye, gercekKlasorler);
        var hex = (Func<string, string>)Metin.HexMetin;
        var personel = hex(Personel);
        var kSatir = new List<string>(); var uSatir = new List<string>(); var hkSatir = new List<string>(); var huSatir = new List<string>();
        int uid = 0, bid = 0, fiyatli = 0, varsayilanFiyatli = 0;
        var eksik = new List<string>();

        for (int kid = 1; kid <= katSira.Count; kid++)
        {
            iptal.ThrowIfCancellationRequested();
            var katAd = Metin.TrBuyuk(katSira[kid - 1]);
            if (katAd.Length > 100) katAd = katAd[..100];
            kSatir.Add(Sql.InsertSatiri(KategoriTablo, kSut, new()
            {
                ["kategoriid"] = kid.ToString(), ["kategoriad"] = hex(katAd), ["kategoriaciklama"] = "NULL",
                ["kategoriindirim"] = "'0'", ["kategorikomisyon"] = "'0'", ["kategorizaman"] = "NOW()", ["kategoripersonel"] = personel,
                ["kategorisil"] = "'N'", ["kategorisilzaman"] = "NULL", ["kategorisilpersonel"] = "NULL",
            }));
            hkSatir.Add(Sql.InsertSatiri(HizliKategoriTablo, hkSut, new()
            {
                ["hizlikategoriid"] = kid.ToString(), ["hizlikategoriad"] = hex(katAd), ["hizlikategorizaman"] = "NOW()",
                ["hizlikategoripersonel"] = personel, ["hizlikategorisil"] = "'N'", ["hizlikategorisilzaman"] = "NULL",
                ["hizlikategorisilpersonel"] = "NULL",
            }));

            foreach (var u in katUrunler[katSira[kid - 1]])
            {
                double uFiyat;
                if (u.Fiyat.HasValue && u.Fiyat.Value > 0) { fiyatli++; uFiyat = u.Fiyat.Value; } else { varsayilanFiyatli++; uFiyat = fiyat; }
                var r = Sql.ResimBul(u.Klasor, u.ResimAdi);
                if (r == null) eksik.Add(u.Ad);
                var buton = r != null ? ButonResmi(r) : "NULL";
                var satirlar = new List<(string ad, double fiyat)> { (u.Ad, uFiyat) };
                foreach (var p in u.Porsiyonlar)
                    satirlar.Add((PorsiyonluAd(u.Ad, p.Ad), p.Fiyat.HasValue && p.Fiyat.Value > 0 ? p.Fiyat.Value : uFiyat));
                foreach (var (ad0, f) in satirlar)
                {
                    uid++;
                    var ad = Metin.TrBuyuk(ad0);
                    if (ad.Length > 100) ad = ad[..100];
                    uSatir.Add(Sql.InsertSatiri(UrunTablo, uSut, new()
                    {
                        ["urunid"] = uid.ToString(), ["urunkategoriid"] = kid.ToString(), ["urunkategoriad"] = hex(katAd),
                        ["urunbarkod"] = $"'{BarkodOnEk}{uid:D6}'", ["urunad"] = hex(ad), ["urunaciklama"] = "NULL",
                        ["urunbirim"] = "'ADET'", ["urunfiyat"] = Metin.Sayi(f), ["urunadet"] = "0", ["urunindirim"] = "'0'",
                        ["urunkomisyon"] = "'0'", ["urunkampanya"] = "NULL", ["urunzaman"] = "NOW()", ["urunpersonel"] = personel,
                        ["urunsil"] = "'N'", ["urunsilzaman"] = "NULL", ["urunsilpersonel"] = "NULL",
                    }));
                    bid++;
                    huSatir.Add(Sql.InsertSatiri(HizliUrunTablo, huSut, new()
                    {
                        ["hizlikategoriurunid"] = bid.ToString(), ["hizlikategoriid"] = kid.ToString(), ["hizliurunid"] = uid.ToString(),
                        ["hizliurunad"] = hex(ad), ["hizliurunresim"] = buton, ["hizliurunzaman"] = "NOW()", ["hizliurunpersonel"] = personel,
                        ["hizliurunsil"] = "'N'", ["hizliurunsilzaman"] = "NULL", ["hizliurunsilpersonel"] = "NULL",
                    }));
                }
            }
        }

        // eski veriler silinir, yenileri yazılır
        foreach (var (tablo, satirlar) in new[] { (KategoriTablo, kSatir), (UrunTablo, uSatir), (HizliKategoriTablo, hkSatir), (HizliUrunTablo, huSatir) })
            dump = Sql.BolumuDoldur(dump, tablo, satirlar);
        dump = Sql.EskiInsertleriDuzelt(dump);
        foreach (var (tablo, n) in new[] { (KategoriTablo, katSira.Count), (UrunTablo, uid), (HizliKategoriTablo, katSira.Count), (HizliUrunTablo, bid) })
            dump = Regex.Replace(dump, @"(CREATE TABLE\s+`?" + Regex.Escape(tablo) + @"`?\s*\(.*?\n\)[^;]*?AUTO_INCREMENT=)\d+",
                m => m.Groups[1].Value + (n + 1), RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.Singleline);

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(ciktiYolu)));
        if (crlf) dump = dump.Replace("\n", "\r\n");
        var cp = Encoding.GetEncoding(1254, new EncoderReplacementFallback("?"), new DecoderReplacementFallback("?"));
        File.WriteAllBytes(ciktiYolu, cp.GetBytes(dump));
        log($"Satış SQL'i yazıldı: {ciktiYolu}  ({katSira.Count} kategori, {uid} ürün, {bid} hızlı satış butonu; " +
            $"{fiyatli} ürün menü fiyatıyla, {varsayilanFiyatli} ürün {Metin.Sayi(fiyat)} TL)");
        log("Eski ürünler, kategoriler ve hızlı satış butonları silindi. Barkodsuz ürünlerin barkodu: " + BarkodOnEk + "xxxxxx");
        if (eksik.Count > 0)
            log($"Resmi olmayan {eksik.Count} ürün (butonu resimsiz): " + string.Join(", ", eksik.Take(15)) + (eksik.Count > 15 ? " ..." : ""));
        return new Sonuc(katSira.Count, uid, bid, eksik);
    }
}
