using System.Text.Json.Nodes;

namespace UrunMotor;

/// <summary>Arayüzden gelen komutlar.</summary>
public static class Komutlar
{
    // ------------------------------------------------------------------ JSON yardımcıları
    static string S(JsonObject a, string k, string v = "")
    {
        var n = a[k];
        if (n == null) return v;
        return n.GetValueKind() == System.Text.Json.JsonValueKind.String ? n.GetValue<string>() : n.ToJsonString();
    }
    static double D(JsonObject a, string k, double v)
    {
        var n = a[k];
        if (n == null) return v;
        if (n.GetValueKind() == System.Text.Json.JsonValueKind.Number) return n.GetValue<double>();
        if (n.GetValueKind() == System.Text.Json.JsonValueKind.String && double.TryParse(Py.Strip(n.GetValue<string>()).Replace(',', '.'),
                System.Globalization.NumberStyles.Float, Py.CI, out var d)) return d;
        if (n.GetValueKind() == System.Text.Json.JsonValueKind.String && n.GetValue<string>().Trim().Length > 0)
            throw new HataMesaji($"'{n.GetValue<string>()}' sayı değil (Fiyat/KDV sayı olmalı).");
        return v;
    }
    static int I(JsonObject a, string k, int v) => (int)Math.Round(D(a, k, v));
    static bool B(JsonObject a, string k, bool v = false)
    {
        var n = a[k];
        return n == null ? v : n.GetValueKind() switch
        {
            System.Text.Json.JsonValueKind.True => true,
            System.Text.Json.JsonValueKind.False => false,
            _ => v,
        };
    }
    static List<string> L(JsonObject a, string k) =>
        a[k] is JsonArray d ? d.Where(x => x != null).Select(x => x.GetValue<string>()).ToList() : new List<string>();

    static JsonNode Sayi(double? d) => d.HasValue ? JsonValue.Create(d.Value) : null;

    public static JsonArray KategoriJson(List<Kategori> k, string resimKok = null, Dictionary<string, string> gercek = null) =>
        new(k.Select(x =>
        {
            string klasor = null;
            if (resimKok != null || gercek != null)
            {
                if (gercek == null || !gercek.TryGetValue(x.Yol, out klasor)) klasor = Sql.KlasorYolu(resimKok ?? "", x.Yol);
            }
            return (JsonNode)new JsonObject
            {
                ["yol"] = x.Yol,
                ["urunler"] = new JsonArray(x.Urunler.Select(u => (JsonNode)new JsonObject
                {
                    ["ad"] = u.Ad, ["arama"] = u.Arama, ["fiyat"] = Sayi(u.Fiyat),
                    ["porsiyonlar"] = new JsonArray(u.Porsiyonlar.Select(p => (JsonNode)new JsonObject { ["ad"] = p.Ad, ["fiyat"] = Sayi(p.Fiyat) }).ToArray()),
                    ["resim"] = klasor != null ? Sql.ResimBul(klasor, gercek != null ? u.Arama : u.Ad) : null,
                }).ToArray()),
            };
        }).ToArray());

    static Yapay.Saglayici Saglayici(JsonObject a)
    {
        var s = a["saglayici"] as JsonObject ?? new JsonObject();
        return new Yapay.Saglayici(S(s, "ad", "gemini"), S(s, "geminiAnahtar"), S(s, "model"), S(s, "mistralAnahtar"));
    }

    static Dictionary<string, Arama.Kirpma> Kirpmalar(JsonObject a)
    {
        var d = new Dictionary<string, Arama.Kirpma>();
        if (a["kirpmalar"] is JsonObject k)
            foreach (var (ad, v) in k)
                if (v is JsonObject o && o["kutu"] is JsonArray kutu && kutu.Count == 4)
                    d[ad] = new Arama.Kirpma(S(o, "dosya"), kutu.Select(x => x.GetValue<double>()).ToArray());
        return d;
    }

    static JsonObject KirpmaJson(Dictionary<string, Arama.Kirpma> k)
    {
        var o = new JsonObject();
        foreach (var (ad, v) in k)
            o[ad] = new JsonObject { ["dosya"] = v.Dosya, ["kutu"] = new JsonArray(v.Kutu.Select(x => (JsonNode)JsonValue.Create(x)).ToArray()) };
        return o;
    }

