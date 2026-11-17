using SkiaSharp;

namespace UrunMotor;

/// <summary>600×600 JPEG üretimi, SQL için sıkıştırma, yedek görsel ve broşürden kesme.</summary>
public static class Resim
{
    public const int Boyut = 600;
    public const int MaksResim = 70 * 1024;
    public static readonly string[] Uzantilar = { ".jpg", ".jpeg", ".png", ".webp", ".gif", ".bmp" };

    static void Ciz(SKCanvas c, SKBitmap kaynak, SKRect hedef)
    {
        using var p = new SKPaint { FilterQuality = SKFilterQuality.High, IsAntialias = true };
        c.DrawBitmap(kaynak, hedef, p);
    }

    public static bool ResimMi(string yol) => Uzantilar.Contains(Path.GetExtension(yol ?? "").ToLowerInvariant());

    public static SKBitmap Coz(byte[] veri)
    {
        try
        {
            using var kod = SKCodec.Create(new MemoryStream(veri));
            if (kod == null) return null;
            var bilgi = new SKImageInfo(kod.Info.Width, kod.Info.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
            var bmp = new SKBitmap(bilgi);
            var sonuc = kod.GetPixels(bilgi, bmp.GetPixels());
            if (sonuc != SKCodecResult.Success && sonuc != SKCodecResult.IncompleteInput) { bmp.Dispose(); return null; }
            // EXIF yönü (telefon fotoğrafları)
            return Dondur(bmp, kod.EncodedOrigin);
        }
        catch { return null; }
    }

    static SKBitmap Dondur(SKBitmap bmp, SKEncodedOrigin yon)
    {
        if (yon == SKEncodedOrigin.TopLeft || yon == SKEncodedOrigin.Default) return bmp;
        bool yanlama = yon is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        var yeni = new SKBitmap(yanlama ? bmp.Height : bmp.Width, yanlama ? bmp.Width : bmp.Height);
        using (var c = new SKCanvas(yeni))
        {
            switch (yon)
            {
                case SKEncodedOrigin.BottomRight: c.RotateDegrees(180, bmp.Width / 2f, bmp.Height / 2f); break;
                case SKEncodedOrigin.RightTop: c.Translate(yeni.Width, 0); c.RotateDegrees(90); break;
                case SKEncodedOrigin.LeftBottom: c.Translate(0, yeni.Height); c.RotateDegrees(270); break;
                case SKEncodedOrigin.TopRight: c.Scale(-1, 1, bmp.Width / 2f, 0); break;
                case SKEncodedOrigin.BottomLeft: c.Scale(1, -1, 0, bmp.Height / 2f); break;
                case SKEncodedOrigin.LeftTop: c.Translate(yeni.Width, 0); c.RotateDegrees(90); c.Scale(1, -1, 0, bmp.Height / 2f); break;
                case SKEncodedOrigin.RightBottom: c.Translate(0, yeni.Height); c.RotateDegrees(270); c.Scale(1, -1, 0, bmp.Height / 2f); break;
            }
            c.DrawBitmap(bmp, 0, 0);
        }
        bmp.Dispose();
        return yeni;
    }

    /// <summary>Resmi oran korunarak 600×600 beyaz zemine ortalar (büyütme dahil).</summary>
    public static SKBitmap Kare(SKBitmap img, bool buyut = true)
    {
        double oran = Math.Min((double)Boyut / img.Width, (double)Boyut / img.Height);
        if (!buyut) oran = Math.Min(oran, 1.0);
        int w = Math.Max(1, (int)Math.Round(img.Width * oran, MidpointRounding.ToEven));
        int h = Math.Max(1, (int)Math.Round(img.Height * oran, MidpointRounding.ToEven));
        var tuval = new SKBitmap(Boyut, Boyut, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var c = new SKCanvas(tuval);
        c.Clear(SKColors.White);
        Ciz(c, img, SKRect.Create((Boyut - w) / 2, (Boyut - h) / 2, w, h));
        return tuval;
    }

    public static byte[] Jpeg(SKBitmap bmp, int kalite)
    {
        using var img = SKImage.FromBitmap(bmp);
        using var d = img.Encode(SKEncodedImageFormat.Jpeg, kalite);
        return d.ToArray();
    }

    /// <summary>kare_jpeg_kaydet: 200 px altını reddeder.</summary>
    public static bool KareJpegKaydet(byte[] veri, string hedef, bool kucukDeOlur = false)
    {
        using var img = Coz(veri);
        if (img == null) return false;
        if (!kucukDeOlur && (img.Width < 200 || img.Height < 200)) return false;
        using var kare = Kare(img);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(hedef)));
        File.WriteAllBytes(hedef, Jpeg(kare, 90));
        return true;
    }

