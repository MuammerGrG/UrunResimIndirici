using System.Text.RegularExpressions;
using ClosedXML.Excel;

namespace UrunMotor;

/// <summary>Müşterinin Excel ürün listesini okur; hazırlanan menüyü Excel'e yazar.</summary>
public static class Excel
{
    static readonly string[] XAd = { "urun", "ürün", "ad", "adi", "adı", "isim", "ismi", "name", "product", "aciklama", "açıklama", "menu", "menü" };
    static readonly string[] XFiyat = { "fiyat", "fiyatı", "price", "tutar", "tl", "satis", "satış", "₺" };
    static readonly string[] XKat = { "kategori", "kategorisi", "grup", "grubu", "category", "bolum", "bölüm", "tur", "tür" };
    static readonly string[] XPisme = { "pisme", "pişme" };
    static readonly string[] XTip = { "tip", "tipi" };
    static readonly string[] XPors = { "porsiyon", "porsiyonlar", "portion", "boy", "secenek", "seçenek" };

    static readonly Regex Kelime = new("[a-z0-9]+", RegexOptions.CultureInvariant);
    static readonly Rx SadeceFiyat = new(@"^[₺\d.,\s]+(tl)?$", true);

    /// <summary>Python _x_hucre: hücre değeri → metin</summary>
    static string Hucre(XLCellValue v)
    {
        if (v.IsBlank) return "";
        if (v.IsNumber)
        {
            var d = v.GetNumber();
            return d == Math.Floor(d) && Math.Abs(d) < 1e15 ? ((long)d).ToString(Py.CI) : Py.FloatRepr(d);
        }
        if (v.IsBoolean) return v.GetBoolean() ? "True" : "False";
        if (v.IsDateTime) return v.GetDateTime().ToString("yyyy-MM-dd HH:mm:ss", Py.CI);
        if (v.IsTimeSpan) return v.GetTimeSpan().ToString();
        if (v.IsError) return "#" + v.GetError();
        return Py.Strip(v.GetText());
    }

    static (int? satir, Dictionary<string, int> es) BaslikBul(List<string[]> satirlar)
    {
        for (int r = 0; r < Math.Min(10, satirlar.Count); r++)
        {
            var es = new Dictionary<string, int>();
            var norm = satirlar[r].Select(Metin.Norm).ToList();
            for (int i = 0; i < norm.Count; i++)
            {
                if (norm[i].Length == 0) continue;
                var kelimeler = Kelime.Matches(norm[i]).Select(m => m.Value).ToHashSet();
                foreach (var (anahtar, liste) in new[] { ("kat", XKat), ("pisme", XPisme), ("tip", XTip), ("pors", XPors), ("fiyat", XFiyat), ("ad", XAd) })
                {
                    if (!es.ContainsKey(anahtar) && liste.Select(Metin.Norm).Any(kelimeler.Contains))
                    {
                        es[anahtar] = i;
                        break;
                    }
                }
            }
            if (es.ContainsKey("ad") && (es.ContainsKey("fiyat") || es.ContainsKey("kat"))) return (r, es);
        }
        return (null, new());
    }

    public static string Oku(string yol)
    {
        if (yol.EndsWith(".xls", StringComparison.OrdinalIgnoreCase))
            throw new HataMesaji("Eski .xls biçimi okunamıyor: Excel'de 'Farklı Kaydet → .xlsx' yapıp tekrar seç.");
        using var kitap = new XLWorkbook(yol);
        var sayfalar = kitap.Worksheets.Where(w => w.Visibility == XLWorksheetVisibility.Visible).ToList();
        var cikti = new List<string>();
        bool kategorili = false;
        foreach (var ws in sayfalar)
        {
            var sonSatir = ws.LastRowUsed()?.RowNumber() ?? 0;
            var sonSutun = ws.LastColumnUsed()?.ColumnNumber() ?? 0;
            var satirlar = new List<string[]>();
            for (int r = 1; r <= sonSatir; r++)
            {
                var a = new string[sonSutun];
                for (int c = 1; c <= sonSutun; c++) { var h = ws.Cell(r, c); a[c - 1] = Hucre(h.HasFormula ? h.CachedValue : h.Value); }
                if (a.Any(x => x.Length > 0)) satirlar.Add(a);
            }
            if (satirlar.Count == 0) continue;
            var (bas, es) = BaslikBul(satirlar);
            var veri = bas.HasValue ? satirlar.Skip(bas.Value + 1).ToList() : satirlar;
            if (!es.ContainsKey("ad"))
            {
                int? yazi = null, sayiS = null;
                int enCok = veri.Count > 0 ? veri.Max(r => r.Length) : 0;
                for (int i = 0; i < enCok; i++)
                {
                    var degerler = veri.Where(r => i < r.Length && r[i].Length > 0).Select(r => r[i]).ToList();
                    if (degerler.Count == 0) continue;
                    int sayisal = degerler.Count(d => Metin.FiyatCoz(d).HasValue && SadeceFiyat.IsMatch(d));
                    if (sayiS == null && sayisal >= degerler.Count * 0.6) sayiS = i;
                    else if (yazi == null && sayisal < degerler.Count * 0.4) yazi = i;
                }
                if (yazi == null) continue;
                es = new Dictionary<string, int> { ["ad"] = yazi.Value };
                if (sayiS != null) es["fiyat"] = sayiS.Value;
            }
            var sayfaKat = sayfalar.Count > 1 && !es.ContainsKey("kat") ? Metin.GuvenliAd(ws.Name) : "";
            (string, string, string)? sonKat = null;
            if (sayfaKat.Length > 0) { cikti.Add(""); cikti.Add("# " + sayfaKat); kategorili = true; }
            foreach (var r in veri)
            {
                string Al(string k) => es.TryGetValue(k, out var i) && i < r.Length ? r[i] : "";
                var ad = Al("ad");
                if (ad.Length == 0) continue;
                double? fiyat = Al("fiyat").Length > 0 ? Metin.FiyatCoz(Al("fiyat")) : null;
                if (es.ContainsKey("kat") && Al("kat").Length > 0)
                {
                    var anahtar = (Metin.GuvenliAd(Al("kat")), Metin.GuvenliAd(Al("pisme")), Metin.GuvenliAd(Al("tip")));
                    var (kat, pisme, tip) = anahtar;
                    if (sonKat == null || sonKat.Value != anahtar)
                    {
                        if (sonKat == null || sonKat.Value.Item1 != kat) { cikti.Add(""); cikti.Add("# " + kat); }
                        if (pisme.Length > 0 && (sonKat == null || (sonKat.Value.Item1, sonKat.Value.Item2) != (kat, pisme))) cikti.Add("## " + pisme);
                        if (tip.Length > 0) cikti.Add(pisme.Length > 0 ? "### " + tip : "## Hazır\n### " + tip);
                        sonKat = anahtar;
                        kategorili = true;
                    }
                }
                var satir = ad;
                var pors = Al("pors");
                bool fv = fiyat.HasValue && fiyat.Value != 0;
                if (fv || pors.Length > 0)
                {
                    satir = $"{ad} | {ad} | {(fv ? Py.G(fiyat.Value) : "")}";
                    if (pors.Length > 0) satir += " | " + pors;
                }
                cikti.Add(satir);
            }
        }
        var metin = Py.Strip(string.Join("\n", cikti));
        if (metin.Length == 0) throw new HataMesaji("Excel'de ürün bulunamadı.");
        return metin;
    }

