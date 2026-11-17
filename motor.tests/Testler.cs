using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using UrunMotor;
using Xunit;
using Xunit.Abstractions;

namespace Motor.Tests;

/// <summary>Python programının (v3.0) ürettiği beklenen sonuçlarla birebir karşılaştırma.</summary>
public static class Fx
{
    static Fx()
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }
    public static string Yol(string ad) => Path.Combine(AppContext.BaseDirectory, "Fixtures", ad);
    public static JsonNode Oku(string ad) => JsonNode.Parse(File.ReadAllText(Yol(ad), Encoding.UTF8));
    public static string S(JsonNode n) => n?.GetValue<string>();
    public static double? D(JsonNode n) => n == null ? null : n.GetValue<double>();

    /// <summary>Python kat_json biçimi: [[yol, [[ad, arama, fiyat, [[e, f]]]]]]</summary>
    public static string KatJson(List<Kategori> k)
    {
        var a = new JsonArray(k.Select(x => (JsonNode)new JsonArray(x.Yol, new JsonArray(x.Urunler.Select(u => (JsonNode)new JsonArray(
            u.Ad, u.Arama, u.Fiyat.HasValue ? JsonValue.Create(u.Fiyat.Value) : null,
            new JsonArray(u.Porsiyonlar.Select(p => (JsonNode)new JsonArray(p.Ad, p.Fiyat.HasValue ? JsonValue.Create(p.Fiyat.Value) : null)).ToArray())
        )).ToArray()))).ToArray());
        return Normal(a);
    }

    /// <summary>JSON'u karşılaştırılabilir hale getirir (sayılar double olarak).</summary>
    public static string Normal(JsonNode n) => n switch
    {
        null => "null",
        JsonArray a => "[" + string.Join(",", a.Select(Normal)) + "]",
        JsonObject o => "{" + string.Join(",", o.OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => JsonSerializer.Serialize(x.Key) + ":" + Normal(x.Value))) + "}",
        JsonValue v when v.GetValueKind() == JsonValueKind.Number => double.Parse(v.ToJsonString(), CultureInfo.InvariantCulture).ToString("R", CultureInfo.InvariantCulture),
        JsonValue v when v.GetValueKind() == JsonValueKind.String => JsonSerializer.Serialize(v.GetValue<string>()),
        _ => n.ToJsonString(),
    };

    public static List<Kategori> Hazirla(string metin) =>
        Metin.ListeyiAyristir(Metin.BasliksizMi(metin) ? Metin.YerelSiniflandir(metin) : metin);

    public static string GeciciKlasor()
    {
        var y = Path.Combine(Path.GetTempPath(), "motortest_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(y);
        return y;
    }
}

public class MetinTestleri
{
    static readonly JsonNode F = Fx.Oku("metin.json");

    public static IEnumerable<object[]> Vakalar() => F["vakalar"].AsArray().Select((v, i) => new object[] { i });