    /// <summary>SQL için: 600×600, 70 KB altına sıkıştırılmış JPEG → "0x..." ya da "NULL"</summary>
    public static string ResimHex(string yol)
    {
        if (string.IsNullOrEmpty(yol) || !File.Exists(yol)) return "NULL";
        using var img = Coz(File.ReadAllBytes(yol));
        if (img == null) return "NULL";
        using var kare = img.Width == Boyut && img.Height == Boyut ? img.Copy() : Kare(img, buyut: false);
        byte[] b = null;
        foreach (var q in new[] { 85, 78, 70, 62, 55, 48, 40, 32, 25, 18, 12 })
        {
            b = Jpeg(kare, q);
            if (b.Length <= MaksResim) break;
        }
        return "0x" + Convert.ToHexString(b);
    }

    /// <summary>Küçük önizleme (galeri / Excel) — PNG ya da JPEG baytları.</summary>
    public static byte[] Kucuk(string yol, int boy, bool png = false)
    {
        using var img = Coz(File.ReadAllBytes(yol));
        if (img == null) return null;
        double oran = Math.Min(1.0, Math.Min((double)boy / img.Width, (double)boy / img.Height));
        int w = Math.Max(1, (int)Math.Round(img.Width * oran)), h = Math.Max(1, (int)Math.Round(img.Height * oran));
        using var k = new SKBitmap(w, h);
        using (var c = new SKCanvas(k))
        {
            c.Clear(SKColors.White);
            Ciz(c, img, SKRect.Create(0, 0, w, h));
        }
        using var im = SKImage.FromBitmap(k);
        using var d = im.Encode(png ? SKEncodedImageFormat.Png : SKEncodedImageFormat.Jpeg, 82);
        return d.ToArray();
    }

    // ------------------------------------------------------------------ yedek görsel
    static readonly (SKColor zemin, SKColor ana)[] Paletler =
    {
        (new SKColor(232, 245, 233), new SKColor(46, 125, 50)), (new SKColor(255, 243, 224), new SKColor(230, 81, 0)),
        (new SKColor(227, 242, 253), new SKColor(21, 101, 192)), (new SKColor(252, 228, 236), new SKColor(173, 20, 87)),
        (new SKColor(243, 229, 245), new SKColor(106, 27, 154)), (new SKColor(255, 248, 225), new SKColor(191, 144, 0)),
    };

    static SKTypeface Yazi(bool kalin)
    {
        foreach (var aile in new[] { "Arial", "Segoe UI", "DejaVu Sans", "Liberation Sans" })
        {
            var t = SKTypeface.FromFamilyName(aile, kalin ? SKFontStyle.Bold : SKFontStyle.Normal);
            if (t != null && t.FamilyName.Equals(aile, StringComparison.OrdinalIgnoreCase)) return t;
        }
        return SKTypeface.FromFamilyName(null, kalin ? SKFontStyle.Bold : SKFontStyle.Normal) ?? SKTypeface.Default;
    }

    /// <summary>Python textwrap.wrap(metin, genislik) (uzun kelimeler bölünür).</summary>
    public static List<string> Sar(string metin, int genislik)
    {
        var satirlar = new List<string>();
        var cur = "";
        foreach (var k0 in Py.SplitWs(metin))
        {
            var k = k0;
            while (k.Length > 0)
            {
                if (cur.Length == 0)
                {
                    if (k.Length <= genislik) { cur = k; k = ""; }
                    else { satirlar.Add(k.Substring(0, genislik)); k = k.Substring(genislik); }
                }
                else if (cur.Length + 1 + k.Length <= genislik) { cur += " " + k; k = ""; }
                else { satirlar.Add(cur); cur = ""; }
            }
        }
        if (cur.Length > 0) satirlar.Add(cur);
        return satirlar;
    }

