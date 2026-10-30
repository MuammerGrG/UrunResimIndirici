# Ürün Resim İndirici + Siparişmatik SQL

Ürün listesi ya da menü resminden ürün fotoğraflarını **600×600 JPEG** olarak indirir,
kategorilere ayırır ve `db_siparismatik.sql` dosyasını (ürünler, kategoriler, porsiyonlar,
resimler, doğru Türkçe karakterlerle) otomatik doldurur.

## İndir
**[Releases](../../releases/latest)** → `UrunResimIndirici.exe` dosyasını indir,
bir klasöre (ör. `C:\UrunResimIndirici`) koy ve çalıştır. Python gerekmez.

## Güncelleme
Program açılışta yeni sürümü kendisi kontrol eder; **Güncelle** butonu ya da
`GUNCELLE.bat` ile en yeni exe indirilir. Ayarlar, resimler ve firma SQL'leri korunur.

## Yeni sürüm yayınlamak
1. `UrunResimIndirici.py` içindeki `SURUM = "..."` satırını artır (ör. 1.8 → 1.9).
2. Dosyayı bu depoya yükle (Add file → Upload files).
3. GitHub Actions exe'yi derler ve **Releases**'e yeni sürüm olarak ekler (~3-4 dk).

Ayrıntılar: [OKU_BENI.txt](OKU_BENI.txt)