    [Theory, MemberData(nameof(Vakalar))]
    public void Satir(int i)
    {
        var v = F["vakalar"][i];
        var g = Fx.S(v["girdi"]);
        var c = Metin.SatiriCoz(g);
        var bekle = v["satiri_coz"];
        Assert.Equal(Fx.S(bekle[0]), c.Ad);
        Assert.Equal(Fx.S(bekle[1]), c.Arama);
        Assert.Equal(Fx.D(bekle[2]), c.Fiyat);
        Assert.Equal(Fx.Normal(bekle[3]), Fx.Normal(new JsonArray(c.Porsiyonlar.Select(p => (JsonNode)new JsonArray(p.Ad, p.Fiyat.HasValue ? JsonValue.Create(p.Fiyat.Value) : null)).ToArray())));

        var ay = Metin.PorsiyonAyir(g);
        if (v["porsiyon_ayir"] == null) Assert.Null(ay);
        else
        {
            Assert.NotNull(ay);
            Assert.Equal(Fx.S(v["porsiyon_ayir"][0]), ay.Kok);
            Assert.Equal(Fx.S(v["porsiyon_ayir"][1]), ay.Etiket);
            Assert.Equal(v["porsiyon_ayir"][2].GetValue<bool>(), ay.CiplakPorsiyon);
        }
        Assert.Equal(Fx.D(v["fiyat_coz"]), Metin.FiyatCoz(g));
        Assert.Equal(Fx.S(v["porsiyon_etiketi"]), Metin.PorsiyonEtiketi(g));
        Assert.Equal(Fx.S(v["norm"]), Metin.Norm(g));
        Assert.Equal(Fx.S(v["pors_norm"]), Metin.PorsNorm(g));
        Assert.Equal(Fx.S(v["guvenli_ad"]), Metin.GuvenliAd(g));
        Assert.Equal(Fx.S(v["tr_buyuk"]), Metin.TrBuyuk(g));
        Assert.Equal(Fx.S(v["hex"]), Metin.HexMetin(g));
        Assert.Equal(v["odeme_satiri"].GetValue<bool>(), Metin.OdemeSatiriMi(g));
        Assert.Equal(Fx.S(v["duzelt_ad"]), Metin.DuzeltAd(g));
        Assert.Equal(Fx.S(v["emojisiz"]), Metin.Emojisiz(g + " 🍔 ☕️"));
        Assert.Equal(Fx.Normal(v["porsiyonlari_coz"]), Fx.Normal(new JsonArray(Metin.PorsiyonlariCoz(g).Select(p => (JsonNode)new JsonArray(p.Ad, p.Fiyat.HasValue ? JsonValue.Create(p.Fiyat.Value) : null)).ToArray())));
    }

    [Fact]
    public void FiyatSayiBicim()
    {
        foreach (var x in F["fiyat"].AsArray()) Assert.Equal(Fx.D(x[1]), Metin.FiyatCoz(Fx.S(x[0])));
        foreach (var x in F["sayi"].AsArray()) Assert.Equal(Fx.S(x[1]), Metin.Sayi(x[0].GetValue<double>()));
        foreach (var x in F["gfmt"].AsArray()) Assert.Equal(Fx.S(x[1]), Py.G(x[0].GetValue<double>()));
        Assert.Equal(Fx.S(F["pors_yaz"]), Metin.PorsiyonlariYaz(new[] { new Porsiyon("Az", 150), new Porsiyon("1,5", 280.5) }));
        Assert.Equal("-50.0", Py.FloatRepr(-50));
        Assert.Equal("12.5", Py.FloatRepr(12.5));
        Assert.Equal("0.1", Py.FloatRepr(0.1));
    }
}

public class ListeTestleri
{
    static readonly JsonNode F = Fx.Oku("listeler.json");
    public static IEnumerable<object[]> Vakalar() => F["vakalar"].AsArray().Select((v, i) => new object[] { i });

    [Theory, MemberData(nameof(Vakalar))]
    public void Liste(int i)
    {
        var v = F["vakalar"][i];
        var t = Fx.S(v["metin"]);
        Assert.Equal(v["basliksiz"].GetValue<bool>(), Metin.BasliksizMi(t));
        Assert.Equal(Fx.Normal(v["ayristir"]), Fx.KatJson(Metin.ListeyiAyristir(t)));
        Assert.Equal(Fx.Normal(v["odeme"]), Fx.Normal(new JsonArray(Metin.OdemeKodlariBul(t).Select(x => (JsonNode)x).ToArray())));
        if (v["yerel"] != null)
        {
            var y = Metin.YerelSiniflandir(t);
            Assert.Equal(Fx.S(v["yerel"]), y);
            Assert.Equal(Fx.Normal(v["yerel_ayristir"]), Fx.KatJson(Metin.ListeyiAyristir(y)));
        }
    }