    /// <summary>Başlıksız listeyi kategorilere ayırır: varsa yapay zekâ, yoksa yerleşik sınıflandırıcı.</summary>
    static async Task<(string metin, string yontem)> Siniflandir(string metin, Yapay.Saglayici s, Is c, bool yapayZeka)
    {
        if (!Metin.BasliksizMi(metin)) return (metin, "hazir");
        if (yapayZeka && (s.Ad == "ollama" || !string.IsNullOrWhiteSpace(s.GeminiAnahtar) || !string.IsNullOrWhiteSpace(s.MistralAnahtar)))
        {
            try
            {
                var yeni = await Yapay.ListeyiDuzenle(metin, s, c.Log, c.T);
                if (Py.SplitLines(yeni).Any(x => Py.Strip(x).StartsWith("#", StringComparison.Ordinal))) return (yeni, "yapay");
            }
            catch (OperationCanceledException) when (c.T.IsCancellationRequested) { throw; }
            catch (Exception e) { c.Log($"Yapay zekâ ile ayrılamadı ({(e.Message.Length > 150 ? e.Message[..150] : e.Message)})."); }
        }
        c.Log("Yerleşik sınıflandırıcı ile kategorilere ayrılıyor...");
        return (Metin.YerelSiniflandir(metin), "yerel");
    }

    static int Say(List<Kategori> k) => k.Sum(x => x.Urunler.Count);

