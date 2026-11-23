using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace UrunMotor;

/// <summary>Menü / broşür okuma: Gemini (ücretsiz AI Studio anahtarı), yedek Mistral, yerel Ollama.</summary>
public static class Yapay
{
    public const string MenuPrompt = """
Bu bir restoran menüsü. Menüdeki TÜM satılan ürünleri çıkar ve SADECE aşağıdaki formatta düz metin yaz, başka hiçbir açıklama yazma:

# Kategori
## Pişme Şekli
### Menü
Ürün Adı | arama terimi | fiyat | porsiyonlar
### Sadece Yemek
Ürün Adı | arama terimi | fiyat | porsiyonlar

Kurallar:
- Kategori: menüdeki ana grup (Döner, Burger, Pizza, Ramen, Tatlılar, İçecekler, Alkollü İçecekler vb.). Menüdeki başlıkları kullan.
- Pişme Şekli: Izgara, Kızartma, Fırın, Haşlama, Tava, Ocakbaşı, Soğuk gibi. Pişme şekli olmayan ürünlerde (içecek, hazır tatlı vb.) "Hazır" yaz.
- Ürün yanında patates, içecek vb. ile birlikte set/menü olarak satılıyorsa "Menü", tek başına ise "Sadece Yemek" altına koy.
- Ürün Adı: menüde yazdığı gibi (fiyat, gramaj açıklaması ve emoji olmadan). Aynı ürünü tekrar yazma.
- arama terimi: o ürünün internette doğru fotoğrafını bulduracak 2-6 kelimelik Türkçe arama (ör. "tavuk döner dürüm", "efes pilsen 50 cl şişe", "cheeseburger menü patates kola").
- fiyat: menüde yazan fiyat, sadece sayı (ör. 250 veya 87.50; TL/₺ yazma). Fiyat yoksa "| fiyat" kısmını hiç yazma.
- PORSİYONLAR: Bir ürünün Az / Yarım / Tam / 1,5 / Duble / Küçük / Orta / Büyük gibi porsiyon seçenekleri varsa
  bunları AYRI ÜRÜN YAZMA. Ürünü bir kez yaz; "fiyat" kısmına normal porsiyonun (Tam / Normal / 1 Porsiyon,
  yoksa en ucuz) fiyatını, "porsiyonlar" kısmına diğerlerini "ad=fiyat" şeklinde noktalı virgülle yaz.
  Örnek: "Et Döner Porsiyon | et döner porsiyon | 250 | Az=180; 1,5=350; Duble=450"
  Porsiyonu olmayan üründe "| porsiyonlar" kısmını hiç yazma. Ondalıklı porsiyonları virgülle yaz (1,5).
- Okunamayan veya emin olmadığın ürünleri atlama, en iyi tahminini yaz.
- ÖDEME: Menüde kabul edilen ödeme yöntemleri / yemek kartları (yazı ya da logo: Sodexo, Pluxee, Multinet,
  Ticket, Edenred, Setcard, Metropol, Paye, Tokenflex, Kredi Kartı, Nakit vb.) görünüyorsa EN SONA tek satır yaz:
  ÖDEME: Sodexo, Multinet, Ticket
  Sadece "yemek kartı geçerlidir" gibi genel bir ifade varsa:  ÖDEME: Yemek Kartı
  Ödeme bilgisi yoksa bu satırı hiç yazma.

""";

    public const string ListePrompt = """
Aşağıda bir işletmenin düz ürün listesi var. Bu ürünleri gruplayıp SADECE aşağıdaki formatta düz metin yaz, başka açıklama yazma:

Restoran/kafe ürünleri (yemek, içecek, tatlı) ise:
# Kategori
## Pişme Şekli
### Menü   (veya ### Sadece Yemek)
Ürün Adı | arama terimi | fiyat | porsiyonlar

Manav, market, kasap gibi pişmemiş/ham ürünler ise sadece kategori kullan:
# Kategori   (ör. Sebzeler, Meyveler, Yeşillikler, Kuruyemiş, Et Ürünleri)
Ürün Adı | arama terimi | fiyat

Listede ürünün yanında fiyat varsa sadece sayı olarak "| fiyat" kısmına yaz; yoksa o kısmı yazma.
Aynı ürünün Az / Tam / 1,5 / Duble / Küçük / Büyük gibi porsiyonları ayrı satırlarda yazılmışsa onları
TEK ürün yap: "fiyat" = normal (Tam/Normal) porsiyonun fiyatı, "porsiyonlar" = diğerleri "ad=fiyat; ad=fiyat".
Örnek: "Tavuk Döner | tavuk döner porsiyon | 200 | Az=150; 1,5=280"

Kurallar:
- Pişme Şekli: Izgara, Kızartma, Fırın, Haşlama, Tava, Ocakbaşı, Soğuk; pişme şekli olmayan restoran ürünlerinde "Hazır".
- Menü: patates/içecek ile set olarak satılan ürünler; diğerleri "Sadece Yemek".
- Ürün Adı: listede yazdığı gibi, sadece yazım hatalarını düzelt (Havuc→Havuç, Mandalin→Mandalina) ve sondaki noktayı sil. Tekrarlananları bir kez yaz. Hiçbir ürünü atlama.
- arama terimi: ürünün internette doğru fotoğrafını bulduracak 2-6 kelimelik Türkçe arama. Yerel/yöresel çeşit isimlerini anlaşılır yaz (ör. "Topan patlıcan" → "topan yuvarlak patlıcan", "Elma starking" → "starking kırmızı elma").
- ÖDEME: Listede kabul edilen ödeme yöntemleri / yemek kartları (yazı ya da logo: Sodexo, Pluxee, Multinet,
  Ticket, Edenred, Setcard, Metropol, Paye, Tokenflex, Kredi Kartı, Nakit vb.) görünüyorsa EN SONA tek satır yaz:
  ÖDEME: Sodexo, Multinet, Ticket
  Sadece "yemek kartı geçerlidir" gibi genel bir ifade varsa:  ÖDEME: Yemek Kartı
  Ödeme bilgisi yoksa bu satırı hiç yazma.

Liste:

""";