    [Fact]
    public void OdemeSatirlari()
    {
        foreach (var x in F["odeme"].AsArray())
            Assert.Equal(Fx.Normal(x[1]), Fx.Normal(new JsonArray(Metin.OdemeKodlariBul(Fx.S(x[0])).Select(k => (JsonNode)k).ToArray())));
    }
}

public class BrosurTestleri
{
    static readonly JsonNode F = Fx.Oku("brosur.json");

    [Fact]
    public void EksikKontrol() =>
        Assert.Equal(Fx.Normal(F["eksik"]), Fx.Normal(new JsonArray(Metin.ListeEksikKontrol(Fx.S(F["liste"]), Fx.S(F["sonuc"])).Select(x => (JsonNode)x).ToArray())));

    [Fact]
    public void JsonMetne()
    {
        var dosyalar = F["dosyalar"].AsArray().Select(Fx.S).ToList();
        var b = Yapay.BrosurJsonMetne(F["veri"].DeepClone(), dosyalar);
        Assert.Equal(Fx.S(F["metin"]), b.Metin);
        var kirp = new JsonObject();
        foreach (var (k, v) in b.Kirpmalar) kirp[k] = new JsonArray(v.Dosya, new JsonArray(v.Kutu.Select(x => (JsonNode)JsonValue.Create(x)).ToArray()));
        Assert.Equal(Fx.Normal(F["kirp"]), Fx.Normal(kirp));
        Assert.Equal(Fx.Normal(F["farklar"]), Fx.Normal(new JsonArray(b.Farklar.Select(x => (JsonNode)x).ToArray())));
    }

    [Fact]
    public void JsonCoz()
    {
        foreach (var x in F["json_coz"].AsArray())
            Assert.Equal(Fx.Normal(x[1]), Fx.Normal(Yapay.JsonCoz(Fx.S(x[0]))));
    }
}

public class AramaTestleri
{
    static readonly JsonNode F = Fx.Oku("arama.json");

    [Fact]
    public void AramaCumlesi()
    {
        foreach (var x in F["arama"].AsArray())
            Assert.Equal(Fx.S(x[3]), Arama.AramaOlustur(Fx.S(x[0]), Fx.S(x[1]), Fx.S(x[2])));
    }

    [Fact]
    public void Puan()
    {
        foreach (var x in F["puan"].AsArray())
        {
            var r = x[0].AsObject().ToDictionary(k => k.Key, k => k.Value is JsonValue v && v.GetValueKind() == JsonValueKind.String ? v.GetValue<string>() : k.Value?.ToJsonString());
            Assert.Equal(x[2].GetValue<int>(), Arama.SonucPuani(r, Fx.S(x[1])));
        }
    }
}

public class ExcelKlasorTestleri
{
    [Fact]
    public void ExcelOku()
    {
        foreach (var x in Fx.Oku("excel.json")["vakalar"].AsArray())
            Assert.Equal(Fx.S(x[1]), Excel.Oku(Fx.Yol(Fx.S(x[0]))));
    }

    [Fact]
    public void ExcelYazOku()
    {
        var tmp = Fx.GeciciKlasor();
        var k = Metin.ListeyiAyristir("# Döner\n## Izgara\n### Sadece Yemek\nTavuk Döner | tavuk döner | 200 | Az=150; 1,5=280\n# İçecekler\nAyran | ayran | 30");
        Resim.YedekGorsel("Ayran", "İçecekler", Path.Combine(tmp, "İçecekler", "Ayran.jpg"));
        var hedef = Path.Combine(tmp, "menu.xlsx");
        Assert.Equal(2, Excel.Yaz(k, tmp, hedef, "Test Firma"));
        var geri = Excel.Oku(hedef);
        Assert.Contains("TAVUK DÖNER", geri);
        Assert.Contains("AYRAN", geri);
        Directory.Delete(tmp, true);
    }

