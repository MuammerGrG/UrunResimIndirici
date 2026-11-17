using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace UrunMotor;

/// <summary>İnternette ürün fotoğrafı arama (DuckDuckGo, yedek Bing) ve indirme.</summary>
public static class Arama
{
    public const string Ua = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0 Safari/537.36";

    public static readonly HttpClient Http = YeniIstemci();

    static HttpClient YeniIstemci()
    {
        var h = new HttpClient(new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            ConnectTimeout = TimeSpan.FromSeconds(15),
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        }) { Timeout = Timeout.InfiniteTimeSpan };
        h.DefaultRequestHeaders.UserAgent.ParseAdd(Ua);
        h.DefaultRequestHeaders.AcceptLanguage.ParseAdd("tr-TR,tr;q=0.9,en;q=0.8");
        return h;
    }

    static readonly (string, string)[] KategoriIpucu =
    {
        ("yesillik", "yeşillik demet"), ("sebze", "sebze"), ("meyve", "meyve"), ("kuruyemis", "kuruyemiş"),
        ("baharat", "baharat"), ("et urun", "kasap"), ("sarkuteri", "şarküteri"), ("icecek", "içecek"), ("tatli", "tatlı"),
    };

    static readonly Dictionary<string, string> AramaDuzelt = new()
    {
        ["turp"] = "kırmızı turp sebze", ["roka"] = "roka yeşillik demet", ["tere"] = "tere otu demet",
        ["nane"] = "taze nane demet", ["dereotu"] = "dereotu demet", ["maydanoz"] = "maydanoz demet",
        ["kabak"] = "sakız kabak sebze", ["pancar"] = "kırmızı pancar sebze", ["kereviz"] = "kereviz kökü sebze",
        ["sogan"] = "kuru soğan sebze", ["taze sogan"] = "taze soğan demet", ["arpacik sogan"] = "arpacık soğan",
        ["patates"] = "patates sebze", ["ispanak"] = "ıspanak sebze", ["domates"] = "domates sebze",
        ["limon"] = "limon meyve", ["limon lamas yatak"] = "lamas limonu meyve", ["lamas limon"] = "lamas limonu",
        ["nar"] = "nar meyvesi", ["ayva"] = "ayva meyvesi", ["kivi"] = "kivi meyvesi",
        ["muz ithal"] = "muz meyvesi salkım", ["yerli muz"] = "yerli anamur muzu",
        ["elma arjantin"] = "arjantin elması", ["elma amasya"] = "amasya elması",
        ["elma starking"] = "starking elma", ["elma golden"] = "golden elma",
        ["armut deveci"] = "deveci armut", ["armut santamaria"] = "santa maria armut",
        ["topan patlican"] = "topan patlıcan yuvarlak", ["altin cilek"] = "altın çilek physalis meyve",
        ["uzum kavacik"] = "kavacık üzümü", ["uzum sultaniye"] = "sultaniye üzüm",
        ["cennet hurmasi"] = "cennet hurması trabzon hurması", ["sarimsak kastamonu"] = "kastamonu taşköprü sarımsak",
        ["mor lahana"] = "mor lahana sebze", ["duz marul"] = "göbek marul sebze",
        ["kivircik marul"] = "kıvırcık marul sebze", ["portakal"] = "portakal meyve",
        ["mandalina"] = "mandalina meyve", ["greyfurt"] = "greyfurt meyve",
    };

    static readonly string[] YasakKelimeler =
    {
        "yatak", "ortu", "nevresim", "carsaf", "yorgan", "yastig", "yastik", "gozlu", "sunglass",
        "eyewear", "ameliyat", "prostat", "surgery", "resection", "hastal", "tedavi", "urolog",
        "kilif", "telefon", "ayakkabi", "elbise", "tisort", "t-shirt", "canta", "kitap", "oyuncak",
        "kolye", "parfum", "sabun", "kumas", "perde", "mobilya", "otomobil", "logo",
        "oyuncu", "clipart", "cizim", "drawing", "cartoon", "karikatur", "emoji", "vector",
        "memorial", "acibadem", "medicalpark", "medical", "medikal", "hastane", "hospital",
        "klinik", "clinic", "doktor", "doctor", "opdr", "op-dr", "uroloji", "endoskop",
        "saglik", "health", "lybrate", "anatomy", "anatomi", "mesane", "bladder",
    };

    static readonly Dictionary<string, string[]> AramaHaric = new()
    {
        ["turp"] = new[] { "ameliyat", "prostat", "hastane", "urology" },
        ["roka"] = new[] { "gözlük", "güneş" },
        ["limon lamas yatak"] = new[] { "yatak", "örtü", "nevresim" },
        ["kabak"] = new[] { "tadı" },
    };

    /// <summary>Ürün adı + kategori ipucu ile doğru resmi bulduracak arama cümlesi.</summary>
    public static string AramaOlustur(string ad, string arama, string kategoriYolu)
    {
        var n = Metin.Norm(ad);
        if (!string.IsNullOrEmpty(arama) && Metin.Norm(arama) != n) return arama;
        if (AramaDuzelt.TryGetValue(n, out var duz))
        {
            var haric = string.Join(" ", (AramaHaric.GetValueOrDefault(n) ?? Array.Empty<string>()).Select(k => "-" + k));
            return Py.Strip($"{duz} {haric}");
        }
        var temiz = string.Join(" ", Py.SplitWs(ad).Where(k => Metin.Norm(k) != "yatak"));
        var ust = string.IsNullOrEmpty(kategoriYolu) ? "" : Metin.Norm(kategoriYolu.Split('/')[0]);
        var ipucu = KategoriIpucu.FirstOrDefault(x => ust.Contains(x.Item1)).Item2 ?? "";
        if (ipucu.Length > 0 && !Metin.Norm(temiz).Contains(Metin.Norm(Py.SplitWs(ipucu)[0]))) return $"{temiz} {ipucu}";
        return temiz;
    }

    static int SayiAl(string v)
    {
        var s = Regex.Replace(v ?? "", "[^0-9]", "");
        return s.Length == 0 ? 0 : int.TryParse(s, out var n) ? n : 0;
    }

    /// <summary>Arama sonucu puanı: yasak kelime → -1; ad kelimeleri +2; kareye yakın büyük resim +1.</summary>
    public static int SonucPuani(IReadOnlyDictionary<string, string> r, string ad)
    {
        var metin = Metin.Norm(string.Join(" ", new[] { "title", "url", "image", "source" }.Select(k => r.TryGetValue(k, out var v) ? v ?? "None" : "")));
        if (YasakKelimeler.Any(y => metin.Contains(Metin.Norm(y)))) return -1;
        var anahtarlar = Py.SplitWs(Metin.Norm(ad)).Where(k => k.Length >= 3);
        int puan = anahtarlar.Sum(k => metin.Contains(k) ? 2 : 0);
        int w = SayiAl(r.GetValueOrDefault("width")), h = SayiAl(r.GetValueOrDefault("height"));
        if (w > 0 && h > 0 && (double)w / h >= 0.6 && (double)w / h <= 1.7 && Math.Min(w, h) >= 400) puan++;
        return puan;
    }

    // ------------------------------------------------------------------ arama motorları
    static async Task<string> MetinAl(string url, CancellationToken t, int sn = 20, string referer = null)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(t);
        cts.CancelAfter(TimeSpan.FromSeconds(sn));
        using var istek = new HttpRequestMessage(HttpMethod.Get, url);
        if (referer != null) istek.Headers.Referrer = new Uri(referer);
        using var c = await Http.SendAsync(istek, cts.Token);
        if (!c.IsSuccessStatusCode) throw new HttpRequestException($"HTTP {(int)c.StatusCode}");
        return await c.Content.ReadAsStringAsync(cts.Token);
    }

    static readonly Regex Vqd = new(@"vqd=[""']?([\d-]+)[""']?", RegexOptions.CultureInvariant);

    static async Task<List<Dictionary<string, string>>> DuckDuckGo(string sorgu, int adet, CancellationToken t)
    {
        var q = Uri.EscapeDataString(sorgu);
        var sayfa = await MetinAl($"https://duckduckgo.com/?q={q}&iax=images&ia=images", t);
        var m = Vqd.Match(sayfa);
        if (!m.Success) throw new HttpRequestException("DuckDuckGo anahtarı (vqd) alınamadı");
        var sonuc = new List<Dictionary<string, string>>();
        var url = $"https://duckduckgo.com/i.js?l=tr-tr&o=json&q={q}&vqd={m.Groups[1].Value}&f=,,,,,&p=1";
        for (int s = 0; s < 3 && sonuc.Count < Math.Max(15, adet * 8); s++)
        {
            var js = await MetinAl(url, t, 20, "https://duckduckgo.com/");
            using var d = JsonDocument.Parse(js);
            if (!d.RootElement.TryGetProperty("results", out var rs) || rs.ValueKind != JsonValueKind.Array) break;
            foreach (var r in rs.EnumerateArray())
            {
                var x = new Dictionary<string, string>();
                foreach (var k in new[] { "image", "title", "url", "source", "width", "height", "thumbnail" })
                    if (r.TryGetProperty(k, out var v)) x[k] = v.ValueKind == JsonValueKind.String ? v.GetString() : v.ToString();
                if (!string.IsNullOrEmpty(x.GetValueOrDefault("image"))) sonuc.Add(x);
            }
            if (!d.RootElement.TryGetProperty("next", out var n) || n.ValueKind != JsonValueKind.String) break;
            url = "https://duckduckgo.com/" + n.GetString() + "&vqd=" + m.Groups[1].Value;
        }
        return sonuc.Take(Math.Max(15, adet * 8)).ToList();
    }

    static readonly Regex BingBlok = new(@"class=""iusc""[^>]*\sm=""([^""]+)""", RegexOptions.CultureInvariant);

    static async Task<List<Dictionary<string, string>>> Bing(string sorgu, int adet, CancellationToken t)
    {
        var html = await MetinAl("https://www.bing.com/images/search?q=" + Uri.EscapeDataString(sorgu) +
                                 "&form=HDRSC2&first=1&cc=tr&setlang=tr", t);
        var sonuc = new List<Dictionary<string, string>>();
        foreach (Match m in BingBlok.Matches(html))
        {
            try
            {
                using var d = JsonDocument.Parse(WebUtility.HtmlDecode(m.Groups[1].Value));
                var r = d.RootElement;
                var murl = r.TryGetProperty("murl", out var mu) ? mu.GetString() : null;
                if (!string.IsNullOrEmpty(murl))
                    sonuc.Add(new()
                    {
                        ["image"] = murl, ["title"] = r.TryGetProperty("t", out var tt) ? tt.GetString() ?? "" : "",
                        ["url"] = r.TryGetProperty("purl", out var pu) ? pu.GetString() ?? "" : "", ["width"] = "", ["height"] = "",
                    });
            }
            catch (JsonException) { }
            if (sonuc.Count >= Math.Max(15, adet * 8)) break;
        }
        return sonuc;
    }

    public static async Task<List<Dictionary<string, string>>> ResimAra(string sorgu, int adet, Action<string> log, string ad, CancellationToken t)
    {
        List<Dictionary<string, string>> sonuc = null;
        for (int deneme = 0; deneme < 3; deneme++)
        {
            try
            {
                sonuc = await DuckDuckGo(sorgu, adet, t);
                if (sonuc.Count > 0) break;
                throw new HttpRequestException("sonuç yok");
            }
            catch (Exception e) when (e is not OperationCanceledException || !t.IsCancellationRequested)
            {
                int bekle = 5 * (deneme + 1);
                var mesaj = e.Message.Length > 80 ? e.Message[..80] : e.Message;
                log($"   Arama hatası ({e.GetType().Name}: {mesaj}), {bekle} sn bekleniyor...");
                if (deneme == 0)
                {
                    try
                    {
                        sonuc = await Bing(sorgu, adet, t);
                        if (sonuc.Count > 0) { log("   Yedek arama (Bing) kullanıldı."); break; }
                    }
                    catch (Exception e2) when (e2 is not OperationCanceledException || !t.IsCancellationRequested)
                    {
                        log($"   Yedek arama da olmadı ({e2.GetType().Name}).");
                    }
                }
                await Task.Delay(bekle * 1000, t);
            }
        }
        if (sonuc == null || sonuc.Count == 0)
        {
            try { sonuc = await Bing(sorgu, adet, t); } catch when (!t.IsCancellationRequested) { sonuc = new(); }
        }
        if (sonuc.Count == 0) return sonuc;
        if (ad != null)
            sonuc = sonuc.Select((r, i) => (p: Puan(r, ad), i, r)).Where(x => x.p >= 0)
                .OrderByDescending(x => x.p).ThenBy(x => x.i).Select(x => x.r).ToList();
        return sonuc;
    }

    static int Puan(Dictionary<string, string> r, string ad) { try { return SonucPuani(r, ad); } catch { return 0; } }

    // ------------------------------------------------------------------ kullanılan resimler kaydı
    public static Dictionary<string, List<string>> KullanilanOku(string kok)
    {
        try
        {
            var js = File.ReadAllText(Path.Combine(kok, ".kullanilan_resimler.json"));
            return JsonSerializer.Deserialize<Dictionary<string, List<string>>>(js) ?? new();
        }
        catch { return new(); }
    }

    static readonly object KayitKilit = new();
    public static void KullanilanYaz(string kok, Dictionary<string, List<string>> veri)
    {
        try
        {
            lock (KayitKilit)
                File.WriteAllText(Path.Combine(kok, ".kullanilan_resimler.json"),
                    JsonSerializer.Serialize(veri, new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
        }
        catch { }
    }

    public static List<string> MevcutResimler(string klasor, string ad)
    {
        if (!Directory.Exists(klasor)) return new();
        var adlar = new HashSet<string> { Py.Lower(ad) };
        for (int i = 1; i <= 10; i++) adlar.Add(Py.Lower($"{ad}_{i}"));
        return Directory.GetFiles(klasor).Select(Path.GetFileName)
            .Where(f => f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) && adlar.Contains(Py.Lower(f[..^4])))
            .OrderBy(x => x, StringComparer.Ordinal).ToList();
    }

    public enum Durum { Var, Indi, Yedek, Yok, Brosur }

    /// <summary>Bir ürünün resmini indirir. kullanilan: daha önce indirilmiş adresler (tekrar gelmesin).</summary>
    public static async Task<(Durum durum, int sayi)> UrunIndir(string ad, string sorgu, string klasor, int adet, Action<string> log,
        List<string> kullanilan, bool yenile, CancellationToken t)
    {
        kullanilan ??= new();
        Directory.CreateDirectory(klasor);
        var mevcut = MevcutResimler(klasor, ad);
        if (mevcut.Count >= adet && !yenile)
        {
            log($"  ✓ {ad} (zaten var, atlandı)");
            return (Durum.Var, mevcut.Count);
        }
        // Yenilemede eski resim yeni gelene kadar korunur; bulunamazsa yerinde kalır.
        int kaydedilen = 0;
        log($"  … {ad}  [arama: {sorgu}]");
        var gecici = Path.Combine(klasor, $".yeni_{Guid.NewGuid():N}");
        try
        {
            foreach (var r in await ResimAra(sorgu, adet, log, ad, t))
            {
                if (kaydedilen >= adet) break;
                var url = r.GetValueOrDefault("image");
                if (string.IsNullOrEmpty(url) || kullanilan.Contains(url)) continue;
                byte[] veri;
                try
                {
                    using var cts = CancellationTokenSource.CreateLinkedTokenSource(t);
                    cts.CancelAfter(TimeSpan.FromSeconds(15));
                    using var c = await Http.GetAsync(url, cts.Token);
                    if (c.StatusCode != HttpStatusCode.OK) continue;
                    veri = await c.Content.ReadAsByteArrayAsync(cts.Token);
                }
                catch when (!t.IsCancellationRequested) { continue; }
                var dosya = adet == 1 ? $"{ad}.jpg" : $"{ad}_{kaydedilen + 1}.jpg";
                if (Resim.KareJpegKaydet(veri, gecici))
                {
                    if (kaydedilen == 0 && yenile)
                        foreach (var f in mevcut) try { File.Delete(Path.Combine(klasor, f)); } catch { }
                    File.Move(gecici, Path.Combine(klasor, dosya), true);
                    kaydedilen++;
                    kullanilan.Add(url);
                }
            }
        }
        finally { try { if (File.Exists(gecici)) File.Delete(gecici); } catch { } }
        if (kaydedilen == 0)
        {
            if (yenile && mevcut.Count > 0)
            {
                log($"  ✗ {ad}: farklı resim bulunamadı, eskisi duruyor");
                return (Durum.Yok, 0);
            }
            try
            {
                Resim.YedekGorsel(ad, Path.GetFileName(klasor.TrimEnd(Path.DirectorySeparatorChar)), Path.Combine(klasor, $"{ad}.jpg"));
                log($"  ~ {ad} (bulunamadı, adı yazılı yedek görsel konuldu)");
                return (Durum.Yedek, -1);
            }
            catch { }
        }
        log($"  {(kaydedilen > 0 ? "✓" : "✗")} {ad} ({kaydedilen}/{adet})");
        return (kaydedilen > 0 ? Durum.Indi : Durum.Yok, kaydedilen);
    }

    public sealed record Kirpma(string Dosya, double[] Kutu);

    /// <summary>Tüm ürünlerin resimlerini indirir; her ürün için olay(kategori, ürün, durum, yol) çağrılır.</summary>
    public static async Task<List<string>> HepsiniIndir(List<Kategori> kategoriler, string cikti, int adet, string ek,
        Action<string> log, Action<string, Urun, Durum, string> olay, bool klasorArama, bool yenile,
        Dictionary<string, Kirpma> kirpmalar, CancellationToken t, string surum)
    {
        int toplam = kategoriler.Sum(k => k.Urunler.Count);
        Directory.CreateDirectory(cikti);
        var kayit = KullanilanOku(cikti);
        log($"[v{surum}] {kategoriler.Count} klasör, {toplam} ürün. Çıktı: {Resim.Boyut}x{Resim.Boyut} JPEG\n");
        var basarisiz = new List<string>();
        foreach (var k in kategoriler)
        {
            var klasor = Sql.KlasorYolu(cikti, k.Yol);
            Directory.CreateDirectory(klasor);
            log($"[{k.Yol.Replace("/", " / ")}]");
            var baglam = klasorArama
                ? string.Join(" ", k.Yol.Split('/').Where(p => Py.Lower(p) is not ("genel" or "sadece yemek" or "hazır")))
                : "";
            foreach (var u in k.Urunler)
            {
                t.ThrowIfCancellationRequested();
                var hedef = Path.Combine(klasor, $"{u.Ad}.jpg");
                if (kirpmalar != null && kirpmalar.TryGetValue(Metin.Norm(u.Ad), out var kp))
                {
                    try
                    {
                        if (Resim.BrosurdenKes(kp.Dosya, kp.Kutu, hedef))
                        {
                            log($"  ✓ {u.Ad} (broşürden alındı)");
                            olay(k.Yol, u, Durum.Brosur, hedef);
                            continue;
                        }
                    }
                    catch (Exception e) { log($"  {u.Ad}: broşürden kesilemedi ({e.GetType().Name}), internette aranıyor"); }
                }
                var sorgu = string.Join(" ", new[] { baglam, AramaOlustur(u.Ad, u.Arama, k.Yol), ek }.Where(x => !string.IsNullOrEmpty(x)));
                var anahtar = $"{k.Yol.Replace('/', Path.DirectorySeparatorChar)}/{u.Ad}";
                if (!kayit.TryGetValue(anahtar, out var liste)) kayit[anahtar] = liste = new();
                var (durum, sayi) = await UrunIndir(u.Ad, sorgu, klasor, adet, log, liste, yenile, t);
                if (sayi <= 0) basarisiz.Add($"{k.Yol.Replace("/", " / ")} / {u.Ad}");
                KullanilanYaz(cikti, kayit);
                olay(k.Yol, u, durum, Sql.ResimBul(klasor, u.Ad));
                if (durum != Durum.Var) await Task.Delay(1500, t);
            }
            log("");
        }
        log("Bitti. Resimler: " + Path.GetFullPath(cikti));
        if (basarisiz.Count > 0)
        {
            log("\nFotoğrafı bulunamayanlar (yedek görsel kondu; galeriden 'Farklı resim' ya da 'Dosya seç' ile değiştir):");
            foreach (var b in basarisiz) log("  - " + b);
        }
        return basarisiz;
    }
}