    const string BirlesikPrompt = """
Sana bir işletmenin menüsüyle ilgili {n} resim veriyorum (sırayla 1, 2, ...).
Resimlerin bazıları ürün fotoğraflı tasarımlı broşür/menü, bazıları düz yazı liste (ör. WhatsApp mesajı
ekran görüntüsü) olabilir.{liste_notu}

GÖREV: Tüm kaynakları KARŞILAŞTIR ve satılan TÜM ürünleri tek bir listede birleştir.
- Bir kaynakta olup diğerinde olmayan ürünleri de ekle; HİÇBİR ürünü atlama.
- Aynı ürün farklı adla geçiyorsa (ör. "Ciğerli Pilav" / "Nohutlu Ciğerli Pilav") tek ürün yap.
- Fiyatlar farklıysa YAZILI LİSTEDEKİ fiyatı kullan (daha güncel); listede yoksa broşürdeki.
- kategori: menüdeki başlık (ör. "Izgara & Et Ürünleri", "Pilav Çeşitleri", "İçecekler", "Menüler"), kısa ve düzgün.
- pisme: Izgara, Kızartma, Fırın, Haşlama, Tava, Ocakbaşı, Soğuk; pişme şekli yoksa "Hazır".
- tip: patates/içecekle birlikte set olarak satılanlar "Menü", diğerleri "Sadece Yemek".
- PORSİYON: aynı ürünün boy/miktar seçenekleri (Yarım Ekmek / 3 Parça, Tek / Buçuk / Duble, Az / Tam / 1,5,
  Küçük / Büyük) AYRI ÜRÜN DEĞİL: ürünü bir kez yaz, "fiyat" = ilk/normal seçeneğin fiyatı, diğerleri
  "porsiyonlar". Servis şekli tamamen farklıysa (ekmek arası vs. 200 gr porsiyon tabak) ayrı ürün yap.
  Tat/çeşit seçenekleri (Fuse Tea Mango / Şeftali, Acılı / Acısız Şalgam) ayrı ürün.
- ad: düzgün Türkçe ürün adı, fiyat ve emoji olmadan.
- arama: ürünün internette doğru fotoğrafını bulduracak 2-6 kelimelik Türkçe arama.
- resim: Ürünün broşürde KENDİNE AİT ayrı bir fotoğrafı varsa {"sayfa": resim no, "kutu": [ymin, xmin, ymax, xmax]}
  (0-1000 arası normalize koordinat). Kutu sadece yemek fotoğrafını kapsasın: yazı, fiyat etiketi, logo hariç.
  Fotoğraf birden fazla farklı ürünü birlikte gösteriyorsa (ör. içecek şişeleri grubu) ya da emin değilsen null.
- odeme: menüde/listede geçen ödeme yöntemleri ve yemek kartları (logo ya da yazı: Ticket, Setcard, Metropol,
  Sodexo, Multinet, Tokenflex, Visa/Mastercard → "Kredi Kartı"...). Sadece "tüm yemek kartları geçerli" gibi
  genel bir ifade varsa "Yemek Kartı" da ekle.
- farklar: kısa Türkçe notlar — sadece listede olanlar, sadece broşürde olanlar, fiyatı farklı olanlar,
  ekstralar (ör. "Patates Kızartması sadece listede", "cheddar/ranch sos +10 TL").

SADECE şu JSON'u döndür:
{"urunler":[{"kategori":"","pisme":"","tip":"Sadece Yemek","ad":"","arama":"","fiyat":0,
"porsiyonlar":[{"ad":"","fiyat":0}],"resim":null}],"odeme":[],"farklar":[]}

""";

    public static readonly string[] GeminiYedek = { "gemini-flash-latest", "gemini-flash-lite-latest" };
    /// <summary>Google'ın açık modeli: aynı anahtarla, Flash'tan ayrı kotayla çalışır, düşünme yapmaz.</summary>
    public static readonly string[] GemmaYedek = { "gemma-4-31b-it", "gemma-4-26b-a4b-it" };
    public const string GroqModel = "qwen/qwen3.8-27b";
    public const int GroqResimSiniri = 3;
    public const string OllamaModel = "qwen2.5vl:7b";
    public const string OllamaUrl = "http://localhost:11434";
    static readonly HashSet<string> GeminiKapali = new();
    static readonly Dictionary<string, List<string>> ModelOnbellek = new();
    static readonly string[] MistralModeller = { "mistral-small-latest", "mistral-medium-latest" };

    static HttpClient Http => Arama.Http;

    // ------------------------------------------------------------------ nabız (uzun beklemelerde log)
    sealed class Nabiz : IDisposable
    {
        readonly CancellationTokenSource _c = new();
        public int Gelen, Dusunce;
        public Nabiz(Action<string> log, Func<Nabiz, int, string> mesaj)
        {
            var sw = Stopwatch.StartNew();
            _ = Task.Run(async () =>
            {
                try
                {
                    while (true)
                    {
                        await Task.Delay(20000, _c.Token);
                        log(mesaj(this, (int)sw.Elapsed.TotalSeconds));
                    }
                }
                catch (OperationCanceledException) { }
            });
        }
        public void Dispose() { _c.Cancel(); _c.Dispose(); }
    }

    sealed class ZamanAsimi : Exception { public ZamanAsimi(string m) : base(m) { } }

