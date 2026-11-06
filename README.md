# Ürün Resim İndirici

Ürün listesi ya da menü resminden ürün fotoğraflarını **600×600 JPEG** olarak indirir,
kategorilere ayırır ve POS programının SQL yedeğini (kategoriler, ürünler, porsiyonlar,
resimler, ödeme tipleri) doğru Türkçe karakterlerle otomatik doldurur.

## İndir
**[Releases](../../releases/latest)** → `UrunResimIndirici.exe` dosyasını indir,
bir klasöre koy ve çalıştır. Python gerekmez.

## Güncelleme
Program açılışta yeni sürümü kendisi kontrol eder. **Güncelle** butonu ya da
`GUNCELLE.bat` en yeni exe'yi indirir; ayarlar, resimler ve firma dosyaları korunur.

## Yeni sürüm yayınlamak
1. `UrunResimIndirici.py` içindeki `SURUM = "..."` satırını artır.
2. Dosyayı bu depoya yükle.
3. GitHub Actions exe'yi derler ve **Releases**'e ekler (~3-4 dk).

Ayrıntılar: [OKU_BENI.txt](OKU_BENI.txt)
