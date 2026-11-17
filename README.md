# Ürün Resim İndirici

Müşterinin menüsünden (fotoğraf, broşür, PDF, yazılı liste ya da Excel) kategorili ürün listesi çıkarır,
her ürün için **600×600 JPEG** resim bulur ve POS programının veritabanını (kategoriler, ürünler,
porsiyonlar, resimler, ödeme tipleri) doğru Türkçe karakterlerle doldurur.

## İndir ve kur
**[Releases](../../releases/latest)** → `UrunResimIndirici-Kurulum-x.y.z.exe` dosyasını indir ve çalıştır.
Kurulum yönetici izni istemez; masaüstüne ve Başlat menüsüne kısayol koyar.

> Windows "bilinmeyen yayımcı" uyarısı verirse **Ek bilgi → Yine de çalıştır**.

Eski (3.x) sürümden geçiyorsan programdaki **Güncelle** butonu kurulumu kendisi indirir.
Yeni sürüm ilk açılışta eski ayarları (API anahtarı, SQL dosyası, resim klasörü) otomatik bulur;
bulamazsa **Ayarlar → Eski sürümün ayarlarını içe aktar** ile `ayarlar.json` dosyasını göster.

## Kullanım
1. **Kaynak** — menü resimlerini sürükle ya da yazılı listeyi yapıştır. İkisi birden varsa broşürdeki
   ürün fotoğrafları kesilir, fiyatlar yazılı listeden alınır. Excel ve hazır resim klasörü/zip de olur.
2. **Ürün listesi** — sağda kategoriler, fiyatlar ve porsiyonlar (Az / Tam / 1,5 / Duble…) canlı görünür;
   soldaki metinden düzelt.
3. **Resimler** — tek tuşla indirir. Beğenmediğin resimde **Farklı resim** ya da **Dosya seç**;
   dosyayı karta sürükleyip bırakmak da olur.
4. **POS çıktısı** — `.sql` yedeği ya da Pro veritabanı, `Firmalar\<firma>` klasörüne yazılır.

Menü resmi okumak için ücretsiz bir Gemini anahtarı gerekir
([aistudio.google.com/apikey](https://aistudio.google.com/apikey), kart istemez). Yazılı liste,
Excel ve resim indirme anahtarsız çalışır. İnternetsiz okumak istersen Ollama seçilebilir.

## Güncelleme
Program açılışta yeni sürümü kendisi indirir; hazır olunca üstte **Yeniden başlat ve kur** çıkar.
Ayarlar, resim kütüphanesi ve firma dosyaları korunur.

## Geliştirme
| Klasör | İçerik |
|---|---|
| `motor/` | C# (.NET 8) motor: liste ayrıştırma, resim arama/indirme, SQL ve SQLite yazıcı, Excel |
| `motor.tests/` | Motorun testleri (eski Python sürümünün çıktılarıyla birebir karşılaştırma) |
| `arayuz/` | Electron arayüz; motorla satır satır JSON konuşur |

Yeni sürüm: `arayuz/package.json` içindeki `version` değerini artırıp `main` dalına gönder.
GitHub Actions testleri çalıştırır, kurulum dosyasını derler ve **Releases**'e ekler (~10 dk).