    [Fact]
    public void ZipTara()
    {
        var f = Fx.Oku("klasor.json");
        var t = Klasor.Tara(Fx.Yol(Fx.S(f["zip"])));
        Assert.Equal(Fx.Normal(f["kategoriler"]), Fx.KatJson(t.Kategoriler));
        Assert.Equal(f["anahtarlar"].AsArray().Select(Fx.S).ToList(), t.Gercek.Keys.ToList());
        Assert.All(t.Gercek.Values, d => Assert.True(Directory.Exists(d)));
    }
}

public class SqlTestleri
{
    readonly ITestOutputHelper _o;
    public SqlTestleri(ITestOutputHelper o) { _o = o; }
    static readonly JsonNode F = Fx.Oku("sql.json");
    public static IEnumerable<object[]> Vakalar() => F["vakalar"].AsArray().Select((v, i) => new object[] { i });
    static string Maske(string s) => Regex.Replace(s, "0x[0-9A-F]{200,}", "IMG");

    [Theory, MemberData(nameof(Vakalar))]
    public void SqlCiktisi(int i)
    {
        var v = F["vakalar"][i];
        var k = Metin.ListeyiAyristir(Fx.S(v["metin"]));
        var resimli = v["resimli"].AsArray().Select(Fx.S).ToHashSet();
        var tmp = Fx.GeciciKlasor();
        try
        {
            foreach (var kat in k)
                foreach (var u in kat.Urunler)
                    if (resimli.Contains(u.Ad)) Resim.YedekGorsel(u.Ad, kat.Yol, Path.Combine(Sql.KlasorYolu(tmp, kat.Yol), u.Ad + ".jpg"));
            var loglar = new List<string>();
            var cikti = Path.Combine(tmp, "out.sql");
            Sql.SqlOlustur(Fx.Yol(Fx.S(v["dump"])), k, tmp, cikti, v["fiyat"].GetValue<double>(), v["kdv"].GetValue<double>(),
                v["seviye"].GetValue<int>(), log: loglar.Add, ekOdemeler: v["ek"].AsArray().Select(Fx.S).ToList());
            var ham = File.ReadAllBytes(cikti);
            var metin = Encoding.GetEncoding(1254).GetString(ham);
            Assert.Equal(v["crlf"].GetValue<bool>(), metin.Contains("\r\n"));
            var bekle = Fx.S(v["beklenen"]);
            var gercek = Maske(metin);
            if (bekle != gercek)
            {
                var a = bekle.Split('\n'); var b = gercek.Split('\n');
                for (int j = 0; j < Math.Max(a.Length, b.Length); j++)
                    if (j >= a.Length || j >= b.Length || a[j] != b[j])
                    {
                        _o.WriteLine($"satır {j + 1}\nPY: {(j < a.Length ? a[j] : "<yok>")}\nC#: {(j < b.Length ? b[j] : "<yok>")}");
                        break;
                    }
            }
            Assert.Equal(bekle, gercek);
            Assert.Equal(v["loglar"].AsArray().Select(Fx.S).ToList(), loglar.Where(l => !l.StartsWith("SQL yazıldı", StringComparison.Ordinal)).ToList());
        }
        finally { Directory.Delete(tmp, true); }
    }

    [Fact]
    public void DumpYardimcilari()
    {
        Assert.Equal(Fx.S(F["firma"]), Sql.FirmaAdiBul(Fx.Yol("test_dump.sql")));
        var (d0, _) = Sql.DumpOku(Fx.Yol("test_dump.sql"));
        var sut = Sql.SutunlariBul(d0, "tbl_urun");
        Assert.Equal(Fx.Normal(F["sutunlar"]), Fx.Normal(new JsonArray(sut.Select(s => (JsonNode)new JsonObject
        {
            ["ad"] = s.Ad, ["tip"] = s.Tip, ["not_null"] = s.NotNull, ["default"] = s.Default, ["auto"] = s.Auto,
        }).ToArray())));
        Assert.Equal(Fx.S(F["eski_duzelt"]), Sql.EskiInsertleriDuzelt(d0));
        Assert.Equal("TEST ŞİŞ & ÇİĞ KÖFTE", Sql.FirmaAdiBul(Fx.Yol("test_dump_crlf.sql")));
    }
}