    /// <summary>Resmi bulunamayan ürün için 600×600, adı yazılı sade kart.</summary>
    public static void YedekGorsel(string ad, string altBaslik, string hedef)
    {
        int toplam = 0;
        foreach (var r in (altBaslik ?? "").EnumerateRunes()) toplam += r.Value;
        var (zemin, ana) = Paletler[toplam % Paletler.Length];
        using var bmp = new SKBitmap(Boyut, Boyut);
        using (var c = new SKCanvas(bmp))
        {
            c.Clear(zemin);
            using var dolgu = new SKPaint { Color = SKColors.White, IsAntialias = true, Style = SKPaintStyle.Fill };
            using var cizgi = new SKPaint { Color = ana, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 8 };
            var kutu = new SKRect(34, 34, 566, 566);
            c.DrawRoundRect(kutu, 40, 40, dolgu);
            c.DrawRoundRect(kutu, 40, 40, cizgi);
            using var daire = new SKPaint { Color = ana, IsAntialias = true };
            c.DrawCircle(300, 170, 80, daire);

            var kalin = Yazi(true);
            var ince = Yazi(false);
            var harf = ad.Length == 0 ? "?" : ad.StartsWith("i", StringComparison.Ordinal) ? "İ" : Py.Upper(ad.Substring(0, char.IsSurrogate(ad[0]) && ad.Length > 1 ? 2 : 1));
            using (var f = new SKFont(kalin, 100))
            using (var p = new SKPaint { Color = SKColors.White, IsAntialias = true })
            {
                var w = f.MeasureText(harf);
                c.DrawText(harf, 300 - w / 2, 105 + f.Metrics.CapHeight + 8, f, p);
            }
            var satirlar = Sar(ad, 16).Take(3).ToList();
            if (satirlar.Count == 0) satirlar.Add(ad);
            int boyut = 64;
            SKFont yf = null;
            while (true)
            {
                yf?.Dispose();
                yf = new SKFont(kalin, boyut);
                if (boyut <= 26 || satirlar.Max(s => yf.MeasureText(s)) <= 480) break;
                boyut -= 4;
            }
            using (yf)
            using (var p = new SKPaint { Color = new SKColor(33, 33, 33), IsAntialias = true })
            {
                int y = 380 - satirlar.Count * (boyut + 12) / 2;
                foreach (var s in satirlar)
                {
                    var w = yf.MeasureText(s);
                    c.DrawText(s, 300 - w / 2, y + boyut * 0.8f, yf, p);
                    y += boyut + 12;
                }
            }
            using (var f2 = new SKFont(ince, 30))
            using (var p = new SKPaint { Color = ana, IsAntialias = true })
            {
                var w = f2.MeasureText(altBaslik ?? "");
                c.DrawText(altBaslik ?? "", 300 - w / 2, 505 + 26, f2, p);
            }
        }
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(hedef)));
        File.WriteAllBytes(hedef, Jpeg(bmp, 88));
    }

    // ------------------------------------------------------------------ broşürden kesme
    /// <summary>Broşürdeki ürün fotoğrafını (0-1000 normalize kutu [ymin,xmin,ymax,xmax]) keser.</summary>
    public static bool BrosurdenKes(string dosya, double[] kutu, string hedef)
    {
        if (dosya.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) return false;
        using var img = Coz(File.ReadAllBytes(dosya));
        if (img == null) return false;
        double W = img.Width, H = img.Height;
        double y0 = kutu[0], x0 = kutu[1], y1 = kutu[2], x1 = kutu[3];
        if (y0 > y1) (y0, y1) = (y1, y0);
        if (x0 > x1) (x0, x1) = (x1, x0);
        double px = (x1 - x0) * 0.03, py = (y1 - y0) * 0.03;
        double sol = Math.Max(0, (x0 - px) / 1000 * W), ust = Math.Max(0, (y0 - py) / 1000 * H);
        double sag = Math.Min(W, (x1 + px) / 1000 * W), alt = Math.Min(H, (y1 + py) / 1000 * H);
        if (sag - sol < 40 || alt - ust < 40) return false;
        var alan = new SKRectI((int)sol, (int)ust, (int)sag, (int)alt);
        using var parca = new SKBitmap(alan.Width, alan.Height);
        if (!img.ExtractSubset(parca, alan)) return false;
        using var kopya = parca.Copy();
        using var kare = Kare(kopya);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(hedef)));
        File.WriteAllBytes(hedef, Jpeg(kare, 90));
        return true;
    }

    /// <summary>Yapay zekâya gönderilecek dosya: resimler küçültülüp JPEG, PDF olduğu gibi.</summary>
    public static (string mime, string b64) DosyaHazirla(string yol, int maks = 1600)
    {
        if (yol.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            return ("application/pdf", Convert.ToBase64String(File.ReadAllBytes(yol)));
        using var img = Coz(File.ReadAllBytes(yol)) ?? throw new Exception("Resim açılamadı: " + Path.GetFileName(yol));
        double oran = Math.Min(1.0, Math.Min((double)maks / img.Width, (double)maks / img.Height));
        int w = Math.Max(1, (int)(img.Width * oran)), h = Math.Max(1, (int)(img.Height * oran));
        using var k = new SKBitmap(w, h);
        using (var c = new SKCanvas(k))
        {
            c.Clear(SKColors.White);
            Ciz(c, img, SKRect.Create(0, 0, w, h));
        }
        return ("image/jpeg", Convert.ToBase64String(Jpeg(k, 88)));
    }
}