    public static int Yaz(List<Kategori> kategoriler, string resimKok, string hedef, string firma = "")
    {
        using var kitap = new XLWorkbook();
        var ws = kitap.Worksheets.Add("Menü");
        var basliklar = new[] { "Resim", "Kategori", "Pişme Şekli", "Tip", "Ürün", "Fiyat (TL)", "Porsiyonlar" };
        int bas = 1;
        if (!string.IsNullOrEmpty(firma))
        {
            ws.Cell(1, 1).Value = firma;
            ws.Cell(1, 1).Style.Font.SetBold().Font.SetFontSize(14);
            bas = 3;
        }
        for (int i = 0; i < basliklar.Length; i++)
        {
            var h = ws.Cell(bas, i + 1);
            h.Value = basliklar[i];
            h.Style.Font.SetBold().Font.SetFontColor(XLColor.White)
                .Fill.SetBackgroundColor(XLColor.FromHtml("#2E7D32"))
                .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center).Alignment.SetVertical(XLAlignmentVerticalValues.Center);
        }
        var genislik = new double[] { 11, 22, 14, 14, 38, 11, 34 };
        for (int i = 0; i < genislik.Length; i++) ws.Column(i + 1).Width = genislik[i];
        ws.SheetView.FreezeRows(bas);
        int satir = bas + 1;
        foreach (var k in kategoriler)
        {
            var parca = k.Yol.Split('/').Concat(new[] { "", "", "" }).ToArray();
            foreach (var u in k.Urunler)
            {
                ws.Cell(satir, 2).Value = Metin.TrBuyuk(parca[0]);
                ws.Cell(satir, 3).Value = parca[1];
                ws.Cell(satir, 4).Value = parca[2];
                ws.Cell(satir, 5).Value = Metin.TrBuyuk(u.Ad);
                if (u.Fiyat.HasValue) ws.Cell(satir, 6).Value = u.Fiyat.Value;
                ws.Cell(satir, 7).Value = string.Join("; ", u.Porsiyonlar.Select(p =>
                    p.Fiyat.HasValue && p.Fiyat.Value != 0 ? $"{Metin.TrBuyuk(p.Ad)} = {Py.G(p.Fiyat.Value)} TL" : Metin.TrBuyuk(p.Ad)));
                for (int i = 1; i <= 7; i++)
                {
                    ws.Cell(satir, i).Style.Alignment.SetVertical(XLAlignmentVerticalValues.Center);
                    if (i == 7) ws.Cell(satir, i).Style.Alignment.SetWrapText(true);
                }
                ws.Cell(satir, 6).Style.NumberFormat.Format = "#,##0.00";
                var r = Sql.ResimBul(Sql.KlasorYolu(resimKok, k.Yol), u.Ad);
                if (r != null)
                {
                    try
                    {
                        var kucuk = Resim.Kucuk(r, 70, png: true);
                        if (kucuk != null)
                        {
                            ws.AddPicture(new MemoryStream(kucuk)).MoveTo(ws.Cell(satir, 1), 2, 2);
                            ws.Row(satir).Height = 56;
                        }
                    }
                    catch { }
                }
                satir++;
            }
        }
        ws.Range(bas, 1, Math.Max(bas, satir - 1), 7).SetAutoFilter();
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(hedef)));
        kitap.SaveAs(hedef);
        return satir - bas - 1;
    }
}