public class ProTestleri
{
    static readonly JsonNode F = Fx.Oku("pro.json");
    public static IEnumerable<object[]> Vakalar() => F["vakalar"].AsArray().Select((v, i) => new object[] { i });
    static readonly string[] Tablolar = { "categories", "products", "detaylar", "urun_detaylar", "silinen_kayitlar", "payment_methods", "users",
        "orders", "order_items", "payments", "shifts", "sms_history", "members", "member_adresleri", "firmalar", "printers", "yazici_eslesme" };

    static JsonObject Dok(string db)
    {
        var o = new JsonObject();
        using var c = new SqliteConnection($"Data Source={db};Pooling=False;Mode=ReadOnly");
        c.Open();
        foreach (var t in Tablolar)
        {
            var satirlar = new JsonArray();
            using var k = c.CreateCommand();
            k.CommandText = $"SELECT * FROM {t} ORDER BY rowid";
            using var r = k.ExecuteReader();
            while (r.Read())
            {
                var d = new JsonObject();
                for (int i = 0; i < r.FieldCount; i++)
                {
                    var ad = r.GetName(i);
                    var val = r.IsDBNull(i) ? null : r.GetValue(i);
                    JsonNode n = val switch
                    {
                        null => null, long l => JsonValue.Create(l), double x => JsonValue.Create(x), string s => JsonValue.Create(s),
                        _ => JsonValue.Create(val.ToString()),
                    };
                    if ((ad == "updated_at" || ad == "silindi_at") && val is string s2 && s2.Length > 0 && t != "users" && t != "orders") n = "ZAMAN";
                    if (ad == "image" && val is string im && im.Length > 0) n = Regex.Replace(im, @"prod_\d+_", "prod_X_");
                    d[ad] = n;
                }
                satirlar.Add(d);
            }
            o[t] = satirlar;
        }
        return o;
    }

    static string LogMaske(string l) =>
        Regex.Replace(Regex.Replace(l, @"^Pro veritabanı yazıldı: .*?siparismatik\.db  ", "Pro veritabanı yazıldı: X  "),
            @"^Resimler: .*?product-images  →", "Resimler: X  →");

    [Theory, MemberData(nameof(Vakalar))]
    public void ProDb(int i)
    {
        var v = F["vakalar"][i];
        var k = Metin.ListeyiAyristir(Fx.S(v["metin"]));
        var resimli = v["resimli"].AsArray().Select(Fx.S).ToHashSet();
        var tmp = Fx.GeciciKlasor();
        try
        {
            var lib = Path.Combine(tmp, "lib");
            foreach (var kat in k)
                foreach (var u in kat.Urunler)
                    if (resimli.Contains(u.Ad)) Resim.YedekGorsel(u.Ad, kat.Yol, Path.Combine(Sql.KlasorYolu(lib, kat.Yol), u.Ad + ".jpg"));
            var loglar = new List<string>();
            var r = Pro.DbOlustur(Fx.Yol("test_pro.db"), k, lib, Path.Combine(tmp, "out"), 1.0, 10, v["seviye"].GetValue<int>(), loglar.Add,
                null, v["ek"].AsArray().Select(Fx.S).ToList(), "kasa pc", Fx.S(v["opsiyon"]), v["icecek"].GetValue<bool>(), v["demo"].GetValue<bool>());
            var bekle = v["tablolar"].AsObject();
            var gercek = Dok(r.DbYolu);
            foreach (var t in Tablolar)
                Assert.True(Fx.Normal(bekle[t]) == Fx.Normal(gercek[t]), $"{t}\nPY: {Fx.Normal(bekle[t])}\nC#: {Fx.Normal(gercek[t])}");
            var resimler = Directory.GetFiles(r.ResimKlasoru).Select(Path.GetFileName).Select(x => Regex.Replace(x, @"prod_\d+_", "prod_X_"))
                .OrderBy(x => x, StringComparer.Ordinal).ToList();
            Assert.Equal(v["resimler"].AsArray().Select(Fx.S).ToList(), resimler);
            Assert.Equal(v["loglar"].AsArray().Select(x => LogMaske(Fx.S(x))).ToList(), loglar.Select(LogMaske).ToList());
            Assert.False(File.Exists(r.DbYolu + "-wal"));
        }
        finally { try { Directory.Delete(tmp, true); } catch { } }
    }