    /// <summary>Satır satır okur; okumaSn boyunca HİÇ veri gelmezse ZamanAsimi.</summary>
    static async IAsyncEnumerable<string> Satirlar(Stream s, int okumaSn, int toplamSn, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken t)
    {
        using var r = new StreamReader(s, Encoding.UTF8);
        var sw = Stopwatch.StartNew();
        while (true)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(t);
            cts.CancelAfter(TimeSpan.FromSeconds(okumaSn));
            string satir;
            try { satir = await r.ReadLineAsync(cts.Token); }
            catch (OperationCanceledException) when (!t.IsCancellationRequested) { throw new ZamanAsimi("veri gelmedi"); }
            if (satir == null) yield break;
            if (sw.Elapsed.TotalSeconds > toplamSn) throw new ZamanAsimi("toplam süre aşıldı");
            yield return satir;
        }
    }

    // ------------------------------------------------------------------ Gemini
    static async Task<(int kod, string metin, string ham, string neden)> GeminiAkis(string model, string anahtar, JsonObject govde,
        Action<string> log, CancellationToken t, int okumaSn = 120, int toplamSn = 600)
    {
        using var nabiz = new Nabiz(log, (n, sn) => n.Gelen > 0 ? $"   … {model} yazıyor ({sn} sn, {n.Gelen} karakter geldi)"
            : n.Dusunce > 0 ? $"   … {model} düşünüyor ({sn} sn)" : $"   … {model} bekleniyor ({sn} sn)");
        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:streamGenerateContent?alt=sse&key={Uri.EscapeDataString(anahtar)}";
        using var istek = new HttpRequestMessage(HttpMethod.Post, url) { Content = new StringContent(govde.ToJsonString(), Encoding.UTF8, "application/json") };
        using var baglanti = CancellationTokenSource.CreateLinkedTokenSource(t);
        baglanti.CancelAfter(TimeSpan.FromSeconds(okumaSn));
        HttpResponseMessage c;
        try { c = await Http.SendAsync(istek, HttpCompletionOption.ResponseHeadersRead, baglanti.Token); }
        catch (OperationCanceledException) when (!t.IsCancellationRequested) { throw new ZamanAsimi("bağlantı"); }
        using (c)
        {
            if (c.StatusCode != HttpStatusCode.OK) return ((int)c.StatusCode, "", await c.Content.ReadAsStringAsync(t), "");
            var parcalar = new StringBuilder();
            string neden = "";
            await foreach (var satir in Satirlar(await c.Content.ReadAsStreamAsync(t), okumaSn, toplamSn, t))
            {
                if (!satir.StartsWith("data:", StringComparison.Ordinal)) continue;
                JsonNode veri;
                try { veri = JsonNode.Parse(satir[5..].Trim()); } catch (JsonException) { continue; }
                if (veri?["candidates"] is JsonArray adaylar)
                    foreach (var aday in adaylar)
                    {
                        if (aday?["content"]?["parts"] is JsonArray ps)
                            foreach (var p in ps)
                            {
                                var metin = p?["text"]?.GetValue<string>() ?? "";
                                if (p?["thought"]?.GetValueKind() == JsonValueKind.True) nabiz.Dusunce += Math.Max(1, metin.Length);
                                else if (metin.Length > 0) { parcalar.Append(metin); nabiz.Gelen += metin.Length; }
                            }
                        var fr = aday?["finishReason"]?.GetValue<string>();
                        if (!string.IsNullOrEmpty(fr)) neden = fr;
                    }
                var blok = veri?["promptFeedback"]?["blockReason"]?.GetValue<string>();
                if (!string.IsNullOrEmpty(blok)) neden = blok;
            }
            return (200, parcalar.ToString(), "", neden);
        }
    }

    /// <summary>Anahtarın kullanabildiği güncel 'flash' modelleri (en yeni numaralı önce).</summary>
    public static async Task<List<string>> GeminiModelleri(string anahtar, CancellationToken t)
    {
        lock (ModelOnbellek) if (ModelOnbellek.TryGetValue(anahtar, out var o)) return o;
        var adaylar = new HashSet<string>();
        var gemmalar = new HashSet<string>();
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(t);
            cts.CancelAfter(TimeSpan.FromSeconds(20));
            var js = await Http.GetStringAsync($"https://generativelanguage.googleapis.com/v1beta/models?pageSize=200&key={Uri.EscapeDataString(anahtar)}", cts.Token);
            if (JsonNode.Parse(js)?["models"] is JsonArray ms)
                foreach (var m in ms)
                {
                    var ad = (m?["name"]?.GetValue<string>() ?? "").Split('/').Last();
                    var yontem = m?["supportedGenerationMethods"] as JsonArray;
                    if (yontem != null && yontem.Any(x => x?.GetValue<string>() == "generateContent") && ad.Contains("flash") &&
                        !new[] { "image", "tts", "audio", "live", "embedding", "thinking", "exp", "preview", "8b" }.Any(ad.Contains))
                        adaylar.Add(ad);
                    else if (yontem != null && yontem.Any(x => x?.GetValue<string>() == "generateContent") &&
                             Regex.IsMatch(ad, @"^gemma-([4-9]|\d\d)") && ad.EndsWith("-it", StringComparison.Ordinal) && !Regex.IsMatch(ad, @"-e\d+b"))
                        gemmalar.Add(ad);
                }
        }
        catch (Exception) when (!t.IsCancellationRequested) { }
        var sirali = adaylar.OrderBy(x => x, StringComparer.Ordinal).OrderByDescending(x => x, new ModelSirasi()).ToList();
        // En yeni Flash'lar yoğunlukta takılabiliyor: en iyi 2 Flash + 1 Lite, sonra Gemma (ayrı kota).
        var flash = sirali.Where(m => !m.Contains("lite")).Take(2).Concat(sirali.Where(m => m.Contains("lite")).Take(1)).ToList();
        if (flash.Count == 0) flash.AddRange(GeminiYedek);
        else if (!flash.Contains("gemini-flash-latest") && !adaylar.Any(m => !m.Contains("lite") && m.EndsWith("-latest", StringComparison.Ordinal)))
            flash.Add("gemini-flash-latest");
        var gemma = gemmalar.OrderByDescending(m => Regex.Match(m, @"(\d+)b").Success ? int.Parse(Regex.Match(m, @"(\d+)b").Groups[1].Value) : 0)
            .ThenBy(m => m, StringComparer.Ordinal).Take(2).ToList();
        if (gemma.Count == 0) gemma.AddRange(GemmaYedek);
        var liste = flash.Concat(gemma).Distinct().ToList();
        lock (ModelOnbellek) ModelOnbellek[anahtar] = liste;
        return liste;
    }

    sealed class ModelSirasi : IComparer<string>
    {
        static (bool, bool, List<int>) Anahtar(string ad)
        {
            var s = Regex.Matches(ad, @"\d+").Take(2).Select(m => int.TryParse(m.Value, out var n) ? n : 0).ToList();
            if (s.Count == 0) s.Add(0);
            return (!ad.Contains("lite"), !ad.EndsWith("-latest", StringComparison.Ordinal), s);
        }
        public int Compare(string a, string b)
        {
            var (a1, a2, a3) = Anahtar(a); var (b1, b2, b3) = Anahtar(b);
            int c = a1.CompareTo(b1); if (c != 0) return c;
            c = a2.CompareTo(b2); if (c != 0) return c;
            for (int i = 0; i < Math.Min(a3.Count, b3.Count); i++) { c = a3[i].CompareTo(b3[i]); if (c != 0) return c; }
            return a3.Count.CompareTo(b3.Count);
        }
    }

    public sealed class AnahtarHatasi : Exception { public AnahtarHatasi(string m) : base(m) { } }

    public static async Task<string> Gemini(IList<string> grup, string anahtar, string modelTercih, string istem, Action<string> log,
        CancellationToken t, bool jsonMod = false, int resimBoyu = 1600)
    {
        var parcalar = new JsonArray();
        if (grup.Count > 0) log($"   {grup.Count} resim hazırlanıyor...");
        foreach (var g in grup)
        {
            var (mime, veri) = Resim.DosyaHazirla(g, resimBoyu);
            parcalar.Add(new JsonObject { ["inline_data"] = new JsonObject { ["mime_type"] = mime, ["data"] = veri } });
        }
        parcalar.Add(new JsonObject { ["text"] = istem });
        log("   Uygun Gemini modelleri soruluyor...");
        var modeller = new List<string>(await GeminiModelleri(anahtar, t));
        if (!string.IsNullOrWhiteSpace(modelTercih)) { modeller.Remove(modelTercih.Trim()); modeller.Insert(0, modelTercih.Trim()); }
        List<string> acik;
        lock (GeminiKapali) acik = modeller.Where(m => !GeminiKapali.Contains(m)).ToList();
        if (acik.Count > 0) modeller = acik;
        var hatalar = new List<string>();
        for (int i = 0; i < modeller.Count; i++)
        {
            var model = modeller[i];
            bool gemmaMi = model.StartsWith("gemma", StringComparison.Ordinal);
            var config = new JsonObject { ["temperature"] = 0.1, ["maxOutputTokens"] = gemmaMi ? 8192 : 32768 };
            if (jsonMod && !gemmaMi) config["responseMimeType"] = "application/json";
            if (gemmaMi) log($"   (Gemini Flash olmadı; Google'ın Gemma modeli deneniyor, kotası ayrı)");
            var dusunme = gemmaMi ? new List<JsonObject>() : model.Contains("2.5")
                ? new List<JsonObject> { new() { ["thinkingBudget"] = 0 } }
                : new List<JsonObject>
                {
                    new() { ["thinkingLevel"] = "minimal", ["includeThoughts"] = true },
                    new() { ["thinkingLevel"] = "low", ["includeThoughts"] = true },
                    new() { ["thinkingLevel"] = "low" },
                };
            dusunme.Add(null);
            int mesgul = 0;
            while (true)
            {
                t.ThrowIfCancellationRequested();
                config.Remove("thinkingConfig");
                if (dusunme[0] != null) config["thinkingConfig"] = dusunme[0].DeepClone();
                var govde = new JsonObject
                {
                    ["contents"] = new JsonArray(new JsonObject { ["parts"] = parcalar.DeepClone() }),
                    ["generationConfig"] = config.DeepClone(),
                };
                log($"   → {model} deneniyor" + (mesgul > 0 ? $" (tekrar {mesgul})" : "") + "...");
                var sw = Stopwatch.StartNew();
                (int kod, string metin, string ham, string neden) c;
                try { c = await GeminiAkis(model, anahtar, govde, log, t); }
                catch (ZamanAsimi)
                {
                    log($"   {model}: 2 dakika boyunca hiç veri gelmedi, sonraki modele geçiliyor.");
                    hatalar.Add($"{model}: zaman aşımı");
                    break;
                }
                catch (HttpRequestException e)
                {
                    log($"   {model}: bağlantı hatası ({e.GetType().Name}), sonraki modele geçiliyor.");
                    hatalar.Add($"{model}: bağlantı");
                    break;
                }
                int sure = (int)sw.Elapsed.TotalSeconds;
                if (c.kod == 200)
                {
                    if (c.metin.Trim().Length > 0) { log($"   ✓ {model} cevap verdi ({sure} sn)."); return c.metin; }
                    log($"   {model}: boş cevap ({(c.neden.Length > 0 ? c.neden : "bilinmiyor")}), sonraki modele geçiliyor.");
                    hatalar.Add($"{model}: {(c.neden.Length > 0 ? c.neden : "boş")}");
                    break;
                }
                var hamK = c.ham.ToLowerInvariant();
                if (c.kod == 400 && config.ContainsKey("responseMimeType") && (hamK.Contains("mime") || hamK.Contains("json mode")))
                {
                    config.Remove("responseMimeType");
                    continue;
                }
                if (c.kod == 400 && (hamK.Contains("thinking") || hamK.Contains("thought")) && dusunme.Count > 1)
                {
                    dusunme.RemoveAt(0);
                    log($"   ({model} bu düşünme ayarını desteklemiyor, bir sonrakiyle deneniyor)");
                    continue;
                }
                if (c.kod is 400 or 401 or 403 && c.ham.Contains("API key"))
                    throw new AnahtarHatasi("Gemini anahtarı geçersiz — Ayarlar'dan yeniden gir.");
                if (c.kod == 404)
                {
                    lock (GeminiKapali) GeminiKapali.Add(model);
                    var m = Regex.Match(c.ham, @"use (?:models/)?([\w.\-]+)");
                    var oneri = m.Success ? m.Groups[1].Value.TrimEnd('.') : "";
                    log($"   {model}: Google bu modeli kapatmış" + (oneri.Length > 0 ? $", önerdiği {oneri} deneniyor." : "."));
                    hatalar.Add($"{model}: kapalı");
                    if (oneri.Length > 0 && !modeller.Skip(i + 1).Contains(oneri)) modeller.Insert(i + 1, oneri);
                    break;
                }
                if (c.kod == 429)
                {
                    bool gun = hamK.Contains("per day") || c.ham.Contains("PerDay");
                    var m = Regex.Match(c.ham, @"""retryDelay"":\s*""(\d+)");
                    int bekle = Math.Min(m.Success ? int.Parse(m.Groups[1].Value) + 2 : 30, 60);
                    if (gun || mesgul >= 2)
                    {
                        log($"   {model}: {(gun ? "günlük" : "dakikalık")} ücretsiz kota doldu, sonraki modele geçiliyor.");
                        hatalar.Add($"{model}: kota");
                        break;
                    }
                    mesgul++;
                    log($"   {model}: dakikalık kota doldu, {bekle} sn bekleniyor...");
                    await Task.Delay(bekle * 1000, t);
                    continue;
                }
                if (c.kod is 500 or 502 or 503 or 504)
                {
                    if (mesgul < 2)
                    {
                        mesgul++;
                        int bekle = 6 * mesgul;
                        log($"   {model}: Google sunucusu yoğun (HTTP {c.kod}), {bekle} sn sonra tekrar...");
                        await Task.Delay(bekle * 1000, t);
                        continue;
                    }
                    log($"   {model}: sunucu hâlâ yoğun, sonraki modele geçiliyor.");
                    hatalar.Add($"{model}: yoğun");
                    break;
                }
                var msg = Regex.Replace(c.ham.Length > 200 ? c.ham[..200] : c.ham, @"\s+", " ");
                log($"   {model}: HTTP {c.kod} — {msg}");
                hatalar.Add($"{model}: HTTP {c.kod}");
                break;
            }
        }
        throw new HataMesaji("Gemini menüyü okuyamadı → " + string.Join(" | ", hatalar.Count > 0 ? hatalar : new List<string> { "model yok" }));
    }

    // ------------------------------------------------------------------ Mistral (yedek)
    public static async Task<string> Mistral(IList<string> grup, string anahtar, string istem, Action<string> log, CancellationToken t,
        bool jsonMod = false, int resimBoyu = 1600)
    {
        var icerik = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = istem });
        foreach (var g in grup)
        {
            if (g.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                throw new HataMesaji("Yedek model (Mistral) PDF okuyamaz; menüyü resim olarak ver.");
            icerik.Add(new JsonObject { ["type"] = "image_url", ["image_url"] = "data:image/jpeg;base64," + Resim.DosyaHazirla(g, resimBoyu).b64 });
        }
        var hatalar = new List<string>();
        foreach (var model in MistralModeller)
        {
            for (int deneme = 0; deneme < 3; deneme++)
            {
                var govde = new JsonObject
                {
                    ["model"] = model, ["temperature"] = 0.1, ["max_tokens"] = 16000, ["stream"] = true,
                    ["messages"] = new JsonArray(new JsonObject { ["role"] = "user", ["content"] = icerik.DeepClone() }),
                };
                if (jsonMod) govde["response_format"] = new JsonObject { ["type"] = "json_object" };
                log($"   → Mistral {model} deneniyor" + (deneme > 0 ? $" (tekrar {deneme})" : "") + "...");
                var sw = Stopwatch.StartNew();
                int kod; string ham = "", metin = "";
                using (var nabiz = new Nabiz(log, (n, sn) => $"   … Mistral {model} çalışıyor ({sn} sn{(n.Gelen > 0 ? $", {n.Gelen} karakter geldi" : "")})"))
                {
                    try
                    {
                        using var istek = new HttpRequestMessage(HttpMethod.Post, "https://api.mistral.ai/v1/chat/completions")
                        { Content = new StringContent(govde.ToJsonString(), Encoding.UTF8, "application/json") };
                        istek.Headers.Authorization = new("Bearer", anahtar);
                        istek.Headers.Accept.ParseAdd("text/event-stream");
                        using var cts = CancellationTokenSource.CreateLinkedTokenSource(t);
                        cts.CancelAfter(TimeSpan.FromSeconds(120));
                        using var c = await Http.SendAsync(istek, HttpCompletionOption.ResponseHeadersRead, cts.Token);
                        kod = (int)c.StatusCode;
                        if (kod != 200) ham = await c.Content.ReadAsStringAsync(t);
                        else
                        {
                            var sb = new StringBuilder();
                            await foreach (var satir in Satirlar(await c.Content.ReadAsStreamAsync(t), 120, 900, t))
                            {
                                if (!satir.StartsWith("data:", StringComparison.Ordinal)) continue;
                                var v = satir[5..].Trim();
                                if (v == "[DONE]") break;
                                JsonNode d;
                                try { d = JsonNode.Parse(v); } catch (JsonException) { continue; }
                                if (d?["choices"] is JsonArray ch)
                                    foreach (var x in ch)
                                    {
                                        var ct = x?["delta"]?["content"];
                                        string s = ct switch
                                        {
                                            JsonValue jv when jv.GetValueKind() == JsonValueKind.String => jv.GetValue<string>(),
                                            JsonArray ja => string.Concat(ja.Select(y => y?["text"]?.GetValue<string>() ?? "")),
                                            _ => "",
                                        };
                                        if (s.Length > 0) { sb.Append(s); nabiz.Gelen += s.Length; }
                                    }
                            }
                            metin = sb.ToString();
                        }
                    }
                    catch (Exception e) when (e is HttpRequestException or ZamanAsimi || (e is OperationCanceledException && !t.IsCancellationRequested))
                    {
                        log($"   Mistral {model}: bağlantı/zaman aşımı ({e.GetType().Name}).");
                        hatalar.Add($"{model}: bağlantı");
                        break;
                    }
                }
                if (kod == 200)
                {
                    if (metin.Trim().Length > 0) { log($"   ✓ Mistral {model} cevap verdi ({(int)sw.Elapsed.TotalSeconds} sn)."); return metin; }
                    hatalar.Add($"{model}: boş");
                    break;
                }
                if (kod is 401 or 403) throw new AnahtarHatasi("Mistral anahtarı geçersiz — Ayarlar'dan yeniden gir.");
                if (kod == 429 || kod >= 500)
                {
                    int bekle = 8 * (deneme + 1);
                    log($"   Mistral {model}: {(kod == 429 ? "kota" : "sunucu yoğun")} (HTTP {kod}), {bekle} sn sonra tekrar...");
                    await Task.Delay(bekle * 1000, t);
                    continue;
                }
                log($"   Mistral {model}: HTTP {kod} — {Regex.Replace(ham.Length > 200 ? ham[..200] : ham, @"\s+", " ")}");
                hatalar.Add($"{model}: HTTP {kod}");
                break;
            }
        }
        throw new HataMesaji("Mistral de okuyamadı → " + string.Join(" | ", hatalar.Count > 0 ? hatalar : new List<string> { "?" }));
    }

    // ------------------------------------------------------------------ Groq (ücretsiz, hızlı yedek)
    static readonly Regex Dusunce = new(@"<think>.*?</think>", RegexOptions.Singleline | RegexOptions.CultureInvariant);

    public static async Task<string> Groq(IList<string> grup, string anahtar, string istem, Action<string> log, CancellationToken t,
        bool jsonMod = false)
    {
        if (grup.Any(g => g.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)))
            throw new HataMesaji("Groq PDF okuyamaz; menüyü resim (jpg/png) olarak ver.");
        int boy = 1536;
        bool dusunmeAyari = true, jsonAyari = jsonMod;
        for (int deneme = 0; deneme < 5; deneme++)
        {
            var icerik = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = istem });
            foreach (var g in grup)
                icerik.Add(new JsonObject
                {
                    ["type"] = "image_url",
                    ["image_url"] = new JsonObject { ["url"] = "data:image/jpeg;base64," + Resim.DosyaHazirla(g, boy).b64 },
                });
            var govde = new JsonObject
            {
                ["model"] = GroqModel, ["temperature"] = 0.1, ["max_completion_tokens"] = 16384,
                ["messages"] = new JsonArray(new JsonObject { ["role"] = "user", ["content"] = icerik }),
            };
            if (dusunmeAyari) govde["reasoning_effort"] = "none";
            if (jsonAyari) govde["response_format"] = new JsonObject { ["type"] = "json_object" };
            log($"   → Groq {GroqModel} deneniyor" + (deneme > 0 ? $" (tekrar {deneme})" : "") + "...");
            var sw = Stopwatch.StartNew();
            int kod; string ham; HttpResponseMessage c;
            using (var nabiz = new Nabiz(log, (n, sn) => $"   … Groq çalışıyor ({sn} sn)"))
            {
                try
                {
                    using var istek = new HttpRequestMessage(HttpMethod.Post, "https://api.groq.com/openai/v1/chat/completions")
                    { Content = new StringContent(govde.ToJsonString(), Encoding.UTF8, "application/json") };
                    istek.Headers.Authorization = new("Bearer", anahtar.Trim());
                    using var cts = CancellationTokenSource.CreateLinkedTokenSource(t);
                    cts.CancelAfter(TimeSpan.FromSeconds(150));
                    c = await Http.SendAsync(istek, cts.Token);
                    kod = (int)c.StatusCode;
                    ham = await c.Content.ReadAsStringAsync(cts.Token);
                }
                catch (Exception e) when (e is HttpRequestException || (e is OperationCanceledException && !t.IsCancellationRequested))
                {
                    throw new HataMesaji($"Groq'a ulaşılamadı ({e.GetType().Name})");
                }
            }
            using (c)
            {
                if (kod == 200)
                {
                    var metin = JsonNode.Parse(ham)?["choices"]?[0]?["message"]?["content"]?.GetValue<string>() ?? "";
                    metin = Dusunce.Replace(metin, "").Trim();
                    if (metin.Length == 0) throw new HataMesaji("Groq boş cevap verdi");
                    log($"   ✓ Groq cevap verdi ({(int)sw.Elapsed.TotalSeconds} sn).");
                    return metin;
                }
                var hamK = ham.ToLowerInvariant();
                if (kod is 401 or 403) throw new AnahtarHatasi("Groq anahtarı geçersiz — Ayarlar'dan yeniden gir.");
                if (kod == 400 && dusunmeAyari && hamK.Contains("reasoning")) { dusunmeAyari = false; continue; }
                if (kod == 400 && jsonAyari && (hamK.Contains("json") || hamK.Contains("response_format"))) { jsonAyari = false; continue; }
                if ((kod == 413 || (kod == 400 && (hamK.Contains("too large") || hamK.Contains("size")))) && boy > 800) { boy = boy * 2 / 3; continue; }
                if (kod == 429 || kod >= 500)
                {
                    var bekle = 6 * (deneme + 1);
                    if (c.Headers.RetryAfter?.Delta is TimeSpan ra) bekle = (int)Math.Ceiling(ra.TotalSeconds) + 1;
                    if (kod == 429 && (bekle > 60 || hamK.Contains("per day") || hamK.Contains("tokens per day")))
                        throw new HataMesaji("Groq günlük ücretsiz sınırı doldu");
                    if (deneme >= 2) throw new HataMesaji($"Groq yoğun (HTTP {kod})");
                    log($"   Groq {(kod == 429 ? "dakikalık sınır" : "yoğun")}, {bekle} sn bekleniyor...");
                    await Task.Delay(bekle * 1000, t);
                    continue;
                }
                throw new HataMesaji($"Groq HTTP {kod}: {Regex.Replace(ham.Length > 200 ? ham[..200] : ham, @"\s+", " ")}");
            }
        }
        throw new HataMesaji("Groq okuyamadı");
    }

    // ------------------------------------------------------------------ Ollama (yerel)
    public static async Task<string> Ollama(IList<string> grup, string model, string istem, CancellationToken t)
    {
        var resimler = new JsonArray();
        foreach (var g in grup)
        {
            if (g.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) throw new HataMesaji("Ollama PDF okuyamaz; menüyü resim (jpg/png) olarak ver.");
            resimler.Add(Resim.DosyaHazirla(g).b64);
        }
        var govde = new JsonObject
        {
            ["model"] = string.IsNullOrWhiteSpace(model) ? OllamaModel : model, ["stream"] = false,
            ["options"] = new JsonObject { ["temperature"] = 0.1, ["num_ctx"] = 8192 },
            ["messages"] = new JsonArray(new JsonObject { ["role"] = "user", ["content"] = istem, ["images"] = resimler }),
        };
        HttpResponseMessage c;
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(t);
        cts.CancelAfter(TimeSpan.FromSeconds(900));
        try { c = await Http.PostAsync(OllamaUrl + "/api/chat", new StringContent(govde.ToJsonString(), Encoding.UTF8, "application/json"), cts.Token); }
        catch (HttpRequestException) { throw new HataMesaji("Ollama çalışmıyor. ollama.com'dan kur, sonra: ollama pull " + OllamaModel); }
        using (c)
        {
            var js = await c.Content.ReadAsStringAsync(t);
            if (!c.IsSuccessStatusCode) throw new HataMesaji("Ollama hatası: " + (js.Length > 300 ? js[..300] : js));
            return JsonNode.Parse(js)?["message"]?["content"]?.GetValue<string>() ?? "";
        }
    }

    // ------------------------------------------------------------------ üst seviye
    public sealed record Saglayici(string Ad, string GeminiAnahtar, string Model, string MistralAnahtar, string GroqAnahtar = "");

    /// <summary>Sırayla: Gemini Flash → Gemma (aynı anahtar) → Groq → Mistral. Biri olmazsa sonrakine geçer.</summary>
    static async Task<T> Zincir<T>(Saglayici s, Action<string> log, CancellationToken t,
        Func<Task<T>> gemini, Func<Task<T>> groq, Func<Task<T>> mistral)
    {
        var adimlar = new List<(string ad, Func<Task<T>> f)>();
        if (!string.IsNullOrWhiteSpace(s.GeminiAnahtar)) adimlar.Add(("Gemini", gemini));
        if (!string.IsNullOrWhiteSpace(s.GroqAnahtar)) adimlar.Add(("Groq", groq));
        if (!string.IsNullOrWhiteSpace(s.MistralAnahtar)) adimlar.Add(("Mistral", mistral));
        if (adimlar.Count == 0) throw new AnahtarHatasi("Yapay zekâ anahtarı yok — Ayarlar'dan ücretsiz Gemini (ya da Groq) anahtarını gir.");
        var hatalar = new List<string>();
        AnahtarHatasi anahtarHatasi = null;
        for (int i = 0; i < adimlar.Count; i++)
        {
            var (ad, f) = adimlar[i];
            if (i > 0) log($"   Yedek: {ad} ile okunuyor...");
            try { return await f(); }
            catch (OperationCanceledException) when (t.IsCancellationRequested) { throw; }
            catch (AnahtarHatasi e) { anahtarHatasi = e; hatalar.Add(e.Message); log("   " + e.Message); }
            catch (HataMesaji e) { hatalar.Add(e.Message); log($"   {ad} okuyamadı ({(e.Message.Length > 160 ? e.Message[..160] : e.Message)})."); }
        }
        if (anahtarHatasi != null && hatalar.Count == 1) throw anahtarHatasi;
        throw new HataMesaji("Yapay zekâ okuyamadı: " + string.Join(" | ", hatalar.Select(h => h.Length > 120 ? h[..120] : h)));
    }

    static async Task<string> Sor(IList<string> grup, Saglayici s, string istem, Action<string> log, CancellationToken t)
    {
        if (s.Ad == "ollama") return await Ollama(grup, s.Model, istem, t);
        return await Zincir(s, log, t,
            () => Gemini(grup, s.GeminiAnahtar, s.Model, istem, log, t),
            async () =>
            {
                if (grup.Count <= GroqResimSiniri) return await Groq(grup, s.GroqAnahtar, istem, log, t);
                var parca = new List<string>();
                for (int i = 0; i < grup.Count; i += GroqResimSiniri)
                    parca.Add((await Groq(grup.Skip(i).Take(GroqResimSiniri).ToList(), s.GroqAnahtar, istem, log, t)).Trim());
                return string.Join("\n\n", parca);
            },
            () => Mistral(grup, s.MistralAnahtar, istem, log, t));
    }

    public static async Task<string> MenuyuOku(IList<string> dosyalar, Saglayici s, Action<string> log, CancellationToken t,
        Action<int, int> ilerleme = null)
    {
        var sonuc = new List<string>();
        for (int i = 0; i < dosyalar.Count; i++)
        {
            ilerleme?.Invoke(i, dosyalar.Count);
            log($"Menü okunuyor ({i + 1}/{dosyalar.Count}): {Path.GetFileName(dosyalar[i])}");
            sonuc.Add((await Sor(new[] { dosyalar[i] }, s, MenuPrompt, log, t)).Trim());
        }
        return string.Join("\n\n", sonuc);
    }

    public static async Task<string> ListeyiDuzenle(string metin, Saglayici s, Action<string> log, CancellationToken t)
    {
        log("Liste düz yazılmış, kategorilere ayrılıyor...");
        return (await Sor(Array.Empty<string>(), s, ListePrompt + metin.Trim(), log, t)).Trim();
    }

    // ------------------------------------------------------------------ arama terimleri
    const string AramaPrompt = """
Aşağıda bir işletmenin ürünleri numaralı olarak var (kategori yolu → ürün adı). Her ürün için internette o ürünün
DOĞRU fotoğrafını bulduracak 2-6 kelimelik Türkçe görsel arama cümlesi yaz.

Kurallar:
- Ürün adı uydurma ya da İngilizce bir isim olabilir ("Hot Chick", "Munchy Chips", "Truffle Mayo"). Kategoriye bakıp ürünün
  NE OLDUĞUNU yaz: "acılı tavuk burger", "baharatlı patates kızartması", "trüflü mayonez sos".
- Her aramada yemeğin/ürünün türü mutlaka geçsin (burger, patates kızartması, sos, dürüm, içecek, tatlı...). İnsan, yer,
  film, futbolcu, giysi ile karışabilecek kelimeleri tek başına bırakma ("mayo" yerine "mayonez").
- Bilinen markalı ürünlerde markayı koru ("coca cola kutu", "ülker çikolatalı gofret", "efes pilsen şişe").

SADECE şu JSON'u döndür: {"aramalar":[{"no":1,"arama":"..."}]}

Ürünler:
""";

    public static async Task<Dictionary<int, string>> AramaTerimleri(IList<(string kategori, string ad)> urunler, Saglayici s,
        Action<string> log, CancellationToken t)
    {
        var istem = AramaPrompt + string.Join("\n", urunler.Select((u, i) => $"{i + 1}. {u.kategori} → {u.ad}"));
        Dictionary<int, string> Coz(string cevap)
        {
            var d = new Dictionary<int, string>();
            if (JsonCoz(cevap)?["aramalar"] is JsonArray a)
                foreach (var x in a)
                    if (x is JsonObject o && PyInt(o["no"]) is int no && o["arama"] is JsonValue v && v.GetValueKind() == JsonValueKind.String)
                        d[no] = v.GetValue<string>();
            if (d.Count == 0) throw new HataMesaji("arama terimi gelmedi");
            return d;
        }
        var bos = Array.Empty<string>();
        return await Zincir(s, log, t,
            async () => Coz(await Gemini(bos, s.GeminiAnahtar, s.Model, istem, log, t, jsonMod: true)),
            async () => Coz(await Groq(bos, s.GroqAnahtar, istem, log, t, jsonMod: true)),
            async () => Coz(await Mistral(bos, s.MistralAnahtar, istem, log, t, jsonMod: true)));
    }

    // ------------------------------------------------------------------ resim kontrolü
    /// <summary>Aday resimlerden ürünü gösterenin numarası (1..n); hiçbiri değilse 0; kontrol yapılamazsa -1.</summary>
    public static async Task<int> ResimSec(IList<string> dosyalar, string ad, string arama, string kategori, Saglayici s, CancellationToken t)
    {
        if (s.Ad == "ollama" || dosyalar.Count == 0) return -1;
        var istem = $"Bir restoran menüsüne ürün fotoğrafı seçiyorum. Ürün adı: \"{ad}\". Kategori: {kategori}. " +
                    (Metin.Norm(arama) != Metin.Norm(ad) ? $"Ürün şu: {arama}. " : "") +
                    $"Sana {dosyalar.Count} aday resim verdim (sırayla 1-{dosyalar.Count}). Bu ürünü (yemeği, içeceği ya da paketli ürünü) " +
                    "en iyi gösteren resmin numarasını seç. İnsan, manzara, eşya, alet, logo, yazı ağırlıklı ya da BAŞKA bir yemeği " +
                    "gösteren resimleri seçme. Hiçbiri uygun değilse 0 yaz.\nSADECE şu JSON'u döndür: {\"secim\": numara}";
        int Coz(string cevap)
        {
            var n = PyInt(JsonCoz(cevap)?["secim"]);
            if (n == null || n < 0 || n > dosyalar.Count) throw new HataMesaji("geçersiz seçim");
            return n.Value;
        }
        Action<string> sessiz = _ => { };
        var sira = new List<Func<Task<int>>>();
        if (!string.IsNullOrWhiteSpace(s.GroqAnahtar)) sira.Add(async () => Coz(await Groq(dosyalar, s.GroqAnahtar, istem, sessiz, t, jsonMod: true)));
        if (!string.IsNullOrWhiteSpace(s.GeminiAnahtar)) sira.Add(async () => Coz(await Gemini(dosyalar, s.GeminiAnahtar, s.Model, istem, sessiz, t, jsonMod: true, resimBoyu: 512)));
        if (!string.IsNullOrWhiteSpace(s.MistralAnahtar)) sira.Add(async () => Coz(await Mistral(dosyalar, s.MistralAnahtar, istem, sessiz, t, jsonMod: true, resimBoyu: 512)));
        foreach (var f in sira)
        {
            try { return await f(); }
            catch (OperationCanceledException) when (t.IsCancellationRequested) { throw; }
            catch (Exception) { }
        }
        return -1;
    }

    // ------------------------------------------------------------------ broşür (JSON)
    public static JsonNode JsonCoz(string metin)
    {
        var t = Regex.Replace(Py.Strip(metin ?? ""), @"^```(?:json)?\s*|\s*```\z", "");
        try { return JsonNode.Parse(t); } catch (JsonException) { }
        var m = Regex.Match(t, @"\{.*\}", RegexOptions.Singleline);
        if (!m.Success) throw new HataMesaji("Menü okunamadı: geçerli veri gelmedi.");
        t = Regex.Replace(m.Value, @",\s*([}\]])", "$1");
        try { return JsonNode.Parse(t); }
        catch (JsonException) { throw new HataMesaji("Menü okunamadı: yapay zekânın cevabı bozuk geldi, tekrar dene."); }
    }

    // Python truthiness / str() taklidi
    static bool Dolu(JsonNode n) => n switch
    {
        null => false,
        JsonArray a => a.Count > 0,
        JsonObject o => o.Count > 0,
        JsonValue v => v.GetValueKind() switch
        {
            JsonValueKind.String => v.GetValue<string>().Length > 0,
            JsonValueKind.Number => v.GetValue<double>() != 0,
            JsonValueKind.True => true,
            _ => false,
        },
        _ => false,
    };

    static bool TamSayiMi(JsonValue v) { var s = v.ToJsonString(); return !s.Contains('.') && !s.Contains('e') && !s.Contains('E'); }

    public static string PyStr(JsonNode n)
    {
        switch (n)
        {
            case null: return "None";
            case JsonValue v:
                switch (v.GetValueKind())
                {
                    case JsonValueKind.String: return v.GetValue<string>();
                    case JsonValueKind.True: return "True";
                    case JsonValueKind.False: return "False";
                    case JsonValueKind.Number:
                        return TamSayiMi(v) ? v.ToJsonString() : Py.FloatRepr(v.GetValue<double>());
                }
                return v.ToJsonString();
            default: return n.ToJsonString();
        }
    }

    static string Str(JsonNode n, string varsayilan) => Dolu(n) ? PyStr(n) : varsayilan;

    static double? F(JsonNode v)
    {
        if (v is JsonValue jv)
        {
            var k = jv.GetValueKind();
            if (k == JsonValueKind.Number) { var d = jv.GetValue<double>(); return d > 0 ? d : null; }
            if (k is JsonValueKind.True) return 1.0;
            if (k is JsonValueKind.False) return null;
            if (k == JsonValueKind.String && jv.GetValue<string>() == "") return null;
        }
        if (v == null) return null;
        return Metin.FiyatCoz(PyStr(v));
    }

    static int? PyInt(JsonNode n)
    {
        if (n is not JsonValue v) return null;
        switch (v.GetValueKind())
        {
            case JsonValueKind.Number: return (int)Math.Truncate(v.GetValue<double>());
            case JsonValueKind.True: return 1;
            case JsonValueKind.False: return 0;
            case JsonValueKind.String:
                var s = Py.Strip(v.GetValue<string>());
                return Regex.IsMatch(s, @"^[+-]?\d+$") ? int.Parse(s, Py.CI) : null;
        }
        return null;
    }

    static double? PyFloat(JsonNode n)
    {
        if (n is not JsonValue v) return null;
        switch (v.GetValueKind())
        {
            case JsonValueKind.Number: return v.GetValue<double>();
            case JsonValueKind.True: return 1;
            case JsonValueKind.False: return 0;
            case JsonValueKind.String:
                return double.TryParse(Py.Strip(v.GetValue<string>()), System.Globalization.NumberStyles.Float, Py.CI, out var d) ? d : null;
        }
        return null;
    }

    public sealed record Brosur(string Metin, Dictionary<string, Arama.Kirpma> Kirpmalar, List<string> Farklar);

    /// <summary>JSON → liste metni + broşürden kesilecek resimler + farklar</summary>
    public static Brosur BrosurJsonMetne(JsonNode veri, IList<string> dosyalar)
    {
        var sira = new List<(string, string, string)>();
        var gruplar = new Dictionary<(string, string, string), List<string>>();
        var kirp = new Dictionary<string, Arama.Kirpma>();
        var vo = veri as JsonObject;
        foreach (var u in (Dolu(vo?["urunler"]) ? vo["urunler"] as JsonArray : null) ?? new JsonArray())
        {
            if (u is not JsonObject uo) continue;
            var ad = Metin.GuvenliAd(Py.Strip(Str(uo["ad"], "")));
            if (ad.Length == 0) continue;
            var kat = Metin.GuvenliAd(Str(uo["kategori"], "Genel")); if (kat.Length == 0) kat = "Genel";
            var pisme = Metin.GuvenliAd(Str(uo["pisme"], "Hazır")); if (pisme.Length == 0) pisme = "Hazır";
            var tip = Metin.Norm(Str(uo["tip"], "")).Contains("men") ? "Menü" : "Sadece Yemek";
            var anahtar = (kat, pisme, tip);
            if (!gruplar.ContainsKey(anahtar)) { gruplar[anahtar] = new(); sira.Add(anahtar); }
            var fiyat = F(uo["fiyat"]);
            var pors = new List<Porsiyon>();
            if (Dolu(uo["porsiyonlar"]) && uo["porsiyonlar"] is JsonArray pa)
                foreach (var p in pa)
                    if (p is JsonObject po && Dolu(po["ad"]))
                    {
                        var e = Metin.PorsiyonEtiketi(Str(po["ad"], ""));
                        if (e.Length > 0) pors.Add(new Porsiyon(e, F(po["fiyat"])));
                    }
            var satir = ad + " | " + Str(uo["arama"], ad).Replace("|", " ");
            bool fv = fiyat.HasValue && fiyat.Value != 0;
            if (fv || pors.Count > 0) satir += " | " + (fv ? Py.G(fiyat.Value) : "");
            if (pors.Count > 0) satir += " | " + Metin.PorsiyonlariYaz(pors);
            gruplar[anahtar].Add(satir);
            if (uo["resim"] is JsonObject r && r["kutu"] is JsonArray kutuJ && kutuJ.Count == 4)
            {
                var sayfa = Dolu(r["sayfa"]) ? PyInt(r["sayfa"]) : 1;
                var kutu = kutuJ.Select(PyFloat).ToList();
                if (sayfa.HasValue && kutu.All(x => x.HasValue) && sayfa >= 1 && sayfa <= dosyalar.Count)
                    kirp[Metin.Norm(ad)] = new Arama.Kirpma(dosyalar[sayfa.Value - 1], kutu.Select(x => x.Value).ToArray());
            }
        }
        var cikti = new List<string>();
        (string, string) onceki = (null, null);
        foreach (var (kat, pisme, tip) in sira)
        {
            if (onceki.Item1 != kat)
            {
                if (cikti.Count > 0) cikti.Add("");
                cikti.Add("# " + kat);
                onceki = (kat, null);
            }
            if (onceki.Item2 != pisme) { cikti.Add("## " + pisme); onceki = (kat, pisme); }
            cikti.Add("### " + tip);
            cikti.AddRange(gruplar[(kat, pisme, tip)]);
        }
        List<string> Dizi(string k) => (Dolu(vo?[k]) && vo[k] is JsonArray a ? a : new JsonArray())
            .Select(PyStr).Where(x => Py.Strip(x).Length > 0).ToList();
        var odeme = Dizi("odeme");
        if (odeme.Count > 0) { cikti.Add(""); cikti.Add("ÖDEME: " + string.Join(", ", odeme)); }
        return new Brosur(string.Join("\n", cikti), kirp, Dizi("farklar"));
    }

    public static async Task<Brosur> BrosurOku(IList<string> dosyalar, string listeMetni, Saglayici s, Action<string> log, CancellationToken t)
    {
        var liste = Py.Strip(listeMetni ?? "");
        var listeNotu = liste.Length > 0
            ? "\nAyrıca müşterinin YAZILI LİSTESİ aşağıda; broşürle karşılaştır, fiyatlarda bunu esas al:\n-----\n" +
              (liste.Length > 12000 ? liste[..12000] : liste) + "\n-----"
            : "";
        var ilk6 = dosyalar.Take(6).ToList();
        var istem = BirlesikPrompt.Replace("{n}", ilk6.Count.ToString()).Replace("{liste_notu}", listeNotu);
        log($"Broşür{(listeNotu.Length > 0 ? " + liste" : "")} okunuyor ({ilk6.Count} resim)...");
        if (s.Ad == "ollama")
            return BrosurJsonMetne(JsonCoz(await Ollama(ilk6, s.Model, istem, t)), ilk6) with { Kirpmalar = new() };
        return await Zincir(s, log, t,
            async () => BrosurJsonMetne(JsonCoz(await Gemini(ilk6, s.GeminiAnahtar, s.Model, istem, log, t, jsonMod: true, resimBoyu: 2048)), ilk6),
            async () =>
            {
                log("   (Groq ile broşür fotoğrafları kesilmez, resimler internetten bulunur)");
                var birlesik = new JsonObject { ["urunler"] = new JsonArray(), ["odeme"] = new JsonArray(), ["farklar"] = new JsonArray() };
                var gorulen = new HashSet<string>();
                for (int i = 0; i < ilk6.Count; i += GroqResimSiniri)
                {
                    var grup = ilk6.Skip(i).Take(GroqResimSiniri).ToList();
                    var ist = BirlesikPrompt.Replace("{n}", grup.Count.ToString()).Replace("{liste_notu}", listeNotu);
                    if (JsonCoz(await Groq(grup, s.GroqAnahtar, ist, log, t, jsonMod: true)) is not JsonObject v) continue;
                    if (v["urunler"] is JsonArray us)
                        foreach (var u in us.ToList())
                        {
                            if (u is not JsonObject uo) continue;
                            var anahtar = Metin.Norm(uo["ad"] is JsonValue av && av.GetValueKind() == JsonValueKind.String ? av.GetValue<string>() : "");
                            if (anahtar.Length == 0 || !gorulen.Add(anahtar)) continue;
                            us.Remove(uo);
                            uo["resim"] = null;
                            ((JsonArray)birlesik["urunler"]).Add(uo);
                        }
                    foreach (var k in new[] { "odeme", "farklar" })
                        if (v[k] is JsonArray d)
                            foreach (var x in d.ToList()) { d.Remove(x); ((JsonArray)birlesik[k]).Add(x); }
                }
                return BrosurJsonMetne(birlesik, ilk6) with { Kirpmalar = new() };
            },
            async () =>
            {
                log("   (Mistral ile broşür fotoğrafları kesilmez, resimler internetten bulunur)");
                return BrosurJsonMetne(JsonCoz(await Mistral(ilk6, s.MistralAnahtar, istem, log, t, jsonMod: true)), ilk6) with { Kirpmalar = new() };
            });
    }
}
