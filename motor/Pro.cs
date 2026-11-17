using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace UrunMotor;

/// <summary>
/// SiparişMatik Pro (siparismatik.db, SQLite) menü yazıcısı.
///   categories / products (porsiyonlar products.variants JSON'u: [{"name": "1,5", "price": fark}])
///   detaylar + urun_detaylar (opsiyonlar)
///   resimler AppData\Roaming\siparismatik-pro\product-images\ klasöründe; DB tam yolu tutar.
/// </summary>
public static class Pro
{
    public const string VeriKlasoru = @"C:\Users\{kullanici}\AppData\Roaming\siparismatik-pro";
    public const string OpsiyonVarsayilan = "Soğanlı, Soğansız, Domatesli, Domatessiz, Marullu, Marulsuz, Soslu, Sossuz, " +
                                            "Patatesli, Patatessiz, Turşulu, Turşusuz, Mayonezli, Mayonezsiz, " +
                                            "Ketçaplı, Ketçapsız, Acılı, Acısız";

    public static string Simdi() => DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", Py.CI);

    /// <summary>Tek bağlantı + o anki işlem; Microsoft.Data.Sqlite komutlara işlemi elle ister.</summary>
    public sealed class Db : IDisposable
    {
        public readonly SqliteConnection C;
        public SqliteTransaction Islem;
        public Db(string yol, bool salt = false)
        {
            C = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = yol, Pooling = false,
                Mode = salt ? SqliteOpenMode.ReadOnly : SqliteOpenMode.ReadWrite,
            }.ToString());
            C.Open();
        }
        SqliteCommand Kmt(string sql, object[] p)
        {
            var k = C.CreateCommand();
            k.CommandText = sql;
            k.Transaction = Islem;
            for (int i = 0; i < p.Length; i++) k.Parameters.AddWithValue("$p" + i, p[i] ?? DBNull.Value);
            return k;
        }
        static string Yer(string sql)
        {
            int i = 0;
            var sb = new StringBuilder();
            bool tirnak = false;
            foreach (var c in sql)
            {
                if (c == '\'') tirnak = !tirnak;
                if (c == '?' && !tirnak) sb.Append("$p" + i++); else sb.Append(c);
            }
            return sb.ToString();
        }
        public int Calistir(string sql, params object[] p) { using var k = Kmt(Yer(sql), p); return k.ExecuteNonQuery(); }
        public long Ekle(string sql, params object[] p)
        {
            using var k = Kmt(Yer(sql), p); k.ExecuteNonQuery();
            using var k2 = Kmt("SELECT last_insert_rowid()", Array.Empty<object>());
            return (long)k2.ExecuteScalar();
        }
        public object Tek(string sql, params object[] p) { using var k = Kmt(Yer(sql), p); var v = k.ExecuteScalar(); return v is DBNull ? null : v; }
        public List<object[]> Satirlar(string sql, params object[] p)
        {
            using var k = Kmt(Yer(sql), p);
            using var r = k.ExecuteReader();
            var l = new List<object[]>();
            while (r.Read())
            {
                var a = new object[r.FieldCount];
                r.GetValues(a);
                for (int i = 0; i < a.Length; i++) if (a[i] is DBNull) a[i] = null;
                l.Add(a);
            }
            return l;
        }
        public void Basla() { Islem ??= C.BeginTransaction(); }
        public void Onayla() { Islem?.Commit(); Islem?.Dispose(); Islem = null; }
        public void Dispose() { try { Islem?.Rollback(); } catch { } Islem?.Dispose(); C.Dispose(); }
    }

    public static Db DbKopyala(string kaynak, string hedef)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(hedef)));
        foreach (var ek in new[] { "-wal", "-shm" }) if (File.Exists(hedef + ek)) File.Delete(hedef + ek);
        File.Copy(kaynak, hedef, true);
        foreach (var ek in new[] { "-wal", "-shm" }) if (File.Exists(kaynak + ek)) File.Copy(kaynak + ek, hedef + ek, true);
        var c = new Db(hedef);
        c.Calistir("PRAGMA wal_checkpoint(TRUNCATE)");
        c.Calistir("PRAGMA journal_mode=DELETE");
        if (c.Tek("PRAGMA integrity_check") as string != "ok")
        {
            c.Dispose();
            throw new HataMesaji("Pro veritabanı bozuk görünüyor (integrity_check).");
        }
        return c;
    }

    public static string FirmaAdi(string dbYolu)
    {
        try
        {
            using var c = new Db(dbYolu, salt: true);
            foreach (var k in new[] { "sm_firma_adi", "panel_firma_adi", "isletme_adi" })
            {
                var r = c.Tek("SELECT value FROM settings WHERE key=?", k);
                if (r != null && Py.Strip(Convert.ToString(r, Py.CI)).Length > 0) return Metin.GuvenliAd(Py.Strip(Convert.ToString(r, Py.CI)));
            }
        }
        catch { }
        return "";
    }

    static HashSet<string> Tablolar(Db c) =>
        c.Satirlar("SELECT name FROM sqlite_master WHERE type='table'").Select(r => (string)r[0]).ToHashSet();

    /// <summary>Kurulumla gelen örnek verileri siler (yönetici hesabına dokunmaz).</summary>
    public static List<(string tablo, int sayi)> DemoTemizle(Db c, Action<string> log)
    {
        var t = Tablolar(c);
        var sayac = new List<(string, int)>();
        var r = c.Satirlar("SELECT created_at FROM users ORDER BY id LIMIT 1");
        if (r.Count == 0 || r[0][0] == null || Convert.ToString(r[0][0], Py.CI) == "")
        {
            log("Demo temizliği: tohum zamanı bulunamadı, atlandı.");
            return sayac;
        }
        var tohum = r[0][0];
        void Sil(string tablo, string kosul, params object[] p)
        {
            if (!t.Contains(tablo)) return;
            int n = c.Calistir($"DELETE FROM {tablo} WHERE {kosul}", p);
            if (n == 0) return;
            int i = sayac.FindIndex(x => x.Item1 == tablo);
            if (i >= 0) sayac[i] = (tablo, sayac[i].Item2 + n); else sayac.Add((tablo, n));
        }
        var demoSip = c.Satirlar("SELECT id FROM orders WHERE created_at < ?", tohum).Select(x => x[0]).ToArray();
        if (demoSip.Length > 0)
        {
            var yer = string.Join(",", demoSip.Select(_ => "?"));
            foreach (var tablo in t.OrderBy(x => x, StringComparer.Ordinal))
            {
                if (tablo == "orders") continue;
                var sutun = c.Satirlar($"PRAGMA table_info({tablo})").Select(x => (string)x[1]).ToList();
                if (sutun.Contains("order_id")) Sil(tablo, $"order_id IN ({yer})", demoSip);
            }
            Sil("orders", $"id IN ({yer})", demoSip);
        }
        Sil("shifts", "baslangic < ?", tohum);
        Sil("sms_history", "created_at < ?", tohum);
        var demoUye = c.Satirlar("SELECT id FROM members WHERE created_at = ?", tohum).Select(x => x[0]).ToArray();
        if (demoUye.Length > 0)
        {
            var yer = string.Join(",", demoUye.Select(_ => "?"));
            Sil("member_adresleri", $"member_id IN ({yer})", demoUye);
            Sil("members", $"id IN ({yer})", demoUye);
        }
        Sil("firmalar", "created_at = ?", tohum);
        var ilk = c.Tek("SELECT id FROM users ORDER BY id LIMIT 1");
        Sil("users", "created_at = ? AND id <> ? AND role <> 'Yönetici'", tohum, ilk);
        if (t.Contains("printers"))
        {
            if (t.Contains("categories")) c.Calistir("UPDATE categories SET printer_id=NULL, printer_ids=''");
            Sil("yazici_eslesme", "1=1");
            Sil("printers", "1=1");
        }
        log("Demo temizliği: " + (sayac.Count > 0 ? string.Join(", ", sayac.Select(x => $"{x.Item1} {x.Item2}")) : "silinecek kayıt yok")
            + "  (yönetici hesabına dokunulmadı)");
        return sayac;
    }

    static void MenuyuBosalt(Db c, Action<string> log)
    {
        var simdi = Simdi();
        var t = Tablolar(c);
        int nU = 0, nK = 0;
        foreach (var (tur, tablo) in new[] { ("urun", "products"), ("kategori", "categories") })
        {
            foreach (var r in c.Satirlar($"SELECT id, COALESCE(remote_id,0) FROM {tablo}"))
                if (t.Contains("silinen_kayitlar"))
                    c.Calistir("INSERT INTO silinen_kayitlar (tur, kayit_id, remote_id, silindi_at, gonderildi_at) VALUES (?,?,?,?, '')",
                        tur, r[0], r[1], simdi);
            int n = c.Calistir($"DELETE FROM {tablo}");
            if (tur == "urun") nU = n; else nK = n;
        }
        foreach (var tablo in new[] { "urun_detaylar", "kategori_detaylar" })
            if (t.Contains(tablo)) c.Calistir($"DELETE FROM {tablo}");
        if (t.Contains("yazici_eslesme")) c.Calistir("DELETE FROM yazici_eslesme WHERE hedef_tip='kategori'");
        if (nU > 0 || nK > 0) log($"Eski menü silindi: {nK} kategori, {nU} ürün (panele silme olarak gönderilecek).");
    }

    static long DetayId(Db c, string ad, string simdi)
    {
        ad = Metin.TrBuyuk(Py.Strip(ad));
        if (ad.Length > 40) ad = ad.Substring(0, 40);
        var r = c.Tek("SELECT id FROM detaylar WHERE lower(name)=lower(?)", ad);
        if (r != null) return Convert.ToInt64(r);
        var sira = Convert.ToInt64(c.Tek("SELECT COALESCE(MAX(sira),0) FROM detaylar") ?? 0L) + 1;
        return c.Ekle("INSERT INTO detaylar (name, sira, updated_at, remote_id) VALUES (?,?,?,0)", ad, sira, simdi);
    }

    /// <summary>Python json.dumps(list, ensure_ascii=False)</summary>
    static string VaryantJson(List<(string name, double price)> v)
    {
        var sb = new StringBuilder("[");
        for (int i = 0; i < v.Count; i++)
        {
            if (i > 0) sb.Append(", ");
            sb.Append("{\"name\": ").Append(JsonMetin(v[i].name)).Append(", \"price\": ").Append(Py.FloatRepr(v[i].price)).Append('}');
        }
        return sb.Append(']').ToString();
    }

    static string JsonMetin(string s)
    {
        var sb = new StringBuilder("\"");
        foreach (var c in s)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                case '\b': sb.Append("\\b"); break;
                case '\f': sb.Append("\\f"); break;
                default:
                    if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4")); else sb.Append(c);
                    break;
            }
        }
        return sb.Append('"').ToString();
    }

    public sealed record Sonuc(int Kategori, int Urun, List<string> Eksik, string DbYolu, string ResimKlasoru);

    public static Sonuc DbOlustur(string sablonDb, List<Kategori> kategoriler, string resimKok, string ciktiKlasor,
        double fiyat = 1.0, double kdv = 10, int kategoriSeviye = 1, Action<string> log = null,
        Dictionary<string, string> gercekKlasorler = null, IList<string> ekOdemeler = null, string kullanici = "",
        string opsiyonlar = "", bool icecegeOpsiyon = false, bool demoTemizle = true, CancellationToken iptal = default)
    {
        log ??= _ => { };
        ekOdemeler ??= Array.Empty<string>();
        if (fiyat <= 0) throw new HataMesaji("Fiyat 0 veya eksi olamaz (yazar kasa kabul etmez).");
        kullanici = Py.Strip(kullanici ?? "");
        if (kullanici.Length == 0) throw new HataMesaji("Kurulacak bilgisayarın Windows kullanıcı adı boş olamaz (resim yolları için).");
        var hedefDb = Path.Combine(ciktiKlasor, "siparismatik.db");
        var rKlasor = Path.Combine(ciktiKlasor, "product-images");
        if (Directory.Exists(rKlasor)) Directory.Delete(rKlasor, true);
        Directory.CreateDirectory(rKlasor);
        var veriKlasoru = VeriKlasoru.Replace("{kullanici}", kullanici);
        int nKat, nUrun = 0, fiyatli = 0, nVar = 0, nBag = 0, nDetay;
        var eksik = new List<string>();
        using (var c = DbKopyala(sablonDb, hedefDb))
        {
            var t = Tablolar(c);
            foreach (var gerekli in new[] { "categories", "products", "detaylar", "urun_detaylar", "settings" })
                if (!t.Contains(gerekli)) throw new HataMesaji($"Seçilen dosya SiparişMatik Pro veritabanı değil ({gerekli} tablosu yok).");
            c.Basla();
            if (demoTemizle) DemoTemizle(c, log);
            MenuyuBosalt(c, log);
            var simdi = Simdi();
            var winKlasor = veriKlasoru + "\\product-images\\";
            long damga = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            string ResimKopyala(string kaynak, string ad)
            {
                if (string.IsNullOrEmpty(kaynak)) return "";
                var dosya = $"prod_{damga}_{ad}.jpg";
                // küçük resim (200 px altı) de kullanılır: büyütülüp 600×600 yapılır
                if (!Resim.KareJpegKaydet(File.ReadAllBytes(kaynak), Path.Combine(rKlasor, dosya), kucukDeOlur: true)) return "";
                return winKlasor + dosya;
            }

            var (katSira, katUrunler) = Sql.KategorileriTopla(kategoriler, resimKok, kategoriSeviye, gercekKlasorler);
            nKat = katSira.Count;
            var detayIdler = Regex.Split(opsiyonlar ?? "", "[,;\n]").Where(o => Py.Strip(o).Length > 0)
                .Select(o => DetayId(c, o, simdi)).ToList();
            nDetay = detayIdler.Count;
            for (int ks = 1; ks <= katSira.Count; ks++)
            {
                iptal.ThrowIfCancellationRequested();
                var katAd = katSira[ks - 1];
                string ilk = null;
                foreach (var u in katUrunler[katAd]) { ilk = Sql.ResimBul(u.Klasor, u.ResimAdi); if (ilk != null) break; }
                var cid = c.Ekle("INSERT INTO categories (name, image, kdv, sort_order, remote_id, kiosk_visible, kiosk_sort, updated_at) " +
                                 "VALUES (?,?,?,?,0,1,?,?)",
                    Metin.TrBuyuk(katAd), ResimKopyala(ilk, $"k{ks}"), (long)Math.Round(kdv, MidpointRounding.ToEven), (long)ks, (long)ks, simdi);
                bool icecek = Py.Upper(Metin.Norm(katAd)).Replace(" ", "").Contains("ICECEK");
                int us = 0;
                foreach (var u in katUrunler[katAd])
                {
                    us++; nUrun++;
                    double uFiyat;
                    if (u.Fiyat.HasValue && u.Fiyat.Value > 0) { fiyatli++; uFiyat = u.Fiyat.Value; } else uFiyat = fiyat;
                    var r = Sql.ResimBul(u.Klasor, u.ResimAdi);
                    if (r == null) eksik.Add(u.Ad);
                    var varyant = new List<(string, double)>();
                    foreach (var p in u.Porsiyonlar)
                    {
                        var pf = p.Fiyat.HasValue && p.Fiyat.Value > 0 ? p.Fiyat.Value : uFiyat;
                        var ad = Metin.TrBuyuk(p.Ad);
                        varyant.Add((ad.Length > 40 ? ad.Substring(0, 40) : ad, Math.Round(pf - uFiyat, 2, MidpointRounding.ToEven)));
                    }
                    nVar += varyant.Count;
                    var pid = c.Ekle("INSERT INTO products (category_id, name, price, kdv, active, wp_show, image, sort_order, " +
                                     "variants, remote_id, kiosk_visible, salon_show, paket_show, updated_at) " +
                                     "VALUES (?,?,?,?,1,1,?,?,?,0,1,1,1,?)",
                        cid, Metin.TrBuyuk(u.Ad), uFiyat, (long)Math.Round(kdv, MidpointRounding.ToEven), ResimKopyala(r, $"u{nUrun}"), (long)us,
                        varyant.Count > 0 ? VaryantJson(varyant) : "", simdi);
                    if (detayIdler.Count > 0 && (icecegeOpsiyon || !icecek))
                        foreach (var d in detayIdler)
                        {
                            c.Calistir("INSERT OR IGNORE INTO urun_detaylar (product_id, detay_id) VALUES (?,?)", pid, d);
                            nBag++;
                        }
                }
            }
            if (t.Contains("payment_methods"))
            {
                c.Calistir("UPDATE payment_methods SET active=1 WHERE name IN ('Nakit','Kredi Kartı')");
                if (ekOdemeler.Any(k => k != "NAKIT" && k != "KREDIKARTI"))
                    c.Calistir("UPDATE payment_methods SET active=1 WHERE name LIKE 'Yemek Kart%'");
            }
            c.Onayla();
            if (c.Tek("PRAGMA integrity_check") as string != "ok")
                throw new HataMesaji("Yazılan veritabanı integrity_check'ten geçmedi.");
        }
        foreach (var ek in new[] { "-wal", "-shm" }) if (File.Exists(hedefDb + ek)) File.Delete(hedefDb + ek);
        log($"Pro veritabanı yazıldı: {hedefDb}  ({nKat} kategori, {nUrun} ürün, {nVar} porsiyon, {nDetay} opsiyon / {nBag} bağ, " +
            $"{fiyatli} ürün menü fiyatıyla, {nUrun - fiyatli} ürün {Metin.Sayi(fiyat)} TL)");
        log($"Resimler: {rKlasor}  → müşteride {veriKlasoru}\\product-images");
        log("KURULUM: Pro'yu kapat → AppData\\Roaming\\siparismatik-pro içindeki siparismatik.db, " +
            "siparismatik.db-wal ve siparismatik.db-shm'yi yedeğe al → bu siparismatik.db'yi ve " +
            "product-images klasörünü oraya kopyala → Pro'yu aç.");
        if (eksik.Count > 0) log($"Resmi olmayan {eksik.Count} ürün: " + string.Join(", ", eksik.Take(15)) + (eksik.Count > 15 ? " ..." : ""));
        return new Sonuc(nKat, nUrun, eksik, hedefDb, rKlasor);
    }

    /// <summary>Kurulu bir Pro veritabanında yerinde demo temizliği (önce yedek alır).</summary>
    public static string DemoTemizleDosya(string dbYolu, Action<string> log)
    {
        if (File.Exists(dbYolu + "-wal") && new FileInfo(dbYolu + "-wal").Length > 0)
            log("Uyarı: yanında dolu -wal dosyası var; Pro açık olabilir. Önce Pro'yu kapat.");
        var yedek = dbYolu + DateTime.Now.ToString("'.yedek_'yyyyMMdd'_'HHmmss", Py.CI);
        File.Copy(dbYolu, yedek, true);
        if (File.Exists(dbYolu + "-wal")) File.Copy(dbYolu + "-wal", yedek + "-wal", true);
        using (var c = new Db(dbYolu))
        {
            c.Calistir("PRAGMA busy_timeout=3000");
            c.Basla();
            DemoTemizle(c, log);
            c.Onayla();
        }
        log($"Yedek: {yedek}");
        return yedek;
    }
}