    [Fact]
    public void FirmaVeDemo()
    {
        Assert.Equal(Fx.S(F["firma"]), Pro.FirmaAdi(Fx.Yol("test_pro.db")));
        var tmp = Fx.GeciciKlasor();
        var db = Path.Combine(tmp, "siparismatik.db");
        File.Copy(Fx.Yol("test_pro.db"), db);
        var loglar = new List<string>();
        var yedek = Pro.DemoTemizleDosya(db, loglar.Add);
        Assert.True(File.Exists(yedek));
        Assert.StartsWith("Demo temizliği: ", loglar[0]);
        Assert.Contains("orders 3", loglar[0]);
        try { Directory.Delete(tmp, true); } catch { }
    }
}

public class ResimTestleri
{
    [Fact]
    public void KareVeHex()
    {
        var tmp = Fx.GeciciKlasor();
        var y = Path.Combine(tmp, "a.jpg");
        Resim.YedekGorsel("Çiğ Köfte Dürüm Çok Uzun Bir Ürün Adı", "Kebap", y);
        Assert.True(File.Exists(y));
        var hex = Resim.ResimHex(y);
        Assert.StartsWith("0xFFD8", hex);
        Assert.True((hex.Length - 2) / 2 <= Resim.MaksResim);
        // küçük resim reddedilir, kucukDeOlur ile kabul edilir
        var kucuk = Resim.Jpeg(new SkiaSharp.SKBitmap(100, 80), 90);
        Assert.False(Resim.KareJpegKaydet(kucuk, Path.Combine(tmp, "k.jpg")));
        Assert.True(Resim.KareJpegKaydet(kucuk, Path.Combine(tmp, "k.jpg"), kucukDeOlur: true));
        using (var b = SkiaSharp.SKBitmap.Decode(Path.Combine(tmp, "k.jpg"))) { Assert.Equal(600, b.Width); Assert.Equal(600, b.Height); }
        Assert.True(Resim.BrosurdenKes(y, new double[] { 100, 100, 600, 700 }, Path.Combine(tmp, "kes.jpg")));
        Assert.False(Resim.BrosurdenKes(y, new double[] { 100, 100, 110, 110 }, Path.Combine(tmp, "kes2.jpg")));
        Directory.Delete(tmp, true);
    }
}

public class ProtokolTestleri
{
    [Fact]
    public async Task Komutlar()
    {
        var c = new Is(1, CancellationToken.None);
        var r = await UrunMotor.Komutlar.Yurut("ayristir", new JsonObject { ["metin"] = "# A\nKola 30\nÖDEME: Sodexo" }, c);
        Assert.Equal(1, r["urunSayisi"].GetValue<int>());
        Assert.Equal("SODEXO", r["odeme"][0].GetValue<string>());
        var s = await UrunMotor.Komutlar.Yurut("siniflandir", new JsonObject { ["metin"] = "Domates\nKola", ["yapayZeka"] = false }, c);
        Assert.Equal("yerel", s["yontem"].GetValue<string>());
        Assert.StartsWith("# Sebzeler", s["metin"].GetValue<string>());
        await Assert.ThrowsAsync<HataMesaji>(() => UrunMotor.Komutlar.Yurut("yok_boyle", new JsonObject(), c));
    }
}
