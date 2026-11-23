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
    static async Task<string> MetinAl(string url, CancellationToken t, int sn = 20, string referer = null, bool json = false)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(t);
        cts.CancelAfter(TimeSpan.FromSeconds(sn));
        using var istek = new HttpRequestMessage(HttpMethod.Get, url);
        if (referer != null) istek.Headers.Referrer = new Uri(referer);
        if (json)
        {
            istek.Headers.Accept.ParseAdd("*/*");
            istek.Headers.TryAddWithoutValidation("Sec-Fetch-Dest", "empty");
            istek.Headers.TryAddWithoutValidation("Sec-Fetch-Mode", "cors");
            istek.Headers.TryAddWithoutValidation("Sec-Fetch-Site", "same-origin");
        }
        else istek.Headers.Accept.ParseAdd("text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
        using var c = await Http.SendAsync(istek, cts.Token);
        if (!c.IsSuccessStatusCode) throw new HttpRequestException($"HTTP {(int)c.StatusCode}");
        return await c.Content.ReadAsStringAsync(cts.Token);
    }

    static readonly Regex Vqd = new(@"vqd=[""']([^""']+)[""']|vqd=([^&""'\s]+)", RegexOptions.CultureInvariant);

    static async Task<List<Dictionary<string, string>>> DuckDuckGo(string sorgu, int adet, CancellationToken t)
    {
        var q = Uri.EscapeDataString(sorgu);
        var sayfa = await MetinAl($"https://duckduckgo.com/?q={q}", t);
        var m = Vqd.Match(sayfa);
        if (!m.Success) throw new HttpRequestException("anahtar (vqd) alınamadı");
        var vqd = Uri.EscapeDataString(m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value);
        var js = await MetinAl($"https://duckduckgo.com/i.js?o=json&q={q}&l=tr-tr&vqd={vqd}&p=1&ct=AT", t, 20, "https://duckduckgo.com/", json: true);
        using var d = JsonDocument.Parse(js);
        var sonuc = new List<Dictionary<string, string>>();
        if (d.RootElement.TryGetProperty("results", out var rs) && rs.ValueKind == JsonValueKind.Array)
            foreach (var r in rs.EnumerateArray())
            {
                var x = new Dictionary<string, string>();
                foreach (var k in new[] { "image", "title", "url", "source", "width", "height", "thumbnail" })
                    if (r.TryGetProperty(k, out var v)) x[k] = v.ValueKind == JsonValueKind.String ? v.GetString() : v.ToString();
                if (!string.IsNullOrEmpty(x.GetValueOrDefault("image"))) sonuc.Add(x);
            }
        return sonuc.Take(Math.Max(20, adet * 8)).ToList();
    }

    // Bing'in sonuç kutuları: <a class="iusc" ... m="{...murl...}"> (öznitelik sırası değişebilir)
    static readonly Regex BingEtiket = new(@"<a\s[^>]*\bclass=""iusc""[^>]*>", RegexOptions.CultureInvariant);
    static readonly Regex BingM = new(@"\sm=""([^""]+)""", RegexOptions.CultureInvariant);

    static List<Dictionary<string, string>> BingCoz(string html, int adet)
    {
        var sonuc = new List<Dictionary<string, string>>();
        foreach (Match e in BingEtiket.Matches(html))
        {
            var m = BingM.Match(e.Value);
            if (!m.Success) continue;
            try
            {
                using var d = JsonDocument.Parse(WebUtility.HtmlDecode(m.Groups[1].Value));
                var r = d.RootElement;
                var murl = r.TryGetProperty("murl", out var mu) ? mu.GetString() : null;
                if (string.IsNullOrEmpty(murl)) continue;
                sonuc.Add(new()
                {
                    ["image"] = murl, ["title"] = r.TryGetProperty("t", out var tt) ? tt.GetString() ?? "" : "",
                    ["url"] = r.TryGetProperty("purl", out var pu) ? pu.GetString() ?? "" : "", ["width"] = "", ["height"] = "",
                });
            }
            catch (JsonException) { }
            if (sonuc.Count >= Math.Max(20, adet * 8)) break;
        }
        return sonuc;
    }

    static async Task<List<Dictionary<string, string>>> Bing(string sorgu, int adet, CancellationToken t)
    {
        var q = Uri.EscapeDataString(sorgu);
        var sonuc = BingCoz(await MetinAl($"https://www.bing.com/images/async?q={q}&async=1&first=1&count=35&mmasync=1&setlang=tr&cc=tr", t), adet);
        if (sonuc.Count == 0)
            sonuc = BingCoz(await MetinAl($"https://www.bing.com/images/search?q={q}&form=HDRSC2&first=1&setlang=tr&cc=tr", t), adet);
        return sonuc;
    }

    // DuckDuckGo arka arkaya cevap vermezse bir süre doğrudan Bing kullanılır (her üründe beklememek için)
    static int _ddgHata;
    static DateTime _ddgAtla = DateTime.MinValue;
    static readonly object AramaKilit = new();

    public static async Task<List<Dictionary<string, string>>> ResimAra(string sorgu, int adet, Action<string> log, string ad, CancellationToken t)
    {
        List<Dictionary<string, string>> sonuc = new();
        var hatalar = new List<string>();
        for (int tur = 0; tur < 2 && sonuc.Count == 0; tur++)
        {
            if (tur > 0)
            {
                log($"   Arama sonuç vermedi ({string.Join(", ", hatalar)}), 4 sn sonra bir kez daha deneniyor...");
                await Task.Delay(4000, t);
                hatalar.Clear();
            }
            bool ddgKullan;
            lock (AramaKilit) ddgKullan = DateTime.UtcNow >= _ddgAtla;
            if (ddgKullan)
            {
                try
                {
                    sonuc = await DuckDuckGo(sorgu, adet, t);
                    if (sonuc.Count == 0) hatalar.Add("DuckDuckGo boş");
                    lock (AramaKilit) _ddgHata = sonuc.Count > 0 ? 0 : _ddgHata + 1;
                }
                catch (Exception e) when (e is not OperationCanceledException || !t.IsCancellationRequested)
                {
                    hatalar.Add("DuckDuckGo " + (e is TaskCanceledException ? "zaman aşımı" : e.Message));
                    lock (AramaKilit) _ddgHata++;
                }
                lock (AramaKilit)
                    if (_ddgHata >= 2)
                    {
                        _ddgHata = 0;
                        _ddgAtla = DateTime.UtcNow.AddMinutes(10);
                        log("   (DuckDuckGo şu an cevap vermiyor; 10 dakika Bing ile aranacak)");
                    }
            }
            if (sonuc.Count == 0)
            {
                try
                {
                    sonuc = await Bing(sorgu, adet, t);
                    if (sonuc.Count == 0) hatalar.Add("Bing boş");
                }
                catch (Exception e) when (e is not OperationCanceledException || !t.IsCancellationRequested)
                {
                    hatalar.Add("Bing " + (e is TaskCanceledException ? "zaman aşımı" : e.Message));
                }
            }
        }
        if (sonuc.Count == 0)
        {
            log($"   Arama sonuç vermedi ({string.Join(", ", hatalar)}).");
            return sonuc;
        }
        if (ad != null)
            sonuc = sonuc.Select((r, i) => (p: Puan(r, ad) + YemekPuani(r), i, r)).Where(x => x.p >= 0)
                .OrderByDescending(x => x.p).ThenBy(x => x.i).Select(x => x.r).ToList();
        return sonuc;
    }

    // Arama sonucunun yemekle ilgili olup olmadığı (başlık + sayfa adresi). Futbolcu, manzara, alet gibi
    // alakasız sonuçlar elenir; yemek kelimesi geçenler öne alınır.
    static readonly string[] YemekKelime =
    {
        "yemek", "food", "tarif", "recipe", "menu", "restoran", "restaurant", "burger", "tavuk", "chicken", "patates", "fries",
        "sos", "sauce", "mayonez", "mayo", "kebap", "kebab", "doner", "pizza", "salata", "salad", "tatli", "dessert", "icecek",
        "drink", "lezzet", "delicious", "sandvic", "sandwich", "wrap", "nugget", "cips", "chips", "kizartma", "fried", "porsiyon",
        "tabak", "plate", "cafe", "kafe", "mutfak", "kitchen", "snack", "atistirmalik", "corba", "soup", "pide", "lahmacun",
        "durum", "tost", "kofte", "et ", "steak", "ayran", "kola", "cola", "soda", "gazoz", "meze", "pilav", "makarna", "pasta",
    };
    static readonly string[] AlakasizKelime =
    {
        "futbol", "football", "soccer", "oyuncu", "player", "transfer", "galatasaray", "fenerbahce", "besiktas", "trabzonspor",
        "manzara", "landscape", "beach", "plaj", "sahil", "sunset", "gun batimi", "selale", "waterfall", "wallpaper", "duvar kagidi",
        "laptop", "notebook", "bilgisayar", "matkap", "makine", "machine", "tools", "hirdavat", "instagram", "tiktok", "youtube",
        "film", "dizi", "movie", "series", "haber", "news", "siyaset", "model", "manken", "bikini", "mayo modelleri", "swimwear",
        "ilac", "araba", "otomobil", "telefon", "phone", "kitap", "book", "oyun", "game", "anime", "meme", "caps",
    };

    public static int YemekPuani(IReadOnlyDictionary<string, string> r)
    {
        var metin = " " + Metin.Norm(string.Join(" ", new[] { "title", "url", "source" }.Select(k => r.TryGetValue(k, out var v) ? v ?? "" : ""))) + " ";
        metin = Regex.Replace(metin, "[^a-z0-9]+", " ");
        int yemek = YemekKelime.Count(k => metin.Contains(k.Trim().Length < 4 ? " " + k.Trim() + " " : k));
        bool alakasiz = AlakasizKelime.Any(k => metin.Contains(" " + k));
        if (alakasiz && yemek == 0) return -100;
        return Math.Min(yemek, 2) * 2 - (alakasiz ? 3 : 0);
    }

    /// <summary>Arama cümlesi: ürüne özel arama terimi yoksa üst kategori adı eklenir ("Hot Chick" → "Hot Chick Burger").</summary>
    public static string Sorgu(string ad, string arama, string yol, string ek, bool klasorArama)
    {
        var temel = AramaOlustur(ad, arama, yol);
        if (Metin.Norm(temel) == Metin.Norm(ad))
            // Türkçede "mayo" mayo (giysi) demek; İngilizce menü adlarını yemeğe çevir
            temel = string.Join(" ", Py.SplitWs(temel).Select(w => Metin.Norm(w) switch
            {
                "mayo" => "mayonez", "chick" => "chicken", "fries" => "patates kızartması", "patty" => "burger köftesi", _ => w,
            }));
        var parcalar = (yol ?? "").Split('/').Where(p => Py.Lower(p) is not ("genel" or "sadece yemek" or "hazır" or "diğer" or "menü")).ToList();
        string baglam = "";
        if (klasorArama) baglam = string.Join(" ", parcalar);
        else if (Metin.Norm(temel) == Metin.Norm(ad) && parcalar.Count > 0)
        {
            var ust = parcalar[0];
            var adN = Metin.Norm(ad);
            if (!Py.SplitWs(Metin.Norm(ust)).All(w => adN.Contains(w))) baglam = ust;
        }
        return string.Join(" ", new[] { temel, baglam, ek }.Where(x => !string.IsNullOrWhiteSpace(x)));
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

    public enum Durum { Var, Indi, Yedek, Yok, Brosur, Yerel }
    public const string YedekIsareti = "#yedek";

    /// <summary>Bir ürünün resmini indirir. kullanilan: daha önce indirilmiş adresler (tekrar gelmesin).</summary>
    public static async Task<(Durum durum, int sayi)> UrunIndir(string ad, string sorgu, string klasor, int adet, Action<string> log,
        List<string> kullanilan, bool yenile, CancellationToken t, string yerelArsiv = null, string kategoriYolu = null)
    {
        kullanilan ??= new();
        Directory.CreateDirectory(klasor);
        var mevcut = MevcutResimler(klasor, ad);
        // Önceki çalıştırmada adı yazılı yedek kart konduysa yeniden aranır
        bool yedekVardi = kullanilan.Remove(YedekIsareti);
        if (mevcut.Count >= adet && !yenile && !yedekVardi)
        {
            log($"  ✓ {ad} (zaten var, atlandı)");
            return (Durum.Var, mevcut.Count);
        }
        bool YereldenKoy(Yerel.Eslesme e)
        {
            try
            {
                if (!Resim.KareJpegKaydet(File.ReadAllBytes(e.Yol), Path.Combine(klasor, $"{ad}.jpg"), kucukDeOlur: true)) return false;
                kullanilan.Add("yerel:" + e.Yol);
                log($"  ✓ {ad} (kendi arşivinden: {Path.GetFileName(e.Yol)})");
                return true;
            }
            catch (Exception) { return false; }
        }
        Yerel.Eslesme yerel = null;
        if (!string.IsNullOrWhiteSpace(yerelArsiv))
        {
            try { yerel = Yerel.Bul(yerelArsiv, ad, kategoriYolu, log); } catch (Exception e) { log($"   Arşiv okunamadı: {e.Message}"); }
            if (yerel != null && kullanilan.Contains("yerel:" + yerel.Yol) && yenile) yerel = null;   // "Farklı resim": aynısını verme
            // Adı birebir tutan kendi resmin internetten önce gelir
            if (yerel != null && yerel.Birebir && !yenile && YereldenKoy(yerel)) return (Durum.Yerel, 1);
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
                    cts.CancelAfter(TimeSpan.FromSeconds(12));
                    using var istek = new HttpRequestMessage(HttpMethod.Get, url);
                    istek.Headers.Accept.ParseAdd("image/avif,image/webp,image/apng,image/*,*/*;q=0.8");
                    if (Uri.TryCreate(r.GetValueOrDefault("url"), UriKind.Absolute, out var sayfa)) istek.Headers.Referrer = sayfa;
                    using var c = await Http.SendAsync(istek, cts.Token);
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
        if (kaydedilen == 0 && yerel != null && YereldenKoy(yerel)) return (Durum.Yerel, 1);
        if (kaydedilen == 0)
        {
            if (yenile && mevcut.Count > 0 && !yedekVardi)
            {
                log($"  ✗ {ad}: farklı resim bulunamadı, eskisi duruyor");
                return (Durum.Yok, 0);
            }
            try
            {
                Resim.YedekGorsel(ad, Path.GetFileName(klasor.TrimEnd(Path.DirectorySeparatorChar)), Path.Combine(klasor, $"{ad}.jpg"));
                kullanilan.Add(YedekIsareti);
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
        Dictionary<string, Kirpma> kirpmalar, CancellationToken t, string surum, string yerelArsiv = null)
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
                var sorgu = Sorgu(u.Ad, u.Arama, k.Yol, ek, klasorArama);
                var anahtar = $"{k.Yol.Replace('/', Path.DirectorySeparatorChar)}/{u.Ad}";
                if (!kayit.TryGetValue(anahtar, out var liste)) kayit[anahtar] = liste = new();
                var (durum, sayi) = await UrunIndir(u.Ad, sorgu, klasor, adet, log, liste, yenile, t, yerelArsiv, k.Yol);
                if (sayi <= 0) basarisiz.Add($"{k.Yol.Replace("/", " / ")} / {u.Ad}");
                KullanilanYaz(cikti, kayit);
                olay(k.Yol, u, durum, Sql.ResimBul(klasor, u.Ad));
                if (durum is not (Durum.Var or Durum.Yerel)) await Task.Delay(1500, t);
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