    public static async Task<JsonNode> Yurut(string komut, JsonObject a, Is c)
    {
        switch (komut)
        {
            case "surum":
                return new JsonObject { ["surum"] = Program.Surum };

            case "ayristir":
            {
                var metin = S(a, "metin");
                var k = Metin.ListeyiAyristir(metin);
                return new JsonObject
                {
                    ["kategoriler"] = KategoriJson(k, a["resimKok"] != null ? S(a, "resimKok") : null),
                    ["odeme"] = new JsonArray(Metin.OdemeKodlariBul(metin).Select(x => (JsonNode)x).ToArray()),
                    ["basliksiz"] = Metin.BasliksizMi(metin), ["urunSayisi"] = Say(k),
                };
            }

            case "siniflandir":
            {
                var (metin, yontem) = await Siniflandir(S(a, "metin"), Saglayici(a), c, B(a, "yapayZeka", true));
                if (yontem != "hazir") c.Log("Kategoriler hazır (listeyi kutuda görebilirsin).\n");
                return new JsonObject { ["metin"] = metin, ["yontem"] = yontem };
            }

            case "menu_oku":
            {
                var dosyalar = Klasor.MenuDosyalari(L(a, "yollar"));
                if (dosyalar.Count == 0) throw new HataMesaji("Resim (jpg/png/webp) veya PDF bulunamadı.");
                var s = Saglayici(a);
                var liste = Py.Strip(S(a, "liste"));
                if (liste.Length > 0) c.Log("Yazılı liste broşürle karşılaştırılacak.");
                c.Log($"{dosyalar.Count} menü dosyası okunuyor ({s.Ad})...");
                string metin; Dictionary<string, Arama.Kirpma> kirp = new(); List<string> farklar = new(), eksik = new();
                if (s.Ad == "ollama")
                {
                    metin = await Yapay.MenuyuOku(dosyalar, s, c.Log, c.T);
                    if (liste.Length > 0) metin = liste + "\n\n" + metin;
                }
                else
                {
                    var b = await Yapay.BrosurOku(dosyalar, liste, s, c.Log, c.T);
                    (metin, kirp, farklar) = (b.Metin, b.Kirpmalar, b.Farklar);
                    if (liste.Length > 0) eksik = Metin.ListeEksikKontrol(liste, metin);
                }
                var sayi = Say(Metin.ListeyiAyristir(metin));
                c.Log($"Menü okundu: {sayi} ürün, {kirp.Count} ürünün fotoğrafı broşürden alınacak.");
                foreach (var f in farklar) c.Log("  • " + f);
                if (eksik.Count > 0) c.Log("  ! Listede olup sonuçta bulamadığım satırlar (kontrol et, gerekirse elle ekle): " + string.Join(", ", eksik));
                return new JsonObject
                {
                    ["metin"] = metin, ["kirpmalar"] = KirpmaJson(kirp),
                    ["farklar"] = new JsonArray(farklar.Select(x => (JsonNode)x).ToArray()),
                    ["eksik"] = new JsonArray(eksik.Select(x => (JsonNode)x).ToArray()),
                    ["dosyalar"] = new JsonArray(dosyalar.Select(x => (JsonNode)x).ToArray()),
                    ["urunSayisi"] = sayi,
                };
            }

            case "indir":
            {
                var metin = S(a, "metin");
                var (yeni, yontem) = await Siniflandir(metin, Saglayici(a), c, B(a, "yapayZeka", true));
                if (yontem != "hazir")
                {
                    c.Olay("metin", new JsonObject { ["metin"] = yeni });
                    c.Log("Kategoriler hazır (listeyi kutuda görebilirsin).\n");
                }
                var k = Metin.ListeyiAyristir(yeni);
                if (k.Count == 0) throw new HataMesaji("Önce menüyü okut veya ürün listesini yapıştır.");
                var cikti = S(a, "cikti");
                if (cikti.Length == 0) throw new HataMesaji("Resim ana klasörü seçilmedi.");
                int toplam = Say(k), sira = 0;
                var basarisiz = await Arama.HepsiniIndir(k, cikti, Math.Max(1, I(a, "adet", 1)), S(a, "ek"), c.Log,
                    (yol, u, durum, resim) =>
                    {
                        sira++;
                        c.Olay("urun", new JsonObject
                        {
                            ["yol"] = yol, ["ad"] = u.Ad, ["durum"] = durum.ToString().ToLowerInvariant(), ["resim"] = resim,
                            ["sira"] = sira, ["toplam"] = toplam,
                        });
                    },
                    B(a, "klasorArama"), B(a, "yenile"), Kirpmalar(a), c.T, Program.Surum);
                return new JsonObject
                {
                    ["metin"] = yeni, ["basarisiz"] = new JsonArray(basarisiz.Select(x => (JsonNode)x).ToArray()),
                    ["kategoriler"] = KategoriJson(k, cikti),
                };
            }

            case "resim_yenile":
            {
                // Galeriden "Farklı resim": daha önce kullanılanlar hariç yeni resim
                var cikti = S(a, "cikti"); var yol = S(a, "yol"); var ad = S(a, "ad");
                var klasor = Sql.KlasorYolu(cikti, yol);
                var kayit = Arama.KullanilanOku(cikti);
                var anahtar = $"{yol.Replace('/', Path.DirectorySeparatorChar)}/{ad}";
                if (!kayit.TryGetValue(anahtar, out var liste)) kayit[anahtar] = liste = new();
                var baglam = B(a, "klasorArama")
                    ? string.Join(" ", yol.Split('/').Where(p => Py.Lower(p) is not ("genel" or "sadece yemek" or "hazır"))) : "";
                var sorgu = string.Join(" ", new[] { baglam, Arama.AramaOlustur(ad, S(a, "arama", ad), yol), S(a, "ek") }.Where(x => x.Length > 0));
                var (durum, _) = await Arama.UrunIndir(ad, sorgu, klasor, 1, c.Log, liste, true, c.T);
                Arama.KullanilanYaz(cikti, kayit);
                return new JsonObject { ["durum"] = durum.ToString().ToLowerInvariant(), ["resim"] = Sql.ResimBul(klasor, ad) };
            }

            case "resim_ayarla":
            {
                // Galeriden "Dosya seç": kullanıcının seçtiği resim 600×600 yapılıp konur
                var klasor = Sql.KlasorYolu(S(a, "cikti"), S(a, "yol"));
                var ad = S(a, "ad");
                var hedef = Path.Combine(klasor, ad + ".jpg");
                foreach (var f in Arama.MevcutResimler(klasor, ad)) try { File.Delete(Path.Combine(klasor, f)); } catch { }
                if (!Resim.KareJpegKaydet(await File.ReadAllBytesAsync(S(a, "dosya"), c.T), hedef, kucukDeOlur: true))
                    throw new HataMesaji("Seçilen dosya resim olarak açılamadı.");
                c.Log($"  ✓ {ad} (elle seçilen resim konuldu)");
                return new JsonObject { ["resim"] = hedef };
            }

            case "resim_sil":
            {
                var klasor = Sql.KlasorYolu(S(a, "cikti"), S(a, "yol"));
                foreach (var f in Arama.MevcutResimler(klasor, S(a, "ad"))) File.Delete(Path.Combine(klasor, f));
                return new JsonObject { ["resim"] = null };
            }

            case "galeri":
            {
                var metin = S(a, "metin");
                if (Metin.BasliksizMi(metin)) metin = Metin.YerelSiniflandir(metin);
                return new JsonObject { ["kategoriler"] = KategoriJson(Metin.ListeyiAyristir(metin), S(a, "cikti")) };
            }

            case "klasor_tara":
            {
                var tr = Klasor.Tara(S(a, "yol"));
                return new JsonObject
                {
                    ["kategoriler"] = KategoriJson(tr.Kategoriler, null, tr.Gercek),
                    ["metin"] = Metin.KategorileriYaz(tr.Kategoriler), ["urunSayisi"] = Say(tr.Kategoriler),
                };
            }

            case "sql":
            case "pro":
            {
                List<Kategori> k; Dictionary<string, string> gercek = null; string resimKok;
                var metin = S(a, "metin");
                if (S(a, "klasor").Length > 0)
                {
                    c.Log($"Klasör taranıyor: {S(a, "klasor")}");
                    var tr = Klasor.Tara(S(a, "klasor"));
                    if (Say(tr.Kategoriler) == 0) throw new HataMesaji("Klasörde resim bulunamadı.");
                    c.Log($"{tr.Kategoriler.Count} klasör, {Say(tr.Kategoriler)} ürün resmi bulundu.");
                    (k, gercek, resimKok) = (tr.Kategoriler, tr.Gercek, S(a, "klasor"));
                }
                else
                {
                    if (Metin.BasliksizMi(metin)) metin = Metin.YerelSiniflandir(metin);
                    k = Metin.ListeyiAyristir(metin);
                    if (k.Count == 0) throw new HataMesaji("Ürün listesi boş.");
                    resimKok = S(a, "resimKok");
                }
                var odeme = Metin.OdemeKodlariBul(metin);
                double fiyat = D(a, "fiyat", 1), kdv = D(a, "kdv", 10);
                int seviye = I(a, "seviye", 1);
                c.Log($"Firma: {S(a, "firma", "Firmasiz")}");
                if (komut == "sql")
                {
                    var elle = new Dictionary<string, Dictionary<string, string>>();
                    var sabit = new Dictionary<string, Dictionary<string, string>>();
                    if (a["sutunlar"] is JsonObject su)
                        foreach (var (tablo, v) in su)
                        {
                            if (v is not JsonObject vo) continue;
                            if (tablo == "sabitler")
                            {
                                foreach (var (t2, v2) in vo)
                                    if (v2 is JsonObject o2) sabit[t2] = o2.ToDictionary(x => x.Key, x => x.Value?.ToString() ?? "NULL");
                            }
                            else elle[tablo] = vo.ToDictionary(x => x.Key, x => x.Value?.ToString());
                        }
                    var r = Sql.SqlOlustur(S(a, "dump"), k, resimKok, S(a, "hedef"), fiyat, kdv, seviye, elle, c.Log, gercek, odeme, sabit, c.T);
                    return new JsonObject
                    {
                        ["kategori"] = r.Kategori, ["urun"] = r.Urun, ["hedef"] = S(a, "hedef"),
                        ["eksik"] = new JsonArray(r.Eksik.Select(x => (JsonNode)x).ToArray()),
                    };
                }
                else
                {
                    var r = Pro.DbOlustur(S(a, "db"), k, resimKok, S(a, "hedef"), fiyat, kdv, seviye, c.Log, gercek, odeme,
                        S(a, "kullanici"), S(a, "opsiyonlar", Pro.OpsiyonVarsayilan), B(a, "icecekOpsiyon"), B(a, "demoTemizle", true), c.T);
                    return new JsonObject
                    {
                        ["kategori"] = r.Kategori, ["urun"] = r.Urun, ["hedef"] = r.DbYolu, ["resimKlasoru"] = r.ResimKlasoru,
                        ["eksik"] = new JsonArray(r.Eksik.Select(x => (JsonNode)x).ToArray()),
                    };
                }
            }

            case "pro_demo_temizle":
                return new JsonObject { ["yedek"] = Pro.DemoTemizleDosya(S(a, "db"), c.Log) };

            case "firma_adi":
                return new JsonObject
                {
                    ["ad"] = S(a, "pro").Length > 0 && File.Exists(S(a, "pro")) ? Pro.FirmaAdi(S(a, "pro"))
                        : S(a, "sql").Length > 0 && File.Exists(S(a, "sql")) ? Sql.FirmaAdiBul(S(a, "sql")) : "",
                };

            case "excel_oku":
            {
                var metin = Excel.Oku(S(a, "yol"));
                return new JsonObject { ["metin"] = metin, ["urunSayisi"] = Say(Metin.ListeyiAyristir(metin)) };
            }

            case "excel_yaz":
            {
                var metin = S(a, "metin");
                if (Metin.BasliksizMi(metin)) metin = Metin.YerelSiniflandir(metin);
                var k = Metin.ListeyiAyristir(metin);
                if (k.Count == 0) throw new HataMesaji("Ürün listesi boş.");
                var n = Excel.Yaz(k, S(a, "resimKok"), S(a, "hedef"), S(a, "firma"));
                c.Log($"Excel'e aktarıldı: {S(a, "hedef")}  ({n} ürün)");
                return new JsonObject { ["urun"] = n, ["hedef"] = S(a, "hedef") };
            }

            case "modeller":
                return new JsonArray((await Yapay.GeminiModelleri(S(a, "anahtar"), c.T)).Select(x => (JsonNode)x).ToArray());

            case "liste_yaz":
            {
                var k = Metin.ListeyiAyristir(S(a, "metin"));
                return new JsonObject { ["metin"] = Metin.KategorileriYaz(k) };
            }

            default:
                throw new HataMesaji($"Bilinmeyen komut: {komut}");
        }
    }
}
