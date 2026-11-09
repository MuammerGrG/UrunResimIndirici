# -*- coding: utf-8 -*-
"""
Ürün Resim İndirici + POS SQL (tek dosya)
--------------------------------------------------
1) Yeni müşterinin menü resimlerini / PDF'ini okur (ücretsiz Gemini API veya yerel Ollama ile),
   ürünleri Kategori / Pişme Şekli / Menü-Sadece Yemek olarak ayırır.
2) Her ürün için internetten resim bulur, 600x600 JPEG yapar ve
   Urun_Resimleri/<Müşteri>/<Kategori>/<Pişme Şekli>/<Menü|Sadece Yemek>/ altına kaydeder.

Kullanım:
  Pencereli:     python urun_resim_indirici.py
  Komut satırı:  python urun_resim_indirici.py liste.txt --musteri "Burgerci Ali"

Liste formatı ('#' sayısı = klasör seviyesi):
    # Döner
    ## Izgara
    ### Menü
    Tavuk Dürüm Menü | tavuk döner dürüm menü patates ayran
    ### Sadece Yemek
    Tavuk Dürüm
'|' sağı yazılırsa arama o kelimelerle yapılır, dosya adı solundaki addır.
"""

import argparse
import base64
import io
import json
import os
import re
import sys
import threading
import time

import requests
from PIL import Image

try:
    from ddgs import DDGS
except ImportError:
    try:
        from duckduckgo_search import DDGS
    except ImportError:
        print("Eksik kütüphane. Şunu çalıştır:  pip install ddgs pillow requests")
        sys.exit(1)

BOYUT = 600
# exe olarak çalışınca ayarlar exe'nin yanında tutulur
KLASOR = (os.path.dirname(sys.executable) if getattr(sys, "frozen", False)
          else os.path.dirname(os.path.abspath(__file__)))
AYAR_DOSYASI = os.path.join(KLASOR, "ayarlar.json")
SURUM = "2.5"
GITHUB_REPO = "MuammerGrG/UrunResimIndirici"     # ayarlar.json > "github_repo" ile değiştirilebilir
GEMINI_MODELLER = ["gemini-2.5-flash", "gemini-flash-latest", "gemini-2.5-flash-lite"]   # yedek; asıl liste Google'dan çekilir
_GEMINI_CACHE = {}
OLLAMA_MODEL = "qwen2.5vl:7b"
OLLAMA_URL = "http://localhost:11434"
# Kontrol sayfası geçici klasöre yazılır, firma/resim klasörlerine dosya bırakmaz
import tempfile as _tempfile
KONTROL_SAYFASI = os.path.join(_tempfile.gettempdir(), "UrunResimIndirici_kontrol.html")
RESIM_UZANTI = (".jpg", ".jpeg", ".png", ".webp", ".gif", ".bmp")
HEADERS = {
    "User-Agent": "Mozilla/5.0 (Windows NT 10.0; Win64; x64) "
                  "AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0 Safari/537.36"
}

MENU_PROMPT = """Bu bir restoran menüsü. Menüdeki TÜM satılan ürünleri çıkar ve SADECE aşağıdaki formatta düz metin yaz, başka hiçbir açıklama yazma:

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
"""


LISTE_PROMPT = """Aşağıda bir işletmenin düz ürün listesi var. Bu ürünleri gruplayıp SADECE aşağıdaki formatta düz metin yaz, başka açıklama yazma:

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
"""


# ---------------------------------------------------------------- ayarlar

def _surum_tuple(metin):
    return tuple(int(x) for x in re.findall(r"\d+", metin or "")[:3]) or (0,)


def github_son_surum(repo, token=""):
    """GitHub Releases'teki son sürüm: (sürüm, exe_indirme_adresi, notlar) ya da hata fırlatır."""
    basliklar = {"Accept": "application/vnd.github+json", "User-Agent": "UrunResimIndirici"}
    if token:
        basliklar["Authorization"] = f"Bearer {token}"
    r = requests.get(f"https://api.github.com/repos/{repo}/releases/latest", headers=basliklar, timeout=15)
    if r.status_code == 404:
        raise RuntimeError(f"GitHub'da '{repo}' için yayınlanmış sürüm yok (depo gizli ya da henüz derlenmedi).")
    r.raise_for_status()
    veri = r.json()
    exe = next((a for a in veri.get("assets", []) if a.get("name", "").lower().endswith(".exe")), None)
    if not exe:
        raise RuntimeError("Son sürümde exe dosyası yok (GitHub Actions derlemesi bitmemiş olabilir).")
    adres = exe["url"] if token else exe["browser_download_url"]
    notlar = "\n".join(x for x in (veri.get("body") or "").splitlines()
                       if not re.match(r"\s*(co-authored-by|claude-session|signed-off-by)\s*:", x, re.I)
                       and "claude.ai/code" not in x)
    return veri.get("tag_name", "").lstrip("vV"), adres, notlar.strip()


def github_exe_indir(adres, hedef, token="", ilerleme=None):
    basliklar = {"User-Agent": "UrunResimIndirici", "Accept": "application/octet-stream"}
    if token:
        basliklar["Authorization"] = f"Bearer {token}"
    with requests.get(adres, headers=basliklar, stream=True, timeout=(15, 120)) as r:
        r.raise_for_status()
        toplam, inen = int(r.headers.get("content-length") or 0), 0
        with open(hedef + ".part", "wb") as f:
            for parca in r.iter_content(256 * 1024):
                f.write(parca)
                inen += len(parca)
                if ilerleme and toplam:
                    ilerleme(inen * 100 // toplam)
    if os.path.getsize(hedef + ".part") < 1_000_000:
        os.remove(hedef + ".part")
        raise RuntimeError("İnen dosya eksik/bozuk görünüyor.")
    return _guvenli_tasi(hedef + ".part", hedef)


def _guvenli_tasi(kaynak, hedef, deneme=30):
    """Antivirüs yeni exe'yi tararken kısa süre kilitler (WinError 5/32): bekleyip tekrar dener.
    Yine olmazsa farklı bir adla bırakır. Gerçekte kullanılan yolu döndürür."""
    for i in range(deneme):
        try:
            if os.path.exists(hedef):
                os.remove(hedef)                 # önceki yarım kalmış güncelleme
            os.replace(kaynak, hedef)
            return hedef
        except PermissionError:
            time.sleep(1)
    yedek_ad = os.path.splitext(hedef)[0] + time.strftime("_%H%M%S") + ".exe"
    for i in range(deneme):
        try:
            os.replace(kaynak, yedek_ad)
            return yedek_ad
        except PermissionError:
            time.sleep(1)
    raise RuntimeError("Antivirüs dosyayı bırakmadı. Birkaç dakika sonra tekrar dene ya da "
                       "exe'yi GitHub Releases sayfasından elle indir.")


def _oem_kodlama():
    """cmd.exe .cmd dosyasını OEM kod sayfasıyla okur (Türkçe Windows: cp857), ANSI (cp1254) değil."""
    try:
        import ctypes
        return f"cp{ctypes.windll.kernel32.GetOEMCP()}"
    except Exception:
        return "cp857"


def exe_degistir_ve_yeniden_ac(yeni_exe, eski_surum):
    """Çalışan exe kapanınca yenisini yerine koyan küçük bir .cmd başlatır."""
    import subprocess
    mevcut = sys.executable
    klasor = os.path.dirname(mevcut)
    yedek = os.path.join(klasor, "eski_surum")
    os.makedirs(yedek, exist_ok=True)
    cmd = os.path.join(klasor, "_guncelle_tmp.cmd")
    yedek_exe = os.path.join(yedek, f"UrunResimIndirici_v{eski_surum}.exe")
    with open(cmd, "w", encoding=_oem_kodlama(), errors="replace", newline="\r\n") as f:
        f.write(f"""@echo off
rem Program kapanana ve antivirus taramasi bitene kadar bekle, sonra yenisini koy
timeout /t 2 /nobreak >nul
copy /y "{mevcut}" "{yedek_exe}" >nul 2>&1
set n=0
:tasi
move /y "{yeni_exe}" "{mevcut}" >nul 2>&1 && goto ac
set /a n+=1
if %n% geq 60 goto ac
timeout /t 1 /nobreak >nul
goto tasi
:ac
start "" "{mevcut}"
del "%~f0"
""")
    subprocess.Popen(["cmd", "/c", cmd], cwd=klasor, creationflags=0x08000000)   # pencere açmadan


def ayar_oku():
    try:
        with open(AYAR_DOSYASI, encoding="utf-8") as f:
            return json.load(f)
    except Exception:
        return {}


def ayar_yaz(ayar):
    try:
        with open(AYAR_DOSYASI, "w", encoding="utf-8") as f:
            json.dump(ayar, f, ensure_ascii=False, indent=2)
    except Exception:
        pass


# ---------------------------------------------------------------- liste

def guvenli_ad(metin: str) -> str:
    metin = re.sub(r'[\\/:*?"<>|]+', "", metin).strip().rstrip(".")
    return re.sub(r"\s+", " ", metin)


_FIYAT_SONDA = re.compile(
    r"^(.*?)(?:\s*[-–—:=…]+\s*|\s+)(?:₺\s*)?(\d{1,3}(?:[.\s]\d{3})*(?:,\d{1,2})?|\d+(?:[.,]\d{1,2})?)"
    r"\s*(tl|₺|try)\.?$", re.I)
_FIYAT_AYRAC = re.compile(
    r"^(.*?)\s*(?:[-–—:=]|\.{2,}|…)+\s*(?:₺\s*)?(\d+(?:[.,]\d{1,2})?)\s*(?:tl|₺)?\.?$", re.I)


def fiyat_coz(metin):
    """'250', '87,50', '1.250,00', '₺ 99' → float; olmazsa None"""
    if metin is None:
        return None
    t = metin.strip().lower().replace("₺", "").replace("tl", "").replace(" ", "")
    if not t:
        return None
    if "," in t and "." in t:              # 1.250,00
        t = t.replace(".", "").replace(",", ".")
    elif "," in t:
        t = t.replace(",", ".")
    elif re.fullmatch(r"\d{1,3}(\.\d{3})+", t):   # 1.250
        t = t.replace(".", "")
    try:
        v = float(t)
        return v if v > 0 else None
    except ValueError:
        return None


# ---------------------------------------------------------------- porsiyonlar
# Ürün adının SONUNDA geçen porsiyon ifadeleri (ör. "Adana Dürüm Az", "Tavuk Döner (1,5)")
_PORS_KELIME = (r"az|yar[iı]m|tam|tek|normal|standart|orta|k[uü][cç][uü]k|b[uü]y[uü]k|mini|midi|maxi|"
                r"mega|duble|double|[cç]ift|bir\s*bu[cç]uk|1\s*[.,]\s*5|0\s*[.,]\s*5|1½")
_PORS_SON = re.compile(
    r"^(?P<kok>.*?)[\s\-–/,(]+(?P<etiket>(?:%s)(?:\s*(?:porsiyon|pors\.?|p\.?|ki[sş]ilik))?|"
    r"[1-3]\s*(?:porsiyon|pors\.?|ki[sş]ilik)|porsiyon)\)?\s*$" % _PORS_KELIME, re.I)
_PORS_NORMAL = ["tam", "normal", "tek", "standart", "1 porsiyon", "porsiyon", "orta"]


def _pors_norm(metin):
    return re.sub(r"\s+", " ", metin.translate(_TR).lower()).strip()


def porsiyon_etiketi(metin):
    """'1.5 porsiyon' → '1,5 Porsiyon', 'az' → 'Az' (POS'taki gibi virgüllü)"""
    m = re.sub(r"\s+", " ", metin).strip()
    kisa = re.sub(r"\s*(porsiyon|pors\.?|p\.)$", "", m, flags=re.I).strip()
    if kisa and not re.fullmatch(r"\d+", kisa):          # "Duble Porsiyon" → "Duble", "1,5 Pors" → "1,5"
        m = kisa
    m = re.sub(r"(\d)\s*[.]\s*(\d)", r"\1,\2", m)
    m = re.sub(r"(\d)\s*,\s*(\d)", r"\1,\2", m)
    return m[:1].upper() + m[1:] if m else m


def porsiyon_ayir(ad):
    """'Adana Dürüm Az' → ('Adana Dürüm', 'Az', False); porsiyon yoksa None"""
    m = _PORS_SON.match(ad.strip())
    if not m or not m.group("kok").strip():
        return None
    etiket = m.group("etiket").strip()
    return m.group("kok").strip(" -–/,("), porsiyon_etiketi(etiket), _pors_norm(etiket) == "porsiyon"


def porsiyonlari_coz(metin):
    """'Az=100; 1,5=200' / 'Az:100, Tam:150' → [('Az', 100.0), ('1,5', 200.0)]"""
    sonuc = []
    for parca in re.split(r"[;/]|,(?!\d)", metin or ""):
        m = re.match(r"\s*(.+?)\s*[=:]\s*(?:₺\s*)?([\d.,]+)\s*(?:tl|₺)?\s*$", parca, re.I)
        if m:
            f = fiyat_coz(m.group(2).rstrip(",."))
            if f:
                sonuc.append((porsiyon_etiketi(m.group(1)), f))
    return sonuc


def porsiyonlari_yaz(pors):
    return "; ".join(f"{e}={'%g' % f}" for e, f in pors)


def porsiyon_grupla(urunler):
    """
    Aynı kategorideki 'Adana Az 100', 'Adana Tam 150', 'Adana 1,5 200' satırlarını
    TEK ürün + porsiyonlar yapar: ('Adana', arama, 150, [('Az',100), ('1,5',200)]).
    Ana fiyat: porsiyonsuz yazılmış hali, yoksa Tam/Normal/1 Porsiyon, yoksa en ucuzu.
    urunler: [(ad, arama_veya_dosya, fiyat, porsiyonlar), ...]
    """
    bilgi = []
    for u in urunler:
        ay = porsiyon_ayir(u[0]) if not u[3] else None
        bilgi.append(ay)
    gruplar = {}
    for i, (u, ay) in enumerate(zip(urunler, bilgi)):
        if ay:
            gruplar.setdefault(_pors_norm(ay[0]), []).append(i)
    for i, u in enumerate(urunler):                      # porsiyonsuz hali de gruba katılır
        if not bilgi[i] and not u[3] and _pors_norm(u[0]) in gruplar:
            gruplar[_pors_norm(u[0])].insert(0, i)
    birlesen, sonuc_yeri = set(), {}
    for kok, idx in gruplar.items():
        if len(idx) < 2:
            continue
        uyeler = [(i, urunler[i], bilgi[i]) for i in idx]
        ana = next((x for x in uyeler if x[2] is None), None)
        if ana is None:
            for tercih in _PORS_NORMAL:
                ana = next((x for x in uyeler if _pors_norm(x[2][1]) == tercih), None)
                if ana:
                    break
        if ana is None:
            fiyatli = [x for x in uyeler if x[1][2]]
            ana = min(fiyatli, key=lambda x: x[1][2]) if fiyatli else uyeler[0]
        i0, u0, ay0 = ana
        if ay0 is None or ay0[2]:                        # porsiyonsuz ya da "... Porsiyon"
            urun_adi = u0[0]
        else:
            urun_adi = ay0[0]
        arama = u0[1] if _pors_norm(u0[1]) != _pors_norm(u0[0]) else urun_adi
        pors, gorulen = [], set()
        for i, u, ay in uyeler:
            if i == i0 or ay is None:
                continue
            e = ay[1]
            if _pors_norm(e) in gorulen:
                continue
            gorulen.add(_pors_norm(e))
            pors.append((e, u[2] or u0[2]))
        birlesen.update(idx)
        sonuc_yeri[min(idx)] = (urun_adi, arama, u0[2], pors)
    sonuc = []
    for i, u in enumerate(urunler):
        if i in sonuc_yeri:
            sonuc.append(sonuc_yeri[i])
        elif i not in birlesen:
            sonuc.append(u)
    return sonuc


def satiri_coz(s):
    """'Ad | arama | fiyat | Az=100; 1,5=200', 'Ad - 250 TL', 'Ad 250₺'
    → (ad, arama, fiyat, porsiyonlar)"""
    parcalar = [x.strip() for x in s.split("|")]
    ad, arama, fiyat, pors = parcalar[0], "", None, []
    if len(parcalar) >= 4:
        pors = porsiyonlari_coz(parcalar[3])
    if len(parcalar) >= 3:
        arama, fiyat = parcalar[1], fiyat_coz(parcalar[2])
    elif len(parcalar) == 2:
        if fiyat_coz(parcalar[1]) is not None and re.fullmatch(r"[₺\d.,\s]+(tl)?", parcalar[1], re.I):
            fiyat = fiyat_coz(parcalar[1])
        else:
            arama = parcalar[1]
    if fiyat is None:
        m = _FIYAT_SONDA.match(ad) or _FIYAT_AYRAC.match(ad)
        if m and m.group(1).strip():
            ad, fiyat = m.group(1).strip(), fiyat_coz(m.group(2))
    return ad, arama, fiyat, pors


# ---------------------------------------------------------------- ödeme tipleri
_ODEME_SATIRI = re.compile(r"^\s*[-•*]?\s*(?:ödeme|odeme)(?:\s*(?:tipleri|yöntemleri|yontemleri|seçenekleri|secenekleri))?\s*[:=]",
                           re.I)
# anahtar kelime → POS ödeme kodu (tbl_odemetipi.odemekod)
ODEME_ESLEME = [
    ("pluxee", "SODEXO"), ("sodexo", "SODEXO"), ("multinet", "MULTINET"),
    ("edenred", "SMARTICKET"), ("ticket", "SMARTICKET"), ("setcard", "SETCARD"), ("set kart", "SETCARD"),
    ("metropol", "METROPOL"), ("paye", "PAYEKART"), ("tokenflex", "TOKENFLEX"),
    ("win win", "WINWIN"), ("winwin", "WINWIN"), ("yemekmatik", "YEMEKMATIK"), ("cio card", "CIOCARD"),
    ("ciocard", "CIOCARD"), ("en kart", "ENCARD"), ("encard", "ENCARD"), ("teb kart", "TEBCARD"),
    ("bistra", "BISTRACARD"), ("maxima", "MAXIMACARD"), ("advantage", "ADVANTAGE"), ("maximum", "MAXIMUM"),
    ("kredi kart", "KREDIKARTI"), ("banka kart", "KREDIKARTI"), ("nakit", "NAKIT"),
]
# Marka yazmadan sadece "yemek kartı" denmişse seçilecek yaygın kartlar
YEMEK_KARTI_GENEL = ["SODEXO", "MULTINET", "SMARTICKET", "SETCARD", "METROPOL"]


def odeme_satiri_mi(satir):
    return bool(_ODEME_SATIRI.match((satir or "").translate(_TR)))


def odeme_kodlari_bul(metin):
    """Listedeki 'ÖDEME: ...' satırlarından POS ödeme kodlarını çıkarır."""
    kodlar = []
    for satir in (metin or "").splitlines():
        if not odeme_satiri_mi(satir):
            continue
        n = " " + re.sub(r"[^a-z0-9]+", " ", _norm(re.split(r"[:=]", satir, 1)[-1])) + " "
        for anahtar, kod in ODEME_ESLEME:
            if f" {anahtar} " in n or (len(anahtar) > 5 and anahtar.replace(" ", "") in n.replace(" ", "")):
                if kod not in kodlar:
                    kodlar.append(kod)
        if (" yemek kart" in n or " yemek ceki" in n) and not any(k in kodlar for k in YEMEK_KARTI_GENEL):
            kodlar.extend(k for k in YEMEK_KARTI_GENEL if k not in kodlar)
    return kodlar


def listeyi_ayristir(metin: str) -> dict:
    """{"Kategori/Pişme/Menü": [(ad, arama, fiyat|None, [(porsiyon, fiyat), ...]), ...]}"""
    kategoriler, yigin = {}, []
    for satir in metin.splitlines():
        s = satir.strip().lstrip("-•* ").strip() if not satir.strip().startswith("#") else satir.strip()
        if not s or s.startswith("```") or odeme_satiri_mi(s):
            continue
        m = re.match(r"^(#+)\s*(.*)$", s)
        if m:
            seviye, ad = len(m.group(1)), guvenli_ad(m.group(2)) or "Genel"
            yigin = yigin[:seviye - 1]
            while len(yigin) < seviye - 1:
                yigin.append("Genel")
            yigin.append(ad)
            continue
        ad_kismi, arama, fiyat, pors = satiri_coz(s)
        ad = guvenli_ad(ad_kismi)
        arama = arama.strip() or ad
        if not ad:
            continue
        yol = os.path.join(*yigin) if yigin else "Genel"
        liste = kategoriler.setdefault(yol, [])
        if ad.lower() not in [u[0].lower() for u in liste]:
            liste.append((ad, arama, fiyat, pors))
    return {k: porsiyon_grupla(v) for k, v in kategoriler.items() if v}


# ---------------------------------------------------------------- menü okuma (Gemini / Ollama)

def _dosya_hazirla(yol, maks=1600):
    """(mime, base64) döner. Resimler küçültülüp JPEG yapılır, PDF olduğu gibi."""
    if yol.lower().endswith(".pdf"):
        with open(yol, "rb") as f:
            return "application/pdf", base64.b64encode(f.read()).decode()
    img = Image.open(yol).convert("RGB")
    img.thumbnail((maks, maks), Image.LANCZOS)
    b = io.BytesIO()
    img.save(b, "JPEG", quality=88)
    return "image/jpeg", base64.b64encode(b.getvalue()).decode()


def menu_dosyalarini_topla(yollar):
    dosyalar = []
    for y in yollar:
        if os.path.isdir(y):
            for kok, _, adlar in os.walk(y):
                for a in sorted(adlar):
                    if a.lower().endswith(RESIM_UZANTI + (".pdf",)):
                        dosyalar.append(os.path.join(kok, a))
        elif y.lower().endswith(RESIM_UZANTI + (".pdf",)):
            dosyalar.append(y)
    return dosyalar


def _gemini(grup, api_key, model_tercih, istem=MENU_PROMPT, log=print, json_mod=False, resim_boyu=1600):
    """Gemini'ye istek atar. Her adımı log'a yazar; takılı kalmaz (süre sınırlı)."""
    parcalar = []
    for g in grup:
        mime, veri = _dosya_hazirla(g, resim_boyu)
        parcalar.append({"inline_data": {"mime_type": mime, "data": veri}})
    parcalar.append({"text": istem})
    log("   Uygun Gemini modelleri soruluyor...")
    modeller = [model_tercih] if model_tercih else gemini_modelleri(api_key)
    hatalar = []
    zaman = 180 if len(grup) > 1 or json_mod else 120
    for model in modeller:
        config = {"temperature": 0.1, "maxOutputTokens": 32768}
        if json_mod:
            config["responseMimeType"] = "application/json"
        if "2.5" in model:                       # "düşünme"yi kapat: hızlı ve token yemesin
            config["thinkingConfig"] = {"thinkingBudget": 0}
        else:                                     # yeni modeller: düşünmeyi en aza indir
            config["thinkingConfig"] = {"thinkingLevel": "low"}
        govde = {"contents": [{"parts": parcalar}], "generationConfig": config}
        url = f"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent"
        r = None
        for deneme in range(3):
            log(f"   → {model} deneniyor" + (f" (tekrar {deneme})" if deneme else "") + "...")
            baslangic = time.time()
            try:
                r = requests.post(url, params={"key": api_key}, json=govde, timeout=(15, zaman))
            except requests.Timeout:
                log(f"   {model}: {zaman} sn içinde cevap vermedi, sonraki modele geçiliyor.")
                hatalar.append(f"{model}: zaman aşımı")
                r = None
                break
            except requests.RequestException as e:
                log(f"   {model}: bağlantı hatası ({e.__class__.__name__}).")
                hatalar.append(f"{model}: bağlantı")
                r = None
                break
            sure = int(time.time() - baslangic)
            if r.status_code == 429:             # ücretsiz kota (dakikalık/günlük)
                gun = "per day" in r.text.lower() or "PerDay" in r.text
                m = re.search(r'"retryDelay":\s*"(\d+)', r.text)
                bekle = min(int(m.group(1)) + 2 if m else 30, 60)
                if gun or deneme == 2:
                    log(f"   {model}: {'günlük' if gun else 'dakikalık'} ücretsiz kota doldu, sonraki modele geçiliyor.")
                    hatalar.append(f"{model}: kota")
                    break
                log(f"   {model}: dakikalık kota doldu, {bekle} sn bekleniyor...")
                time.sleep(bekle)
                continue
            if r.status_code == 400 and "thinking" in r.text.lower() and "thinkingConfig" in config:
                config.pop("thinkingConfig")      # bu model düşünmeyi kapatmaya izin vermiyor
                continue
            if r.status_code in (500, 502, 503, 504):
                log(f"   {model}: Google sunucusu meşgul (HTTP {r.status_code}), sonraki modele geçiliyor.")
                hatalar.append(f"{model}: meşgul")
                break
            break
        if r is None or r.status_code != 200:
            if r is not None and r.status_code in (400, 401, 403) and "API key" in r.text:
                raise RuntimeError("API_KEY_HATALI: anahtar geçersiz — 'API anahtarını değiştir' ile yeniden gir.")
            if r is not None and r.status_code not in (429, 500, 502, 503, 504):
                msg = r.text[:200].replace("\n", " ")
                log(f"   {model}: HTTP {r.status_code} — {msg}")
                hatalar.append(f"{model}: HTTP {r.status_code}")
            continue
        veri = r.json()
        try:
            aday = veri["candidates"][0]
            metin = "".join(p.get("text", "") for p in aday.get("content", {}).get("parts", []))
        except (KeyError, IndexError):
            metin, aday = "", {}
        if metin.strip():
            log(f"   ✓ {model} cevap verdi ({sure} sn).")
            return metin
        neden = aday.get("finishReason") or veri.get("promptFeedback", {}).get("blockReason", "boş cevap")
        log(f"   {model}: boş cevap ({neden}), sonraki modele geçiliyor.")
        hatalar.append(f"{model}: {neden}")
    raise RuntimeError("Gemini menüyü okuyamadı → " + " | ".join(hatalar or ["model yok"]))


def gemini_modelleri(api_key):
    """Anahtarın kullanabildiği güncel 'flash' modellerini Google'dan sorar (model adları değişiyor)."""
    if api_key in _GEMINI_CACHE:
        return _GEMINI_CACHE[api_key]
    adaylar = []
    try:
        r = requests.get("https://generativelanguage.googleapis.com/v1beta/models",
                         params={"key": api_key, "pageSize": 200}, timeout=(10, 15))
        if r.status_code == 200:
            for mdl in r.json().get("models", []):
                ad = mdl.get("name", "").split("/")[-1]
                if ("generateContent" in mdl.get("supportedGenerationMethods", [])
                        and "flash" in ad
                        and not any(x in ad for x in ("image", "tts", "audio", "live", "embedding",
                                                      "thinking", "exp", "preview", "8b"))):
                    adaylar.append(ad)
    except Exception:
        pass

    def anahtar(ad):
        # En hızlı ve düşünmesi kapatılabilen gemini-2.5-flash önce, sonra diğerleri
        surum = [int(x) for x in re.findall(r"\d+", ad)[:2]] or [0]
        return (ad == "gemini-2.5-flash", ad.endswith("-latest"), "lite" not in ad, surum)
    adaylar = sorted(set(adaylar), key=anahtar, reverse=True)
    liste = (adaylar[:4] or []) + [m for m in GEMINI_MODELLER if m not in adaylar]
    _GEMINI_CACHE[api_key] = liste
    return liste


def _ollama(grup, model, istem=MENU_PROMPT):
    resimler = []
    for g in grup:
        if g.lower().endswith(".pdf"):
            raise RuntimeError("Ollama PDF okuyamaz; menüyü resim (jpg/png) olarak ver.")
        resimler.append(_dosya_hazirla(g)[1])
    try:
        r = requests.post(f"{OLLAMA_URL}/api/chat", timeout=900, json={
            "model": model or OLLAMA_MODEL, "stream": False,
            "options": {"temperature": 0.1, "num_ctx": 8192},
            "messages": [{"role": "user", "content": istem, "images": resimler}]})
    except requests.ConnectionError:
        raise RuntimeError("Ollama çalışmıyor. ollama.com'dan kur, sonra: ollama pull " + OLLAMA_MODEL)
    if r.status_code != 200:
        raise RuntimeError(f"Ollama hatası: {r.text[:300]}")
    return r.json()["message"]["content"]


def menuyu_oku(dosyalar, saglayici, api_key, model, log):
    sonuc = []
    grup_boyu = 1          # her sayfa ayrı: daha hızlı cevap, kota/zaman aşımı riski düşük
    for i in range(0, len(dosyalar), grup_boyu):
        grup = dosyalar[i:i + grup_boyu]
        log(f"Menü okunuyor ({i + 1}/{len(dosyalar)}): {', '.join(os.path.basename(g) for g in grup)}")
        if saglayici == "gemini":
            metin = _gemini(grup, api_key, model, log=log)
        else:
            metin = _ollama(grup, model)
        sonuc.append(metin.strip())
    return "\n\n".join(sonuc)


BIRLESIK_PROMPT = """Sana bir işletmenin menüsüyle ilgili {n} resim veriyorum (sırayla 1, 2, ...).
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
- resim: Ürünün broşürde KENDİNE AİT ayrı bir fotoğrafı varsa {{"sayfa": resim no, "kutu": [ymin, xmin, ymax, xmax]}}
  (0-1000 arası normalize koordinat). Kutu sadece yemek fotoğrafını kapsasın: yazı, fiyat etiketi, logo hariç.
  Fotoğraf birden fazla farklı ürünü birlikte gösteriyorsa (ör. içecek şişeleri grubu) ya da emin değilsen null.
- odeme: menüde/listede geçen ödeme yöntemleri ve yemek kartları (logo ya da yazı: Ticket, Setcard, Metropol,
  Sodexo, Multinet, Tokenflex, Visa/Mastercard → "Kredi Kartı"...). Sadece "tüm yemek kartları geçerli" gibi
  genel bir ifade varsa "Yemek Kartı" da ekle.
- farklar: kısa Türkçe notlar — sadece listede olanlar, sadece broşürde olanlar, fiyatı farklı olanlar,
  ekstralar (ör. "Patates Kızartması sadece listede", "cheddar/ranch sos +10 TL").

SADECE şu JSON'u döndür:
{{"urunler":[{{"kategori":"","pisme":"","tip":"Sadece Yemek","ad":"","arama":"","fiyat":0,
"porsiyonlar":[{{"ad":"","fiyat":0}}],"resim":null}}],"odeme":[],"farklar":[]}}
"""


def _json_coz(metin):
    """Model cevabından JSON'u güvenle çıkarır (kod bloğu, sondaki virgül vb. temizlenir)."""
    t = (metin or "").strip()
    t = re.sub(r"^```(?:json)?\s*|\s*```$", "", t)
    try:
        return json.loads(t)
    except ValueError:
        pass
    m = re.search(r"\{.*\}", t, re.S)
    if not m:
        raise RuntimeError("Menü okunamadı: geçerli veri gelmedi.")
    t = re.sub(r",\s*([}\]])", r"\1", m.group(0))
    return json.loads(t)


def _f(v):
    if isinstance(v, (int, float)):
        return float(v) if v > 0 else None
    return fiyat_coz(str(v)) if v not in (None, "") else None


def brosur_json_metne(veri, dosyalar):
    """JSON → kutudaki liste metni + broşürden kesilecek resimler + farklar"""
    sira, gruplar, kirp = [], {}, {}
    for u in veri.get("urunler") or []:
        ad = guvenli_ad(str(u.get("ad") or "").strip())
        if not ad:
            continue
        kat = guvenli_ad(str(u.get("kategori") or "Genel")) or "Genel"
        pisme = guvenli_ad(str(u.get("pisme") or "Hazır")) or "Hazır"
        tip = "Menü" if "men" in _norm(str(u.get("tip") or "")) else "Sadece Yemek"
        anahtar = (kat, pisme, tip)
        if anahtar not in gruplar:
            gruplar[anahtar] = []
            sira.append(anahtar)
        fiyat = _f(u.get("fiyat"))
        pors = [(porsiyon_etiketi(str(p.get("ad") or "")), _f(p.get("fiyat")))
                for p in (u.get("porsiyonlar") or []) if isinstance(p, dict) and p.get("ad")]
        pors = [(e, f) for e, f in pors if e]
        satir = ad + f" | {str(u.get('arama') or ad).replace('|', ' ')}"
        if fiyat or pors:
            satir += f" | {'%g' % fiyat if fiyat else ''}"
        if pors:
            satir += " | " + "; ".join(f"{e}={'%g' % f}" if f else e for e, f in pors)
        gruplar[anahtar].append(satir)
        r = u.get("resim")
        if isinstance(r, dict) and isinstance(r.get("kutu"), list) and len(r["kutu"]) == 4:
            try:
                sayfa = int(r.get("sayfa") or 1)
                kutu = [float(x) for x in r["kutu"]]
                if 1 <= sayfa <= len(dosyalar):
                    kirp[_norm(ad)] = (dosyalar[sayfa - 1], kutu)
            except (TypeError, ValueError):
                pass
    cikti, onceki = [], (None, None)
    for kat, pisme, tip in sira:
        if onceki[0] != kat:
            if cikti:
                cikti.append("")
            cikti.append(f"# {kat}")
            onceki = (kat, None)
        if onceki[1] != pisme:
            cikti.append(f"## {pisme}")
            onceki = (kat, pisme)
        cikti.append(f"### {tip}")
        cikti.extend(gruplar[(kat, pisme, tip)])
    odeme = [str(x) for x in (veri.get("odeme") or []) if str(x).strip()]
    if odeme:
        cikti += ["", "ÖDEME: " + ", ".join(odeme)]
    farklar = [str(x) for x in (veri.get("farklar") or []) if str(x).strip()]
    return "\n".join(cikti), kirp, farklar


def liste_eksik_kontrol(liste_metni, sonuc_metni):
    """Yazılı listede fiyatıyla geçip sonuçta bulunamayan satırları döndürür (kontrol için)."""
    urunler = [_norm(u[0]) for v in listeyi_ayristir(sonuc_metni).values() for u in v]
    kelimeler = [set(re.findall(r"[a-z0-9]{3,}", u)) for u in urunler]
    eksik = []
    for satir in (liste_metni or "").splitlines():
        s = satir.strip().lstrip("-•*·").strip()
        if not s or s.startswith("#") or odeme_satiri_mi(s):
            continue
        ad, _, fiyat, _ = satiri_coz(s)
        if not fiyat:
            continue
        n = _norm(ad)
        if not n or re.fullmatch(r"(?:%s|bu[cç]uk|[0-9 ]+)(?:\s*(?:porsiyon|pors|parca|gram|gr))*" % _PORS_KELIME, n, re.I):
            continue                                   # "3 Parça 400 TL", "Buçuk 225 TL" gibi porsiyon satırları
        k = set(re.findall(r"[a-z0-9]{3,}", n)) - {"gram", "porsiyon", "yarim", "ekmek", "parca", "tek", "tam",
                                                    "bucuk", "duble", "double", "buyuk", "kucuk", "orta", "cift"}
        if not k:
            continue
        en_iyi = max((len(k & w) / len(k) for w in kelimeler), default=0)
        if en_iyi < 0.75:
            eksik.append(ad)
    return eksik


def brosur_oku(dosyalar, liste_metni, api_key, model, log):
    """Broşür resimleri (+ varsa yazılı liste) → (liste metni, kırpmalar, farklar)"""
    liste_notu = ""
    if liste_metni.strip():
        liste_notu = ("\nAyrıca müşterinin YAZILI LİSTESİ aşağıda; broşürle karşılaştır, fiyatlarda bunu esas al:\n"
                      "-----\n" + liste_metni.strip()[:12000] + "\n-----")
    istem = BIRLESIK_PROMPT.format(n=len(dosyalar), liste_notu=liste_notu)
    log(f"Broşür{' + liste' if liste_notu else ''} okunuyor ({len(dosyalar)} resim)...")
    cevap = _gemini(dosyalar[:6], api_key, model, istem, log=log, json_mod=True, resim_boyu=2048)
    veri = _json_coz(cevap)
    return brosur_json_metne(veri, dosyalar[:6])


def brosurden_kes(dosya, kutu, hedef):
    """Broşürdeki ürün fotoğrafını (0-1000 normalize kutu) kesip 600x600 beyaz zemine yerleştirir."""
    if dosya.lower().endswith(".pdf"):
        return False
    img = Image.open(dosya).convert("RGB")
    W, H = img.size
    y0, x0, y1, x1 = kutu
    if y0 > y1:
        y0, y1 = y1, y0
    if x0 > x1:
        x0, x1 = x1, x0
    pay_x, pay_y = (x1 - x0) * 0.03, (y1 - y0) * 0.03
    sol, ust = max(0, (x0 - pay_x) / 1000 * W), max(0, (y0 - pay_y) / 1000 * H)
    sag, alt = min(W, (x1 + pay_x) / 1000 * W), min(H, (y1 + pay_y) / 1000 * H)
    if sag - sol < 40 or alt - ust < 40:
        return False
    parca = img.crop((int(sol), int(ust), int(sag), int(alt)))
    oran = min(BOYUT / parca.width, BOYUT / parca.height)
    parca = parca.resize((max(1, round(parca.width * oran)), max(1, round(parca.height * oran))), Image.LANCZOS)
    tuval = Image.new("RGB", (BOYUT, BOYUT), (255, 255, 255))
    tuval.paste(parca, ((BOYUT - parca.width) // 2, (BOYUT - parca.height) // 2))
    tuval.save(hedef, "JPEG", quality=90, optimize=True)
    return True


def basliksiz_mi(metin):
    satirlar = [x.strip() for x in metin.splitlines() if x.strip()]
    return bool(satirlar) and not any(x.startswith("#") for x in satirlar)


def listeyi_duzenle(metin, saglayici, api_key, model, log):
    """Başlıksız düz listeyi kategorilere ayırır."""
    log("Liste düz yazılmış, kategorilere ayrılıyor...")
    istem = LISTE_PROMPT + metin.strip()
    if saglayici == "gemini":
        sonuc = _gemini([], api_key, model, istem, log=log)
    else:
        sonuc = _ollama([], model, istem)
    return sonuc.strip()


# ---------------------------------------------------------------- yerleşik sınıflandırıcı (internetsiz)

_TR = str.maketrans("İIıÇçĞğÖöŞşÜüÂâÎîÛû", "iiiccggoossuuaaiiuu")


def _norm(metin):
    return metin.translate(_TR).lower().replace(".", " ").strip()


# Yazım düzeltmeleri (normalize hali → doğru yazım)
_DUZELT = {
    "havuc": "Havuç", "salatalik": "Salatalık", "mandalin": "Mandalina",
    "kavacik": "kavacık", "sogan": "Soğan", "sarimsak": "Sarımsak",
    "patlican": "Patlıcan", "pirasa": "Pırasa", "ispanak": "Ispanak",
}

# Manav / market: anahtar kelime → kategori (en uzun eşleşme kazanır)
_MANAV = {
    "Yeşillikler": ["maydanoz", "roka", "dereotu", "nane", "tere", "taze sogan", "fesleğen",
                    "feslegen", "semizotu", "kuzukulagi", "reyhan", "kisnis", "taze nane"],
    "Sebzeler": ["domates", "salatalik", "biber", "patlican", "kabak", "patates", "sogan",
                 "sarimsak", "marul", "havuc", "turp", "lahana", "pirasa", "kereviz", "ispanak",
                 "pancar", "fasulye", "bezelye", "bamya", "enginar", "brokoli", "karnabahar",
                 "mantar", "misir", "pazi", "bakla", "zencefil", "tatli patates", "kapya",
                 "kuskonmaz", "brüksel", "bruksel", "lahana tursusu", "barbunya", "borulce"],
    "Meyveler": ["elma", "armut", "portakal", "mandalina", "mandalin", "greyfurt", "limon", "nar",
                 "ayva", "muz", "kivi", "hurma", "cilek", "uzum", "mersin", "ananas", "karpuz",
                 "kavun", "seftali", "kayisi", "erik", "kiraz", "visne", "incir", "avokado",
                 "mango", "nektarin", "dut", "bogurtlen", "ahududu", "frambuaz", "papaya",
                 "pitaya", "ejder meyvesi", "hindistan cevizi", "misket limonu", "lime",
                 "altin cilek", "yaban mersini", "trabzon hurmasi", "cennet hurmasi"],
    "Kuruyemiş": ["findik", "fistik", "ceviz", "badem", "leblebi", "kaju", "cekirdek", "kuru uzum",
                  "kuru kayisi", "kuru incir"],
}

# Restoran: anahtar kelime → kategori
_RESTORAN = {
    "Döner": ["doner", "iskender", "durum doner"],
    "Burger": ["burger", "hamburger", "cheeseburger"],
    "Pizza": ["pizza"],
    "Pide & Lahmacun": ["pide", "lahmacun"],
    "Kebap": ["kebap", "kebab", "adana", "urfa", "beyti", "sis", "kofte", "kanat", "pirzola", "cop sis"],
    "Tavuk": ["tavuk", "nugget", "chicken", "schnitzel"],
    "Uzak Doğu": ["ramen", "sushi", "noodle", "wok", "udon", "gyoza", "dumpling"],
    "Makarna": ["makarna", "spagetti", "penne", "fettuccine", "lazanya", "manti"],
    "Çorbalar": ["corba", "mercimek corbasi", "iskembe"],
    "Salatalar": ["salata", "sezar"],
    "Tost & Sandviç": ["tost", "sandvic", "wrap", "kumru", "hot dog", "sosisli"],
    "Kahvaltı": ["kahvalti", "menemen", "omlet", "sucuklu yumurta", "simit", "pogaca", "borek"],
    "Tatlılar": ["tatli", "baklava", "kunefe", "sutlac", "kazandibi", "pasta", "cheesecake",
                 "brownie", "dondurma", "tiramisu", "waffle", "puding", "katmer", "trilece"],
    "Alkollü İçecekler": ["bira", "efes", "tuborg", "carlsberg", "rakı", "raki", "sarap", "vodka",
                          "viski", "whisky", "cin", "gin", "tekila", "kokteyl", "bomonti", "corona"],
    "İçecekler": ["kola", "cola", "fanta", "sprite", "ayran", "su", "soda", "gazoz", "meyve suyu",
                  "limonata", "cay", "kahve", "latte", "americano", "espresso", "ice tea", "salgam",
                  "redbull", "red bull", "enerji", "milkshake", "smoothie", "nescafe", "salep"],
    "Yan Ürünler": ["patates kizartmasi", "cips", "sogan halkasi", "sos", "ekmek", "pilav", "tursu"],
}

_PISME = {
    "Kızartma": ["kizartma", "crispy", "citir", "nugget", "kanat", "schnitzel", "patates kizartmasi",
                 "sogan halkasi", "cips"],
    "Izgara": ["izgara", "burger", "kebap", "kebab", "adana", "urfa", "sis", "kofte", "pirzola",
               "doner", "iskender", "tavuk"],
    "Fırın": ["firin", "pizza", "pide", "lahmacun", "lazanya", "borek", "pogaca", "simit"],
    "Haşlama": ["ramen", "makarna", "spagetti", "noodle", "manti", "corba", "udon"],
    "Tava": ["tava", "wok", "menemen", "omlet", "sote"],
    "Soğuk": ["salata", "sezar", "cacik", "humus", "meze", "sushi"],
}


def _en_uzun(urun_n, sozluk):
    en, uzunluk = None, 0
    kelimeler = f" {urun_n} "
    for anahtar, liste in sozluk.items():
        for k in liste:
            k_n = _norm(k)
            if (f" {k_n} " in kelimeler or k_n in urun_n and len(k_n) >= 5) and len(k_n) > uzunluk:
                en, uzunluk = anahtar, len(k_n)
    return en, uzunluk


def _duzelt_ad(ad):
    parcalar = []
    for kelime in re.sub(r"(?<!\d)\.|\.(?!\d)", " ", ad).split():   # "1.5" korunur
        d = _DUZELT.get(_norm(kelime))
        if d:
            kelime = d if kelime[:1].isupper() else d.lower()
        parcalar.append(kelime)
    return " ".join(parcalar)


def _ilk_gorulme(sira, yol):
    """Kategoriler listedeki ilk görülme sırasıyla yazılır (alt klasörler üst kategorinin yanında)."""
    for i, y in enumerate(sira):
        if y[0] == yol[0]:
            return (i, sira.index(yol))
    return (len(sira), 0)


def yerel_siniflandir(metin):
    """Başlıksız listeyi internetsiz olarak kategorilere ayırır."""
    odeme_satirlari = []
    gruplar = {}          # yol -> [satır]
    sira = []
    gorulen = set()
    for satir in metin.splitlines():
        s = satir.strip().lstrip("-•* ").strip()
        if not s or s.startswith("#"):
            continue
        if odeme_satiri_mi(s):
            odeme_satirlari.append(s)
            continue
        ad_kismi, arama, fiyat, pors = satiri_coz(s)
        ad = _duzelt_ad(guvenli_ad(ad_kismi))
        if not ad or _norm(ad) in gorulen:
            continue
        gorulen.add(_norm(ad))
        n = _norm(ad)
        ay = porsiyon_ayir(ad)                   # "Adana Az" → "Adana" ile sınıflandır
        if ay:
            n = _norm(ay[0])

        manav, m_uz = _en_uzun(n, _MANAV)
        restoran, r_uz = _en_uzun(n, _RESTORAN)
        if manav and m_uz >= r_uz:          # eşitlikte manav kazanır (ör. "Tatlı kıl biber")
            yol = (manav,)
        elif restoran:
            pisme = _en_uzun(n, _PISME)[0] or ("Hazır" if restoran in
                                            ("İçecekler", "Alkollü İçecekler", "Tatlılar") else "Diğer")
            alt = "Menü" if " menu " in f" {n} " or "menusu" in n else "Sadece Yemek"
            yol = (restoran, pisme, alt)
        else:
            yol = ("Diğer",)
        if yol not in gruplar:
            gruplar[yol] = []
            sira.append(yol)
        gruplar[yol].append((ad, arama.strip() or ad, fiyat, pors))

    # Başlıkları yaz (aynı üst başlıkları tekrar etmeden)
    cikti, onceki = [], ()
    for yol in sorted(sira, key=lambda y: (y == ("Diğer",), _ilk_gorulme(sira, y))):
        for seviye, ad in enumerate(yol):
            if onceki[:seviye + 1] != yol[:seviye + 1]:
                if seviye == 0 and cikti:
                    cikti.append("")
                cikti.append("#" * (seviye + 1) + " " + ad)
        for ad, arama, fiyat, pors in porsiyon_grupla(gruplar[yol]):
            satir = ad
            if fiyat or pors or _pors_norm(arama) != _pors_norm(ad):
                satir += f" | {arama}"
            if fiyat or pors:
                satir += f" | {'%g' % fiyat if fiyat else ''}"
            if pors:
                satir += f" | {porsiyonlari_yaz(pors)}"
            cikti.append(satir)
        onceki = yol
    if odeme_satirlari:
        cikti += [""] + odeme_satirlari
    return "\n".join(cikti)


# ---------------------------------------------------------------- resim indirme

def kare_jpeg_kaydet(veri: bytes, hedef: str) -> bool:
    try:
        img = Image.open(io.BytesIO(veri))
        img.load()
    except Exception:
        return False
    if img.width < 200 or img.height < 200:
        return False
    if img.mode in ("RGBA", "LA", "P"):
        img = img.convert("RGBA")
        duz = Image.new("RGB", img.size, (255, 255, 255))
        duz.paste(img, mask=img.split()[-1])
        img = duz
    else:
        img = img.convert("RGB")
    oran = min(BOYUT / img.width, BOYUT / img.height)
    yeni = (max(1, round(img.width * oran)), max(1, round(img.height * oran)))
    img = img.resize(yeni, Image.LANCZOS)
    tuval = Image.new("RGB", (BOYUT, BOYUT), (255, 255, 255))
    tuval.paste(img, ((BOYUT - yeni[0]) // 2, (BOYUT - yeni[1]) // 2))
    tuval.save(hedef, "JPEG", quality=90, optimize=True)
    return True


# Kategoriye göre aramaya eklenen kelime (ör. "Turp" → "Turp sebze")
KATEGORI_IPUCU = [("yesillik", "yeşillik demet"), ("sebze", "sebze"), ("meyve", "meyve"),
                  ("kuruyemis", "kuruyemiş"), ("baharat", "baharat"), ("et urun", "kasap"),
                  ("sarkuteri", "şarküteri"), ("icecek", "içecek"), ("tatli", "tatlı")]

# Başka anlamı da olan ürünler için hazır aramalar (normalize ad → arama)
ARAMA_DUZELT = {
    "turp": "kırmızı turp sebze", "roka": "roka yeşillik demet", "tere": "tere otu demet",
    "nane": "taze nane demet", "dereotu": "dereotu demet", "maydanoz": "maydanoz demet",
    "kabak": "sakız kabak sebze", "pancar": "kırmızı pancar sebze", "kereviz": "kereviz kökü sebze",
    "sogan": "kuru soğan sebze", "taze sogan": "taze soğan demet", "arpacik sogan": "arpacık soğan",
    "patates": "patates sebze", "ispanak": "ıspanak sebze", "domates": "domates sebze",
    "limon": "limon meyve", "limon lamas yatak": "lamas limonu meyve", "lamas limon": "lamas limonu",
    "nar": "nar meyvesi", "ayva": "ayva meyvesi", "kivi": "kivi meyvesi",
    "muz ithal": "muz meyvesi salkım", "yerli muz": "yerli anamur muzu",
    "elma arjantin": "arjantin elması", "elma amasya": "amasya elması",
    "elma starking": "starking elma", "elma golden": "golden elma",
    "armut deveci": "deveci armut", "armut santamaria": "santa maria armut",
    "topan patlican": "topan patlıcan yuvarlak", "altin cilek": "altın çilek physalis meyve",
    "uzum kavacik": "kavacık üzümü", "uzum sultaniye": "sultaniye üzüm",
    "cennet hurmasi": "cennet hurması trabzon hurması", "sarimsak kastamonu": "kastamonu taşköprü sarımsak",
    "mor lahana": "mor lahana sebze", "duz marul": "göbek marul sebze",
    "kivircik marul": "kıvırcık marul sebze", "portakal": "portakal meyve",
    "mandalina": "mandalina meyve", "greyfurt": "greyfurt meyve",
}

# Başlığında/adresinde bunlar geçen resimler elenir
YASAK_KELIMELER = [   # kökler (Türkçe ekler gelse de yakalanır)
    "yatak", "ortu", "nevresim", "carsaf", "yorgan", "yastig", "yastik", "gozlu", "sunglass",
    "eyewear", "ameliyat", "prostat", "surgery", "resection", "hastal", "tedavi", "urolog",
    "kilif", "telefon", "ayakkabi", "elbise", "tisort", "t-shirt", "canta", "kitap", "oyuncak",
    "kolye", "parfum", "sabun", "kumas", "perde", "mobilya", "otomobil", "logo",
    "oyuncu", "clipart", "cizim", "drawing", "cartoon", "karikatur", "emoji", "vector",
    # hastane / sağlık siteleri (ör. "Turp" ameliyatı)
    "memorial", "acibadem", "medicalpark", "medical", "medikal", "hastane", "hospital",
    "klinik", "clinic", "doktor", "doctor", "opdr", "op-dr", "uroloji", "endoskop",
    "saglik", "health", "lybrate", "anatomy", "anatomi", "mesane", "bladder",
]

# Aramaya eklenen dışlama kelimeleri (ürün → arama motoruna "-kelime")
ARAMA_HARIC = {
    "turp": ["ameliyat", "prostat", "hastane", "urology"],
    "roka": ["gözlük", "güneş"],
    "limon lamas yatak": ["yatak", "örtü", "nevresim"],
    "kabak": ["tadı"],
}


def arama_olustur(ad, arama, kategori_yolu):
    """Ürün adı + kategori ipucu ile doğru resmi bulduracak arama cümlesi."""
    n = _norm(ad)
    if arama and _norm(arama) != n:          # elle ya da yapay zekâ ile verilmiş arama
        return arama
    if n in ARAMA_DUZELT:
        haric = " ".join(f"-{k}" for k in ARAMA_HARIC.get(n, []))
        return f"{ARAMA_DUZELT[n]} {haric}".strip()
    temiz = " ".join(k for k in ad.split() if _norm(k) not in ("yatak",))
    ust = _norm(kategori_yolu.split(os.sep)[0]) if kategori_yolu else ""
    ipucu = next((v for k, v in KATEGORI_IPUCU if k in ust), "")
    if ipucu and _norm(ipucu.split()[0]) not in _norm(temiz):
        return f"{temiz} {ipucu}"
    return temiz


def _sayi_al(v):
    """'800', '800 px', 800, None → 800 / 0  (arama kütüphanesi boyutu metin döndürebiliyor)"""
    try:
        return int(re.sub(r"[^0-9]", "", str(v)) or 0)
    except (TypeError, ValueError):
        return 0


def _sonuc_puani(r, ad):
    metin = _norm(" ".join(str(r.get(k, "")) for k in ("title", "url", "image", "source")))
    if any(_norm(y) in metin for y in YASAK_KELIMELER):
        return -1
    anahtarlar = [k for k in _norm(ad).split() if len(k) >= 3]
    puan = sum(2 for k in anahtarlar if k in metin)
    w, h = _sayi_al(r.get("width")), _sayi_al(r.get("height"))
    if w and h and 0.6 <= w / h <= 1.7 and min(w, h) >= 400:
        puan += 1                              # kareye yakın, yeterince büyük
    return puan


def _bing_resim(sorgu, adet):
    """DuckDuckGo çalışmazsa yedek: Bing görsel arama sayfasından resim adreslerini okur."""
    import html as _html
    r = requests.get("https://www.bing.com/images/search",
                     params={"q": sorgu, "form": "HDRSC2", "first": "1", "cc": "tr", "setlang": "tr"},
                     headers=HEADERS, timeout=20)
    sonuc = []
    for blok in re.findall(r'class="iusc"[^>]*\sm="([^"]+)"', r.text):
        try:
            veri = json.loads(_html.unescape(blok))
        except ValueError:
            continue
        if veri.get("murl"):
            sonuc.append({"image": veri["murl"], "title": veri.get("t", ""),
                          "url": veri.get("purl", ""), "width": "", "height": ""})
        if len(sonuc) >= max(15, adet * 8):
            break
    return sonuc


def resim_ara(sorgu, adet, log, ad=None):
    sonuc = None
    for deneme in range(3):
        try:
            sonuc = list(DDGS().images(sorgu, region="tr-tr", safesearch="moderate",
                                       max_results=max(15, adet * 8)))
            break
        except Exception as e:
            bekle = 5 * (deneme + 1)
            log(f"   Arama hatası ({e.__class__.__name__}: {str(e)[:80]}), {bekle} sn bekleniyor...")
            if deneme == 0:
                try:
                    sonuc = _bing_resim(sorgu, adet)
                    if sonuc:
                        log("   Yedek arama (Bing) kullanıldı.")
                        break
                except Exception as e2:
                    log(f"   Yedek arama da olmadı ({e2.__class__.__name__}).")
            time.sleep(bekle)
    if not sonuc:
        try:
            sonuc = _bing_resim(sorgu, adet)
        except Exception:
            sonuc = []
    if not sonuc:
        return []
    if ad:
        puanli = []
        for i, r in enumerate(sonuc):
            try:
                p = _sonuc_puani(r, ad)
            except Exception:
                p = 0                      # puanlanamayan sonuç elenmez, sıraya girer
            puanli.append((p, i, r))
        sonuc = [r for p, i, r in sorted(puanli, key=lambda x: (-x[0], x[1])) if p >= 0]
    return sonuc


def _kullanilan_oku(kok):
    try:
        with open(os.path.join(kok, ".kullanilan_resimler.json"), encoding="utf-8") as f:
            return json.load(f)
    except Exception:
        return {}


def _kullanilan_yaz(kok, veri):
    try:
        with open(os.path.join(kok, ".kullanilan_resimler.json"), "w", encoding="utf-8") as f:
            json.dump(veri, f, ensure_ascii=False)
    except Exception:
        pass


def urun_indir(ad, sorgu, klasor, adet, log, kullanilan=None, yenile=False) -> int:
    """kullanilan: bu ürün için daha önce indirilmiş adresler (silinip tekrar istenince farklısı gelir)"""
    kullanilan = kullanilan if kullanilan is not None else []
    mevcut = [f for f in os.listdir(klasor)
              if os.path.splitext(f)[0].lower() in {ad.lower()} | {f"{ad}_{i}".lower() for i in range(1, 11)}
              and f.lower().endswith(".jpg")]
    if len(mevcut) >= adet and not yenile:
        log(f"  ✓ {ad} (zaten var, atlandı)")
        return len(mevcut)
    if yenile:
        for f in mevcut:
            try:
                os.remove(os.path.join(klasor, f))
            except OSError:
                pass
    kaydedilen = 0
    log(f"  … {ad}  [arama: {sorgu}]")
    for r in resim_ara(sorgu, adet, log, ad):
        if kaydedilen >= adet:
            break
        url = r.get("image")
        if not url or url in kullanilan:
            continue
        try:
            y = requests.get(url, headers=HEADERS, timeout=15)
            if y.status_code != 200:
                continue
        except Exception:
            continue
        dosya = f"{ad}.jpg" if adet == 1 else f"{ad}_{kaydedilen + 1}.jpg"
        if kare_jpeg_kaydet(y.content, os.path.join(klasor, dosya)):
            kaydedilen += 1
            kullanilan.append(url)
    if kaydedilen == 0:
        try:
            yedek_gorsel(ad, os.path.basename(klasor), os.path.join(klasor, f"{ad}.jpg"))
            log(f"  ~ {ad} (bulunamadı, adı yazılı yedek görsel konuldu)")
            return -1
        except Exception:
            pass
    log(f"  {'✓' if kaydedilen else '✗'} {ad} ({kaydedilen}/{adet})")
    return kaydedilen


def _font(kalin, boyut):
    from PIL import ImageFont
    adaylar = (["arialbd.ttf", "segoeuib.ttf", "DejaVuSans-Bold.ttf",
                "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf"] if kalin else
               ["arial.ttf", "segoeui.ttf", "DejaVuSans.ttf",
                "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf"])
    for a in adaylar:
        try:
            return ImageFont.truetype(a, boyut)
        except OSError:
            continue
    return ImageFont.load_default()


def yedek_gorsel(ad, alt_baslik, hedef):
    """Resmi bulunamayan ürün için 600x600, adı yazılı sade bir kart."""
    import textwrap
    from PIL import ImageDraw
    paletler = [((232, 245, 233), (46, 125, 50)), ((255, 243, 224), (230, 81, 0)),
                ((227, 242, 253), (21, 101, 192)), ((252, 228, 236), (173, 20, 87)),
                ((243, 229, 245), (106, 27, 154)), ((255, 248, 225), (191, 144, 0))]
    zemin, ana = paletler[sum(map(ord, alt_baslik)) % len(paletler)]
    img = Image.new("RGB", (BOYUT, BOYUT), zemin)
    d = ImageDraw.Draw(img)
    d.rounded_rectangle((30, 30, 570, 570), 40, outline=ana, width=8, fill=(255, 255, 255))
    d.ellipse((220, 90, 380, 250), fill=ana)
    harf = "İ" if ad[:1] == "i" else ad[:1].upper()
    f = _font(True, 100)
    w = d.textlength(harf, font=f)
    d.text((300 - w / 2, 105), harf, font=f, fill=(255, 255, 255))
    satirlar = textwrap.wrap(ad, 16)[:3]
    boyut = 64
    while boyut > 26:
        f = _font(True, boyut)
        if max(d.textlength(s, font=f) for s in satirlar) <= 480:
            break
        boyut -= 4
    y = 380 - (len(satirlar) * (boyut + 12)) // 2
    for s in satirlar:
        w = d.textlength(s, font=f)
        d.text((300 - w / 2, y), s, font=f, fill=(33, 33, 33))
        y += boyut + 12
    f2 = _font(False, 30)
    w = d.textlength(alt_baslik, font=f2)
    d.text((300 - w / 2, 505), alt_baslik, font=f2, fill=ana)
    img.save(hedef, "JPEG", quality=88)


def hepsini_indir(kategoriler, cikti, adet, ek, log, klasor_arama=False, yenile=False,
                  kontrol_yolu=None, kirpmalar=None):
    toplam = sum(len(v) for v in kategoriler.values())
    os.makedirs(cikti, exist_ok=True)
    kayit = _kullanilan_oku(cikti)
    log(f"[v{SURUM}] {len(kategoriler)} klasör, {toplam} ürün. Çıktı: {BOYUT}x{BOYUT} JPEG\n")
    basarisiz = []
    for kat, urunler in kategoriler.items():
        klasor = os.path.join(cikti, kat)
        os.makedirs(klasor, exist_ok=True)
        log(f"[{kat.replace(os.sep, ' / ')}]")
        baglam = ""
        if klasor_arama:
            baglam = " ".join(p for p in kat.split(os.sep)
                              if p.lower() not in ("genel", "sadece yemek", "hazır"))
        for u in urunler:
            ad, arama = u[0], u[1]
            if kirpmalar and _norm(ad) in kirpmalar:          # müşterinin broşüründeki kendi fotoğrafı
                dosya, kutu = kirpmalar[_norm(ad)]
                try:
                    if brosurden_kes(dosya, kutu, os.path.join(klasor, f"{ad}.jpg")):
                        log(f"  ✓ {ad} (broşürden alındı)")
                        continue
                except Exception as e:
                    log(f"  {ad}: broşürden kesilemedi ({e.__class__.__name__}), internette aranıyor")
            sorgu = " ".join(x for x in (baglam, arama_olustur(ad, arama, kat), ek) if x)
            anahtar = f"{kat}/{ad}"
            liste = kayit.setdefault(anahtar, [])
            if urun_indir(ad, sorgu, klasor, adet, log, liste, yenile) <= 0:
                basarisiz.append(f"{kat} / {ad}")
            _kullanilan_yaz(cikti, kayit)
            time.sleep(1.5)
        log("")
    kontrol = kontrol_sayfasi_yaz(kategoriler, cikti, kontrol_yolu)
    log("Resimleri görmek için 'Resimleri kontrol et' (yanlışı sil, tekrar 'İndir' → farklı resim gelir)")
    log("Bitti. Resimler: " + os.path.abspath(cikti))
    if basarisiz:
        log("\nFotoğrafı bulunamayanlar (yedek görsel kondu; istersen dosyayı elle değiştir):")
        for b in basarisiz:
            log("  - " + b)


def kontrol_sayfasi_yaz(kategoriler, cikti, html_yolu=None):
    """Tüm resimleri tek sayfada gösteren _kontrol.html (yanlışları hızlıca görmek için)."""
    import html
    from urllib.parse import quote
    hedef = html_yolu or os.path.join(cikti, "_kontrol.html")
    os.makedirs(os.path.dirname(hedef), exist_ok=True)
    parca = ["<!doctype html><meta charset='utf-8'><title>Resim kontrol</title>"
             "<style>body{font-family:Segoe UI,Arial;margin:16px;background:#f5f5f5}"
             "h2{margin:24px 0 8px}div.g{display:flex;flex-wrap:wrap;gap:10px}"
             "figure{margin:0;width:150px;background:#fff;border-radius:8px;padding:6px;"
             "box-shadow:0 1px 3px #0002;text-align:center;font-size:13px}"
             "img{width:150px;height:150px;object-fit:contain}"
             ".yok{width:150px;height:150px;display:flex;align-items:center;"
             "justify-content:center;background:#fdd;color:#900}</style>",
             "<h1>Resim kontrol</h1><p>Yanlış olan resmin dosyasını klasörden sil, "
             "programda tekrar <b>İndir</b>'e bas: sadece silinenler, farklı bir resimle iner.</p>"]
    for kat, urunler in kategoriler.items():
        parca.append(f"<h2>{html.escape(kat.replace(os.sep, ' / '))}</h2><div class='g'>")
        for u in urunler:
            ad = u[0]
            yol = os.path.join(cikti, kat, f"{ad}.jpg")
            if not os.path.exists(yol):
                yol = os.path.join(cikti, kat, f"{ad}_1.jpg")
            try:
                goreli = quote(os.path.relpath(yol, os.path.dirname(hedef)).replace(os.sep, "/"))
            except ValueError:                      # farklı sürücü (C: / D:)
                goreli = "file:///" + quote(os.path.abspath(yol).replace(os.sep, "/"))
            resim = (f"<img src='{goreli}' loading='lazy'>" if os.path.exists(yol)
                     else "<div class='yok'>resim yok</div>")
            parca.append(f"<figure>{resim}<figcaption>{html.escape(ad)}</figcaption></figure>")
        parca.append("</div>")
    with open(hedef, "w", encoding="utf-8") as f:
        f.write("\n".join(parca))
    return hedef


# ================================================================ POS SQL yazıcı

KATEGORI_TABLO = "tbl_urunkategori"
KAT_CARPAN_TABLO = "tbl_urunkategoricarpan"
URUN_CARPAN_TABLO = "tbl_uruncarpan"
URUN_TABLO = "tbl_urun"
MAKS_RESIM = 70 * 1024


# ------------------------------------------------------------ dump okuma / yazma

def dump_oku(yol):
    with open(yol, "rb") as f:
        ham = f.read()
    for kod in ("utf-8-sig", "cp1254", "latin-1"):
        try:
            metin = ham.decode(kod)
            break
        except UnicodeDecodeError:
            continue
    # Satır sonlarını içeride \n'e çevir; yazarken orijinal haline döner
    return metin.replace("\r\n", "\n"), (b"\r\n" in ham[:5000])


def sutunlari_bul(dump, tablo):
    """CREATE TABLE içinden [(ad, tip, not_null, default, auto_inc)] döner."""
    m = re.search(r"CREATE TABLE\s+`?%s`?\s*\((.*?)\n\)\s*[^;]*;" % re.escape(tablo),
                  dump, re.S | re.I)
    if not m:
        raise ValueError(f"Dump içinde '{tablo}' tablosu bulunamadı.")
    sutunlar = []
    for satir in m.group(1).splitlines():
        s = satir.strip().rstrip(",")
        sm = re.match(r"`([^`]+)`\s+(\w+(?:\([^)]*\))?(?:\s+unsigned)?)(.*)", s, re.I)
        if not sm:
            continue
        ad, tip, geri = sm.group(1), sm.group(2).lower(), sm.group(3)
        dm = re.search(r"default\s+('(?:[^'\\]|\\.|'')*'|[^\s,]+)", geri, re.I)
        sutunlar.append({
            "ad": ad, "tip": tip,
            "not_null": bool(re.search(r"not null", geri, re.I)),
            "default": dm.group(1) if dm else None,
            "auto": bool(re.search(r"auto_increment", geri, re.I)),
        })
    return sutunlar


def _bul(sutunlar, desenler, tipler=None, haric=()):
    for desen in desenler:
        for s in sutunlar:
            if s["ad"] in haric:
                continue
            if tipler and not any(t in s["tip"] for t in tipler):
                continue
            if re.fullmatch(desen, s["ad"], re.I):
                return s["ad"]
    return None


METIN = ("char", "text")
SAYI = ("int", "decimal", "float", "double", "numeric", "bit")
BLOB = ("blob", "binary")


def eslestir(sutunlar, urun_mi, elle=None):
    """Hangi sütunun ne olduğunu bulur. elle = ayarlar.json'dan zorla eşleme."""
    e = {}
    e["id"] = next((s["ad"] for s in sutunlar if s["auto"]), sutunlar[0]["ad"])
    if urun_mi:
        e["ad"] = _bul(sutunlar, [r"urun_?ad[ıi]?", r"urun_?ism[ıi]", r"urun_?isim", r"ad[ıi]?",
                                  r"isim", r"name", r"urun"], METIN)
        e["kategori"] = _bul(sutunlar, [r"(urun)?_?kategori_?id", r"kategori_?(no|kod)?",
                                        r"kat_?id", r"grup_?id"], SAYI, haric=(e["id"],))
        e["fiyat"] = _bul(sutunlar, [r"urun_?fiyat[ıi]?", r"(satis_?)?fiyat[ıi]?(_?1)?", r"satis_?fiyat\w*",
                                     r"fiyat\w*", r"price"], SAYI)
        e["sira"] = _bul(sutunlar, [r"urun_?sira", r"sira\w*"], SAYI)
        e["resim"] = _bul(sutunlar, [r"urun_?resim", r"resim\w*", r"image", r"foto\w*"], BLOB + METIN)
    else:
        e["ad"] = _bul(sutunlar, [r"kategori_?ad[ıi]?", r"kategori_?ism[ıi]", r"kategori_?isim",
                                  r"ad[ıi]?", r"isim", r"name", r"kategori"], METIN)
        e["sira"] = _bul(sutunlar, [r"kategori_?sira", r"sira\w*"], SAYI)
        e["resim"] = _bul(sutunlar, [r"kategori_?resim", r"resim\w*", r"image", r"foto\w*"],
                          BLOB + METIN)
    e["kdv"] = _bul(sutunlar, [r"\w*kdv\w*", r"vergi\w*"], SAYI)
    if elle:
        e.update({k: v for k, v in elle.items() if v})
    return e


# ------------------------------------------------------------ değer biçimleme

def tr_buyuk(metin):
    """Türkçe büyük harf: i→İ, ı→I (Python'un upper()'ı 'i'yi 'I' yapar)."""
    return (metin or "").replace("i", "İ").replace("ı", "I").upper()


def _alanlari_ayir(ic):
    """'1,'NAKIT','Nakit','N',NULL' → ['1', "'NAKIT'", ...]  (tırnak içindeki virgüller korunur)"""
    alanlar, buf, tirnak, i = [], [], False, 0
    while i < len(ic):
        c = ic[i]
        if tirnak:
            buf.append(c)
            if c == "\\" and i + 1 < len(ic):
                buf.append(ic[i + 1]); i += 2; continue
            if c == "'":
                if i + 1 < len(ic) and ic[i + 1] == "'":
                    buf.append("'"); i += 2; continue
                tirnak = False
        elif c == "'":
            tirnak = True; buf.append(c)
        elif c == ",":
            alanlar.append("".join(buf).strip()); buf = []
        else:
            buf.append(c)
        i += 1
    alanlar.append("".join(buf).strip())
    return alanlar


def _demetleri_ayir(govde):
    """'(..),(..)' → ['..', '..']  (en üst seviye parantezler, tırnaklara dikkat ederek)"""
    sonuc, derinlik, tirnak, bas, i = [], 0, False, 0, 0
    while i < len(govde):
        c = govde[i]
        if tirnak:
            if c == "\\":
                i += 2; continue
            if c == "'":
                if i + 1 < len(govde) and govde[i + 1] == "'":
                    i += 2; continue
                tirnak = False
        elif c == "'":
            tirnak = True
        elif c == "(":
            if derinlik == 0:
                bas = i + 1
            derinlik += 1
        elif c == ")":
            derinlik -= 1
            if derinlik == 0:
                sonuc.append(govde[bas:i])
        i += 1
    return sonuc


def odeme_tiplerini_sec(dump, kodlar=("NAKIT", "KREDIKARTI"), log=print):
    """tbl_odemetipi'nde verilen ödeme kodlarını seçili (odemesec='Y') ve görünür yapar.
    Diğer ödeme tiplerine dokunmaz. Sütun sırası dump'tan okunur."""
    try:
        sut = [x["ad"] for x in sutunlari_bul(dump, "tbl_odemetipi")]
    except ValueError:
        log("Uyarı: dump'ta tbl_odemetipi yok, ödeme tipleri seçilemedi.")
        return dump
    if "odemekod" not in sut or "odemesec" not in sut:
        log("Uyarı: tbl_odemetipi'nde odemekod/odemesec sütunu yok.")
        return dump
    i_kod, i_sec = sut.index("odemekod"), sut.index("odemesec")
    i_gizle = sut.index("odemegizle") if "odemegizle" in sut else None
    secilen = []

    def satir_duzelt(m):
        bas, govde = m.group(1), m.group(2)
        yeni = []
        for d in _demetleri_ayir(govde):
            alan = _alanlari_ayir(d)
            if len(alan) == len(sut) and alan[i_kod].strip("'").upper() in kodlar:
                alan[i_sec] = "'Y'"
                if i_gizle is not None:
                    alan[i_gizle] = "'N'"
                secilen.append(alan[i_kod].strip("'"))
            yeni.append("(" + ",".join(alan) + ")")
        return bas + ",".join(yeni) + ";"
    dump = re.sub(r"^(INSERT INTO `tbl_odemetipi`(?:\s*\([^)]*\))?\s+VALUES\s*)(.*);[ \t]*$",
                  satir_duzelt, dump, flags=re.M)
    if secilen:
        log("Ödeme tipleri seçili: " + ", ".join(secilen))
    eksik = [k for k in kodlar if k not in secilen]
    if eksik:
        log("Uyarı: dump'ta bulunamayan ödeme tipi: " + ", ".join(eksik))
    return dump


def hex_metin(metin):
    if metin == "":
        return "''"
    return "CONVERT(0x%s USING utf8)" % metin.encode("utf-8").hex().upper()


def varsayilan_deger(s):
    if s["default"] is not None and s["default"].upper() != "NULL":
        d = s["default"]
        if d.upper() in ("CURRENT_TIMESTAMP", "NOW()"):
            return "NOW()"
        if d.startswith("'"):
            ic = d[1:-1].replace("''", "'")
            return hex_metin(ic) if any(ord(c) > 127 for c in ic) else d
        return d
    if not s["not_null"]:
        return "NULL"
    t = s["tip"]
    if any(x in t for x in SAYI):
        return "0"
    if "datetime" in t or "timestamp" in t:
        return "NOW()"
    if "date" in t:
        return "CURDATE()"
    if "time" in t:
        return "'00:00:00'"
    return "''"


def sayi(deger):
    d = f"{float(deger):.2f}".rstrip("0").rstrip(".")
    return d or "0"


def resim_hex(yol):
    """600x600 JPEG, 70 KB altına sıkıştırıp 0x... döner."""
    if not yol or not os.path.exists(yol):
        return "NULL"
    img = Image.open(yol).convert("RGB")
    if img.size != (600, 600):
        tuval = Image.new("RGB", (600, 600), (255, 255, 255))
        img.thumbnail((600, 600), Image.LANCZOS)
        tuval.paste(img, ((600 - img.width) // 2, (600 - img.height) // 2))
        img = tuval
    for kalite in (85, 78, 70, 62, 55, 48, 40, 32, 25, 18, 12):
        b = io.BytesIO()
        img.save(b, "JPEG", quality=kalite, optimize=True)
        if b.tell() <= MAKS_RESIM:
            break
    return "0x" + b.getvalue().hex().upper()


def insert_satiri(tablo, sutunlar, degerler):
    adlar = ",".join(f"`{s['ad']}`" for s in sutunlar)
    vals = ",".join(degerler[s["ad"]] if s["ad"] in degerler else varsayilan_deger(s)
                    for s in sutunlar)
    return f"INSERT INTO `{tablo}` ({adlar}) VALUES ({vals});"


# ------------------------------------------------------------ eski satırlardaki Türkçe

_LITERAL = re.compile(r"'((?:[^'\\]|\\.|'')*)'")
_KACIS = {"0": "\0", "n": "\n", "r": "\r", "t": "\t", "Z": "\x1a", "b": "\b"}


def _coz(ic):
    ic = ic.replace("''", "'")
    return re.sub(r"\\(.)", lambda m: _KACIS.get(m.group(1), m.group(1)), ic, flags=re.S)


def eski_insertleri_duzelt(dump):
    """Dump'taki tüm INSERT'lerde ASCII olmayan metinleri CONVERT(0x..) yapar."""
    def satir_duzelt(satir):
        if not satir.lstrip().upper().startswith("INSERT INTO"):
            return satir
        return _LITERAL.sub(lambda m: hex_metin(_coz(m.group(1)))
                            if any(ord(c) > 127 for c in m.group(1)) else m.group(0), satir)
    return "\n".join(satir_duzelt(s) for s in dump.split("\n"))


# ------------------------------------------------------------ veri bölümünü doldurma

def _bolumu_doldur(dump, tablo, satirlar):
    # Eski INSERT'leri sil
    dump = re.sub(r"^INSERT INTO `?%s`?\s.*?;\s*$\n?" % re.escape(tablo), "", dump,
                  flags=re.M | re.I)
    blok = "\n".join(satirlar) + "\n"
    baslik = re.search(r'^#\s*Data for table [`"]?%s[`"]?\s*\n#\s*\n\n?' % re.escape(tablo),
                       dump, re.M | re.I)
    if baslik:
        return dump[:baslik.end()] + blok + dump[baslik.end():]
    # Başlık yoksa CREATE TABLE'dan hemen sonra ekle
    ct = re.search(r"CREATE TABLE\s+`?%s`?\s*\(.*?\n\)\s*[^;]*;\n" % re.escape(tablo), dump, re.S | re.I)
    if not ct:
        raise ValueError(f"'{tablo}' için ekleme yeri bulunamadı.")
    ek = f"\n#\n# Data for table \"{tablo}\"\n#\n\n" + blok
    return dump[:ct.end()] + ek + dump[ct.end():]


def firma_adi_bul(dump_yolu):
    """SQL yedeğindeki tbl_ayar satırından firma adını okur."""
    try:
        dump, _ = dump_oku(dump_yolu)
    except Exception:
        return ""
    m = re.search(r"INSERT INTO `?tbl_ayar`?\s+VALUES\s*\(\s*[^,]*,\s*'((?:[^'\\]|\\.|'')*)'", dump)
    if m:
        return guvenli_ad(_coz(m.group(1)))
    m = re.search(r"INSERT INTO `?tbl_ayar`?\s+VALUES\s*\(\s*[^,]*,\s*CONVERT\(0x([0-9A-Fa-f]+)", dump)
    if m:
        try:
            return guvenli_ad(bytes.fromhex(m.group(1)).decode("utf-8"))
        except ValueError:
            pass
    return ""


def resim_bul(klasor, ad):
    """Klasörde ürünün resmini bulur (jpg/png/webp..., büyük-küçük harf fark etmez)."""
    for aday in (f"{ad}.jpg", f"{ad}_1.jpg"):
        yol = os.path.join(klasor, aday)
        if os.path.exists(yol):
            return yol
    if not os.path.isdir(klasor):
        return None
    hedef = {ad.lower(), f"{ad}_1".lower()}
    for dosya in sorted(os.listdir(klasor)):
        kok, uz = os.path.splitext(dosya)
        if uz.lower() in RESIM_UZANTI and kok.lower() in hedef:
            return os.path.join(klasor, dosya)
    return None


def _emojisiz(metin):
    # utf8 (MySQL 5.5) 4 baytlık karakterleri (emoji) saklayamaz
    return re.sub(r"\s+", " ", "".join(c for c in metin if ord(c) <= 0xFFFF
                                       and not 0xFE00 <= ord(c) <= 0xFE0F)).strip()


def klasoru_tara(kok):
    """
    Hazır resim klasörünü (veya .zip) okur:
        kok/Kategori/[Pişme Şekli/[Menü/]]Ürün Adı.jpg
    Dönen değer listeyi_ayristir() ile aynı yapıda + gerçek klasör yolları:
        ({"Kategori/...": [(ad, ad, fiyat), ...]}, {"Kategori/...": gerçek_klasör})
    Kökteki resimler 'Genel' kategorisine girer.
    """
    if kok.lower().endswith(".zip"):
        import tempfile
        import zipfile
        gecici = tempfile.mkdtemp(prefix="menu_")
        with zipfile.ZipFile(kok) as z:
            for bilgi in z.infolist():
                ad = bilgi.filename
                if not (bilgi.flag_bits & 0x800):      # Türkçe adlı eski zip'ler
                    try:
                        ad = ad.encode("cp437").decode("cp857")
                    except (UnicodeEncodeError, UnicodeDecodeError):
                        pass
                hedef = os.path.normpath(os.path.join(gecici, ad))
                if not hedef.startswith(gecici):
                    continue
                if bilgi.is_dir():
                    os.makedirs(hedef, exist_ok=True)
                    continue
                os.makedirs(os.path.dirname(hedef), exist_ok=True)
                with z.open(bilgi) as kaynak, open(hedef, "wb") as f:
                    f.write(kaynak.read())
        kok = gecici
        # zip tek bir üst klasör içeriyorsa onun içine gir
        icerik = [x for x in os.listdir(kok) if not x.startswith(("__MACOSX", "."))]
        if len(icerik) == 1 and os.path.isdir(os.path.join(kok, icerik[0])):
            kok = os.path.join(kok, icerik[0])

    kategoriler, gercek = {}, {}
    for klasor, alt, dosyalar in os.walk(kok):
        alt[:] = sorted(a for a in alt if not a.startswith(("__MACOSX", ".")))
        resimler = [d for d in sorted(dosyalar)
                    if os.path.splitext(d)[1].lower() in RESIM_UZANTI and not d.startswith(".")]
        if not resimler:
            continue
        goreli = os.path.relpath(klasor, kok)
        parcalar = [] if goreli == "." else [_emojisiz(p) or "Genel" for p in goreli.split(os.sep)]
        yol = os.path.join(*parcalar) if parcalar else "Genel"
        liste = kategoriler.setdefault(yol, [])
        gercek[yol] = klasor
        for d in resimler:
            ad_ham = os.path.splitext(d)[0]
            ad, _, fiyat, _p = satiri_coz(ad_ham)
            ad = _emojisiz(ad)
            if ad and ad.lower() not in [u[0].lower() for u in liste]:
                liste.append((ad, ad_ham, fiyat, []))
    return {k: porsiyon_grupla(v) for k, v in kategoriler.items()}, gercek


def sql_olustur(dump_yolu, kategoriler, resim_kok, cikti_yolu, fiyat=1.0, kdv=10,
                kategori_seviye=1, elle=None, log=print, gercek_klasorler=None, ek_odemeler=()):
    """
    kategoriler: listeyi_ayristir() çıktısı {"Döner/Izgara/Menü": [(ad, arama), ...]}
    kategori_seviye: SQL kategorisi klasörün kaçıncı seviyesinden alınır
        1 = sadece "Döner", 3 = "Döner Izgara Menü"
    """
    if fiyat <= 0:
        raise ValueError("Fiyat 0 veya eksi olamaz (yazar kasa kabul etmez).")
    elle = elle or {}
    dump, crlf = dump_oku(dump_yolu)
    k_sut = sutunlari_bul(dump, KATEGORI_TABLO)
    u_sut = sutunlari_bul(dump, URUN_TABLO)
    k_es = eslestir(k_sut, False, elle.get(KATEGORI_TABLO))
    u_es = eslestir(u_sut, True, elle.get(URUN_TABLO))
    log(f"{KATEGORI_TABLO} eşleşme: {k_es}")
    log(f"{URUN_TABLO} eşleşme: {u_es}")
    for gerekli, es, tablo in (("ad", k_es, KATEGORI_TABLO), ("ad", u_es, URUN_TABLO),
                               ("kategori", u_es, URUN_TABLO)):
        if not es.get(gerekli):
            raise ValueError(f"{tablo} içinde '{gerekli}' sütunu bulunamadı; "
                             f"ayarlar.json > sql_sutunlar ile belirt.")
    sabit = elle.get("sabitler", {})

    # Kategorileri sırayla topla
    kat_sira, kat_urunler = [], {}
    for yol, urunler in kategoriler.items():
        parca = yol.split(os.sep)
        kat_ad = " ".join(parca[:kategori_seviye])
        if kat_ad not in kat_urunler:
            kat_sira.append(kat_ad)
            kat_urunler[kat_ad] = []
        klasor_yolu = (gercek_klasorler or {}).get(yol) or os.path.join(resim_kok, yol)
        kat_urunler[kat_ad].extend(
            (u[0], klasor_yolu, u[2] if len(u) > 2 else None,
             u[1] if gercek_klasorler else u[0],            # resim dosyasının adı
             u[3] if len(u) > 3 else [])                    # porsiyonlar
            for u in urunler)

    k_satir, u_satir, uid, eksik, fiyatli = [], [], 0, [], 0
    kc_satir, c_satir, kcid, cid = [], [], 0, 0
    for kid, kat_ad in enumerate(kat_sira, 1):
        ilk_resim = next((resim_bul(k, r) for _, k, _, r, _p in kat_urunler[kat_ad] if resim_bul(k, r)), None)
        d = {k_es["id"]: str(kid), k_es["ad"]: hex_metin(tr_buyuk(kat_ad))}
        if k_es.get("sira"):
            d[k_es["sira"]] = str(kid)
        if k_es.get("kdv"):
            d[k_es["kdv"]] = sayi(kdv)
        if k_es.get("resim"):
            d[k_es["resim"]] = resim_hex(ilk_resim)
        d.update({k: v for k, v in sabit.get(KATEGORI_TABLO, {}).items()})
        k_satir.append(insert_satiri(KATEGORI_TABLO, k_sut, d))

        kat_sira_no = 0
        kat_pors = {}                                  # porsiyon adı → kategoricarpanid
        for ad, klasor, u_fiyat, r_ad, u_pors in kat_urunler[kat_ad]:
            uid += 1
            kat_sira_no += 1
            if u_fiyat is not None and u_fiyat > 0:
                fiyatli += 1
            else:
                u_fiyat = fiyat
            r = resim_bul(klasor, r_ad)
            if not r:
                eksik.append(ad)
            d = {u_es["id"]: str(uid), u_es["ad"]: hex_metin(tr_buyuk(ad)), u_es["kategori"]: str(kid)}
            if u_es.get("fiyat"):
                d[u_es["fiyat"]] = sayi(u_fiyat)
            if u_es.get("sira"):
                d[u_es["sira"]] = str(kat_sira_no)   # her kategoride 1'den başlar
            if u_es.get("kdv"):
                d[u_es["kdv"]] = sayi(kdv)
            if u_es.get("resim"):
                d[u_es["resim"]] = resim_hex(r)
            d.update({k: v for k, v in sabit.get(URUN_TABLO, {}).items()})
            u_satir.append(insert_satiri(URUN_TABLO, u_sut, d))
            for p_ad, p_fiyat in u_pors:
                anahtar = _pors_norm(p_ad)
                if anahtar not in kat_pors:
                    kcid += 1
                    kat_pors[anahtar] = kcid
                    kc_satir.append(dict(kategoricarpanid=str(kcid), kategoriid=str(kid),
                                         kategoricarpantip="'Porsiyon'", kategoricarpanad=hex_metin(tr_buyuk(p_ad)),
                                         kategoricarpansira=str(len(kat_pors)),
                                         kategoricarpansil="'N'", kategoricarpanyeni="'N'"))
                cid += 1
                c_satir.append(dict(carpanid=str(cid), kategoricarpanid=str(kat_pors[anahtar]),
                                    kategoriid=str(kid), urunid=str(uid), carpantip="'Porsiyon'",
                                    carpanad=hex_metin(tr_buyuk(p_ad)), carpanfiyat=sayi(p_fiyat or u_fiyat),
                                    carpansil="'N'"))

    kodlar = ["NAKIT", "KREDIKARTI"] + [k for k in ek_odemeler if k not in ("NAKIT", "KREDIKARTI")]
    dump = odeme_tiplerini_sec(dump, tuple(kodlar), log=log)
    dump = eski_insertleri_duzelt(dump)
    dump = _bolumu_doldur(dump, KATEGORI_TABLO, k_satir)
    dump = _bolumu_doldur(dump, URUN_TABLO, u_satir)
    # Porsiyonlar (tbl_urunkategoricarpan + tbl_uruncarpan)
    sayac = [(KATEGORI_TABLO, len(kat_sira)), (URUN_TABLO, uid)]
    for tablo, satirlar, n in ((KAT_CARPAN_TABLO, kc_satir, kcid), (URUN_CARPAN_TABLO, c_satir, cid)):
        try:
            sut = sutunlari_bul(dump, tablo)
        except ValueError:
            if satirlar:
                log(f"Uyarı: dump'ta {tablo} yok, porsiyonlar yazılamadı.")
            continue
        adlar = {x["ad"] for x in sut}
        satirlar = [insert_satiri(tablo, sut, {k: v for k, v in r.items() if k in adlar})
                    for r in satirlar]
        dump = _bolumu_doldur(dump, tablo, satirlar)
        sayac.append((tablo, n))
    if cid:
        log(f"Porsiyonlar: {kcid} porsiyon tipi, {cid} ürün porsiyonu yazıldı.")
    # AUTO_INCREMENT sayaçlarını güncelle
    for tablo, n in sayac:
        dump = re.sub(r"(CREATE TABLE\s+`?%s`?\s*\(.*?\n\)[^;]*?AUTO_INCREMENT=)\d+" % re.escape(tablo),
                      lambda m: m.group(1) + str(n + 1), dump, flags=re.S | re.I)

    os.makedirs(os.path.dirname(os.path.abspath(cikti_yolu)), exist_ok=True)
    if crlf:
        dump = dump.replace("\n", "\r\n")
    kalan = sum(1 for c in dump if ord(c) > 127)
    with open(cikti_yolu, "w", encoding="cp1254", errors="replace", newline="") as f:
        f.write(dump)
    log(f"SQL yazıldı: {cikti_yolu}  ({len(kat_sira)} kategori, {uid} ürün, "
        f"{fiyatli} ürün menü fiyatıyla, {uid - fiyatli} ürün {sayi(fiyat)} TL)")
    if kalan:
        log(f"Not: INSERT dışındaki yorum satırlarında {kalan} Türkçe karakter kaldı (önemsiz).")
    if eksik:
        log(f"Resmi olmayan {len(eksik)} ürün (urunresim NULL): " + ", ".join(eksik[:15])
            + (" ..." if len(eksik) > 15 else ""))
    return len(kat_sira), uid, eksik


# ---------------------------------------------------------------- SiparişMatik Pro (siparismatik.db)
#
# Pro, menüyü SQLite (siparismatik.db) içinde tutar:
#   categories / products (porsiyonlar products.variants JSON'u: [{"name": "1,5", "price": fark}])
#   detaylar + urun_detaylar (opsiyonlar: soğansız, acısız ...)
#   resimler veritabanında değil, AppData\Roaming\siparismatik-pro\product-images\ klasöründe;
#   products.image / categories.image o dosyanın TAM yolunu tutar.
# Panel, silinen eski kayıtların yerel id'lerini hatırlar; bu yüzden id'ler sqlite_sequence'den
# devam eder (eski id tekrar kullanılmaz) ve updated_at "şimdi" yazılır.

PRO_VERI_KLASORU = r"C:\Users\{kullanici}\AppData\Roaming\siparismatik-pro"
PRO_OPSIYON_VARSAYILAN = ("Soğanlı, Soğansız, Domatesli, Domatessiz, Marullu, Marulsuz, Soslu, Sossuz, "
                          "Patatesli, Patatessiz, Turşulu, Turşusuz, Mayonezli, Mayonezsiz, "
                          "Ketçaplı, Ketçapsız, Acılı, Acısız")


def _pro_simdi():
    import datetime
    return datetime.datetime.now(datetime.timezone.utc).strftime("%Y-%m-%dT%H:%M:%S.%f")[:-3] + "Z"


def pro_db_kopyala(kaynak, hedef):
    """siparismatik.db'yi (yanındaki -wal/-shm ile) kopyalar, WAL'i dosyaya işler, tek dosya bırakır."""
    import shutil
    import sqlite3
    os.makedirs(os.path.dirname(os.path.abspath(hedef)), exist_ok=True)
    for ek in ("-wal", "-shm"):
        if os.path.exists(hedef + ek):
            os.remove(hedef + ek)
    shutil.copy2(kaynak, hedef)
    for ek in ("-wal", "-shm"):
        if os.path.exists(kaynak + ek):
            shutil.copy2(kaynak + ek, hedef + ek)
    c = sqlite3.connect(hedef)
    c.execute("PRAGMA wal_checkpoint(TRUNCATE)")
    c.execute("PRAGMA journal_mode=DELETE")
    c.commit()
    if c.execute("PRAGMA integrity_check").fetchone()[0] != "ok":
        c.close()
        raise ValueError("Pro veritabanı bozuk görünüyor (integrity_check).")
    return c


def pro_firma_adi(db_yolu):
    import sqlite3
    try:
        c = sqlite3.connect(f"file:{db_yolu}?mode=ro", uri=True)
        for k in ("sm_firma_adi", "panel_firma_adi", "isletme_adi"):
            r = c.execute("SELECT value FROM settings WHERE key=?", (k,)).fetchone()
            if r and str(r[0]).strip():
                return guvenli_ad(str(r[0]).strip())
    except Exception:
        pass
    return ""


def _pro_tablolar(c):
    return {r[0] for r in c.execute("SELECT name FROM sqlite_master WHERE type='table'")}


def pro_demo_temizle(c, log=print):
    """
    Pro'nun ilk kurulumda gelen örnek verilerini siler. Örnek kayıtlar kurulumun tohum zamanıyla
    (yönetici kullanıcısının created_at'i) yazılır; siparişler ondan öncesine tarihlidir.
    Yönetici kullanıcısına ve PIN'ine dokunulmaz.
    """
    t = _pro_tablolar(c)
    r = c.execute("SELECT created_at FROM users ORDER BY id LIMIT 1").fetchone()
    if not r or not r[0]:
        log("Demo temizliği: tohum zamanı bulunamadı, atlandı.")
        return {}
    tohum = r[0]
    sayac = {}

    def sil(tablo, kosul, p=()):
        if tablo in t:
            n = c.execute(f"DELETE FROM {tablo} WHERE {kosul}", p).rowcount
            if n:
                sayac[tablo] = sayac.get(tablo, 0) + n

    demo_sip = [x[0] for x in c.execute("SELECT id FROM orders WHERE created_at < ?", (tohum,))]
    if demo_sip:
        yer = ",".join("?" * len(demo_sip))
        for tablo in sorted(t):
            if tablo == "orders":
                continue
            sutun = [x[1] for x in c.execute(f"PRAGMA table_info({tablo})")]
            if "order_id" in sutun:
                sil(tablo, f"order_id IN ({yer})", demo_sip)
        sil("orders", f"id IN ({yer})", demo_sip)
    sil("shifts", "baslangic < ?", (tohum,))
    sil("sms_history", "created_at < ?", (tohum,))
    demo_uye = [x[0] for x in c.execute("SELECT id FROM members WHERE created_at = ?", (tohum,))]
    if demo_uye:
        yer = ",".join("?" * len(demo_uye))
        sil("member_adresleri", f"member_id IN ({yer})", demo_uye)
        sil("members", f"id IN ({yer})", demo_uye)
    sil("firmalar", "created_at = ?", (tohum,))
    ilk = c.execute("SELECT id FROM users ORDER BY id LIMIT 1").fetchone()[0]
    sil("users", "created_at = ? AND id <> ? AND role <> 'Yönetici'", (tohum, ilk))
    # Örnek yazıcılar (Adisyon / Mutfak / Bar) ve onlara bağlı eşleşmeler
    if "printers" in t:
        if "categories" in t:
            c.execute("UPDATE categories SET printer_id=NULL, printer_ids=''")
        sil("yazici_eslesme", "1=1")
        sil("printers", "1=1")
    log("Demo temizliği: " + (", ".join(f"{k} {v}" for k, v in sayac.items()) or "silinecek kayıt yok")
        + "  (yönetici hesabına dokunulmadı)")
    return sayac


def _pro_menuyu_bosalt(c, log):
    """Var olan menüyü siler; panelin de silmesi için silinen_kayitlar'a yazar."""
    simdi = _pro_simdi()
    t = _pro_tablolar(c)
    n_u = n_k = 0
    for tur, tablo in (("urun", "products"), ("kategori", "categories")):
        for kid, rid in c.execute(f"SELECT id, COALESCE(remote_id,0) FROM {tablo}").fetchall():
            if "silinen_kayitlar" in t:
                c.execute("INSERT INTO silinen_kayitlar (tur, kayit_id, remote_id, silindi_at, gonderildi_at) "
                          "VALUES (?,?,?,?, '')", (tur, kid, rid, simdi))
        n = c.execute(f"DELETE FROM {tablo}").rowcount
        if tur == "urun":
            n_u = n
        else:
            n_k = n
    for tablo in ("urun_detaylar", "kategori_detaylar"):
        if tablo in t:
            c.execute(f"DELETE FROM {tablo}")
    if "yazici_eslesme" in t:
        c.execute("DELETE FROM yazici_eslesme WHERE hedef_tip='kategori'")
    if n_u or n_k:
        log(f"Eski menü silindi: {n_k} kategori, {n_u} ürün (panele silme olarak gönderilecek).")


def _pro_detay_id(c, ad, simdi):
    ad = tr_buyuk(ad.strip())[:40]
    r = c.execute("SELECT id FROM detaylar WHERE lower(name)=lower(?)", (ad,)).fetchone()
    if r:
        return r[0]
    sira = (c.execute("SELECT COALESCE(MAX(sira),0) FROM detaylar").fetchone()[0] or 0) + 1
    return c.execute("INSERT INTO detaylar (name, sira, updated_at, remote_id) VALUES (?,?,?,0)",
                     (ad, sira, simdi)).lastrowid


def pro_db_olustur(sablon_db, kategoriler, resim_kok, cikti_klasor, fiyat=1.0, kdv=10,
                   kategori_seviye=1, log=print, gercek_klasorler=None, ek_odemeler=(),
                   kullanici="", opsiyonlar="", icecege_opsiyon=False, demo_temizle=True):
    """
    Menüyü Pro veritabanına yazar. Çıktı:
        cikti_klasor/siparismatik.db
        cikti_klasor/product-images/*.jpg
    Müşteri PC'sinde ikisi de C:\\Users\\<kullanici>\\AppData\\Roaming\\siparismatik-pro\\ içine konur.
    """
    import shutil
    if fiyat <= 0:
        raise ValueError("Fiyat 0 veya eksi olamaz (yazar kasa kabul etmez).")
    kullanici = (kullanici or "").strip()
    if not kullanici:
        raise ValueError("Kurulacak bilgisayarın Windows kullanıcı adı boş olamaz (resim yolları için).")
    hedef_db = os.path.join(cikti_klasor, "siparismatik.db")
    r_klasor = os.path.join(cikti_klasor, "product-images")
    if os.path.isdir(r_klasor):
        shutil.rmtree(r_klasor)
    os.makedirs(r_klasor)
    c = pro_db_kopyala(sablon_db, hedef_db)
    try:
        t = _pro_tablolar(c)
        for gerekli in ("categories", "products", "detaylar", "urun_detaylar", "settings"):
            if gerekli not in t:
                raise ValueError(f"Seçilen dosya SiparişMatik Pro veritabanı değil ({gerekli} tablosu yok).")
        if demo_temizle:
            pro_demo_temizle(c, log)
        _pro_menuyu_bosalt(c, log)
        simdi = _pro_simdi()
        win_klasor = PRO_VERI_KLASORU.format(kullanici=kullanici) + "\\product-images\\"
        damga = int(time.time() * 1000)

        def resim_kopyala(kaynak, ad):
            if not kaynak:
                return ""
            dosya = f"prod_{damga}_{ad}.jpg"
            with open(kaynak, "rb") as f:
                veri = f.read()
            hedef = os.path.join(r_klasor, dosya)
            if not kare_jpeg_kaydet(veri, hedef):
                # küçük resim (200 px altı) de kullanılır: büyütülüp 600x600 yapılır
                try:
                    img = Image.open(io.BytesIO(veri))
                    img.load()
                    if img.mode in ("RGBA", "LA", "P"):
                        img = img.convert("RGBA")
                        duz = Image.new("RGB", img.size, (255, 255, 255))
                        duz.paste(img, mask=img.split()[-1])
                        img = duz
                    img = img.convert("RGB")
                    oran = min(BOYUT / img.width, BOYUT / img.height)
                    yeni = (max(1, round(img.width * oran)), max(1, round(img.height * oran)))
                    tuval = Image.new("RGB", (BOYUT, BOYUT), (255, 255, 255))
                    tuval.paste(img.resize(yeni, Image.LANCZOS), ((BOYUT - yeni[0]) // 2, (BOYUT - yeni[1]) // 2))
                    tuval.save(hedef, "JPEG", quality=90, optimize=True)
                except Exception:
                    return ""
            return win_klasor + dosya

        # Kategorileri topla (sql_olustur ile aynı kural)
        kat_sira, kat_urunler = [], {}
        for yol, urunler in kategoriler.items():
            kat_ad = " ".join(yol.split(os.sep)[:kategori_seviye])
            if kat_ad not in kat_urunler:
                kat_sira.append(kat_ad)
                kat_urunler[kat_ad] = []
            klasor_yolu = (gercek_klasorler or {}).get(yol) or os.path.join(resim_kok, yol)
            kat_urunler[kat_ad].extend(
                (u[0], klasor_yolu, u[2] if len(u) > 2 else None,
                 u[1] if gercek_klasorler else u[0], u[3] if len(u) > 3 else [])
                for u in urunler)

        detay_idler = [_pro_detay_id(c, o, simdi)
                       for o in re.split(r"[,;\n]", opsiyonlar or "") if o.strip()]
        n_urun, fiyatli, eksik, n_var, n_bag = 0, 0, [], 0, 0
        for ks, kat_ad in enumerate(kat_sira, 1):
            ilk = next((resim_bul(k, r) for _, k, _, r, _p in kat_urunler[kat_ad] if resim_bul(k, r)), None)
            cid = c.execute(
                "INSERT INTO categories (name, image, kdv, sort_order, remote_id, kiosk_visible, kiosk_sort, "
                "updated_at) VALUES (?,?,?,?,0,1,?,?)",
                (tr_buyuk(kat_ad), resim_kopyala(ilk, f"k{ks}"), int(round(kdv)), ks, ks, simdi)).lastrowid
            icecek = "ICECEK" in _norm(kat_ad).upper().replace(" ", "")
            for us, (ad, klasor, u_fiyat, r_ad, u_pors) in enumerate(kat_urunler[kat_ad], 1):
                n_urun += 1
                if u_fiyat is not None and u_fiyat > 0:
                    fiyatli += 1
                else:
                    u_fiyat = fiyat
                r = resim_bul(klasor, r_ad)
                if not r:
                    eksik.append(ad)
                varyant = []
                for p_ad, p_fiyat in u_pors:
                    p_fiyat = p_fiyat if p_fiyat and p_fiyat > 0 else u_fiyat
                    varyant.append({"name": tr_buyuk(p_ad)[:40], "price": round(p_fiyat - u_fiyat, 2)})
                n_var += len(varyant)
                pid = c.execute(
                    "INSERT INTO products (category_id, name, price, kdv, active, wp_show, image, sort_order, "
                    "variants, remote_id, kiosk_visible, salon_show, paket_show, updated_at) "
                    "VALUES (?,?,?,?,1,1,?,?,?,0,1,1,1,?)",
                    (cid, tr_buyuk(ad), float(u_fiyat), int(round(kdv)), resim_kopyala(r, f"u{n_urun}"), us,
                     json.dumps(varyant, ensure_ascii=False) if varyant else "", simdi)).lastrowid
                if detay_idler and (icecege_opsiyon or not icecek):
                    for d in detay_idler:
                        c.execute("INSERT OR IGNORE INTO urun_detaylar (product_id, detay_id) VALUES (?,?)", (pid, d))
                        n_bag += 1
        # Ödeme tipleri: Nakit + Kredi Kartı (+ menüde yazan yemek kartları) açık
        if "payment_methods" in t:
            c.execute("UPDATE payment_methods SET active=1 WHERE name IN ('Nakit','Kredi Kartı')")
            if any(k not in ("NAKIT", "KREDIKARTI") for k in ek_odemeler):
                c.execute("UPDATE payment_methods SET active=1 WHERE name LIKE 'Yemek Kart%'")
        c.commit()
        if c.execute("PRAGMA integrity_check").fetchone()[0] != "ok":
            raise ValueError("Yazılan veritabanı integrity_check'ten geçmedi.")
        # journal_mode DELETE kalır: tek dosya; Pro açılışta kendisi WAL'e geçer
    finally:
        c.close()
    for ek in ("-wal", "-shm"):
        if os.path.exists(hedef_db + ek):
            os.remove(hedef_db + ek)
    log(f"Pro veritabanı yazıldı: {hedef_db}  ({len(kat_sira)} kategori, {n_urun} ürün, "
        f"{n_var} porsiyon, {len(detay_idler)} opsiyon / {n_bag} bağ, "
        f"{fiyatli} ürün menü fiyatıyla, {n_urun - fiyatli} ürün {sayi(fiyat)} TL)")
    log(f"Resimler: {r_klasor}  → müşteride {PRO_VERI_KLASORU.format(kullanici=kullanici)}\\product-images")
    log("KURULUM: Pro'yu kapat → AppData\\Roaming\\siparismatik-pro içindeki siparismatik.db, "
        "siparismatik.db-wal ve siparismatik.db-shm'yi yedeğe al → bu siparismatik.db'yi ve "
        "product-images klasörünü oraya kopyala → Pro'yu aç.")
    if eksik:
        log(f"Resmi olmayan {len(eksik)} ürün: " + ", ".join(eksik[:15]) + (" ..." if len(eksik) > 15 else ""))
    return len(kat_sira), n_urun, eksik


def pro_demo_temizle_dosya(db_yolu, log=print):
    """Kurulu bir Pro veritabanında yerinde demo temizliği (önce yedek alır). Pro kapalı olmalı."""
    import shutil
    import sqlite3
    if os.path.exists(db_yolu + "-wal") and os.path.getsize(db_yolu + "-wal") > 0:
        log("Uyarı: yanında dolu -wal dosyası var; Pro açık olabilir. Önce Pro'yu kapat.")
    yedek = db_yolu + time.strftime(".yedek_%Y%m%d_%H%M%S")
    shutil.copy2(db_yolu, yedek)
    if os.path.exists(db_yolu + "-wal"):
        shutil.copy2(db_yolu + "-wal", yedek + "-wal")
    c = sqlite3.connect(db_yolu)
    try:
        c.execute("PRAGMA busy_timeout=3000")
        pro_demo_temizle(c, log)
        c.commit()
    finally:
        c.close()
    log(f"Yedek: {yedek}")


# ---------------------------------------------------------------- pencere

def pencere():
    import tkinter as tk
    from tkinter import filedialog, messagebox, scrolledtext, simpledialog

    ayar = ayar_oku()
    kok = tk.Tk()
    kok.title(f"Ürün Resim İndirici v{SURUM} (600x600 JPEG)")
    # Küçük ekranlarda (ör. 1366x768) pencere ekrandan taşmasın
    kok.geometry(f"900x{max(600, min(1060, kok.winfo_screenheight() - 80))}+20+10")

    # --- müşteri + menü okuma
    m_cer = tk.LabelFrame(kok, text=" 1) Müşteri ve menü ")
    m_cer.pack(fill="x", padx=10, pady=(10, 4))
    tk.Label(m_cer, text="Firma adı:").grid(row=0, column=0, sticky="w", padx=4)
    musteri_var = tk.StringVar()
    tk.Entry(m_cer, textvariable=musteri_var, width=40).grid(row=0, column=1, sticky="w")
    tk.Label(m_cer, text="(boşsa SQL'deki firma adı kullanılır; sadece SQL klasörü için)",
             fg="#777").grid(row=2, column=1, columnspan=3, sticky="w")

    tk.Label(m_cer, text="Menü okuyucu:").grid(row=1, column=0, sticky="w", padx=4)
    saglayici_var = tk.StringVar(value=ayar.get("saglayici", "gemini"))
    sf = tk.Frame(m_cer)
    sf.grid(row=1, column=1, columnspan=3, sticky="w")
    tk.Radiobutton(sf, text="Gemini (ücretsiz, internet)", value="gemini",
                   variable=saglayici_var).pack(side="left")
    tk.Radiobutton(sf, text="Ollama (ücretsiz, bilgisayarda, internetsiz)", value="ollama",
                   variable=saglayici_var).pack(side="left", padx=8)

    def api_key_al():
        if saglayici_var.get() == "ollama":
            return "-"
        key = ayar.get("gemini_key") or os.environ.get("GEMINI_API_KEY", "")
        if not key:
            key = simpledialog.askstring(
                "Gemini API anahtarı (ücretsiz)",
                "aistudio.google.com/apikey adresine Google hesabınla gir,\n"
                "'Create API key' de ve anahtarı buraya yapıştır.\n"
                "Kart bilgisi gerekmez. Bir kez girmen yeterli:", show="*")
            if key:
                ayar["gemini_key"] = key.strip()
                ayar_yaz(ayar)
        return (key or "").strip()

    brosur_kirp = {}          # ürün adı → (broşür dosyası, kutu); 'İndir'de fotoğraf buradan kesilir

    son_dosyalar = []         # son seçilen broşür resimleri ("Broşürle birleştir" tekrar kullanır)

    def menu_oku(yollar):
        dosyalar = menu_dosyalarini_topla(yollar)
        if not dosyalar:
            messagebox.showwarning("Dosya yok", "Resim (jpg/png/webp) veya PDF bulunamadı.")
            return
        son_dosyalar[:] = dosyalar
        key = api_key_al()
        if not key:
            return
        for b in (btn_resim, btn_klasor, btn, btn_birlestir):
            b.configure(state="disabled")
        saglayici = saglayici_var.get()
        ayar["saglayici"] = saglayici
        ayar_yaz(ayar)
        model = ayar.get("gemini_model" if saglayici == "gemini" else "ollama_model", "")
        # Yazılı liste: "Müşterinin yazılı listesi" kutusu; o boşsa ve büyük kutuya
        # başlıksız düz liste yapıştırılmışsa o kullanılır.
        liste_metni = liste_kutu.get("1.0", "end").strip()
        if not liste_metni:
            mevcut = kutu.get("1.0", "end").strip()
            if mevcut and basliksiz_mi(mevcut):
                liste_metni = mevcut
        if liste_metni:
            log("Yazılı liste broşürle karşılaştırılacak.")
        log(f"{len(dosyalar)} menü dosyası okunuyor ({saglayici})...")

        def is_parcacigi():
            try:
                farklar, eksik = [], []
                if saglayici == "gemini":
                    metin, kirp, farklar = brosur_oku(dosyalar, liste_metni, key, model, log)
                    if liste_metni:
                        eksik = liste_eksik_kontrol(liste_metni, metin)
                else:
                    metin, kirp = menuyu_oku(dosyalar, saglayici, key, model, log), {}
                    if liste_metni:
                        metin = liste_metni + "\n\n" + metin
                brosur_kirp.clear()
                brosur_kirp.update(kirp)

                def yaz():
                    kutu.delete("1.0", "end")
                    kutu.insert("1.0", metin)
                    sayi = sum(len(v) for v in listeyi_ayristir(metin).values())
                    log(f"Menü okundu: {sayi} ürün, {len(kirp)} ürünün fotoğrafı broşürden alınacak.")
                    for f in farklar:
                        log("  • " + f)
                    if eksik:
                        log("  ! Listede olup sonuçta bulamadığım satırlar (kontrol et, gerekirse elle ekle): "
                            + ", ".join(eksik))
                    log("Listeyi kontrol et, sonra 'İndir'e bas.\n")
                kok.after(0, yaz)
            except Exception as e:
                msg = str(e)
                if "API_KEY_HATALI" in msg:
                    ayar.pop("gemini_key", None)
                    ayar_yaz(ayar)
                    msg += "\nAPI anahtarı hatalı, bir dahaki sefere tekrar sorulacak."
                log(f"HATA: {msg}")
            finally:
                kok.after(0, lambda: [b.configure(state="normal")
                                      for b in (btn_resim, btn_klasor, btn, btn_birlestir)])
        threading.Thread(target=is_parcacigi, daemon=True).start()

    btn_resim = tk.Button(m_cer, text="Menü resimleri / PDF seç...",
                          command=lambda: (lambda y: y and menu_oku(list(y)))(
                              filedialog.askopenfilenames(filetypes=[
                                  ("Menü", "*.jpg *.jpeg *.png *.webp *.pdf *.bmp"),
                                  ("Hepsi", "*.*")])))
    btn_resim.grid(row=0, column=2, padx=6, pady=4)
    btn_klasor = tk.Button(m_cer, text="Menü klasörü seç...",
                           command=lambda: (lambda y: y and menu_oku([y]))(filedialog.askdirectory()))
    btn_klasor.grid(row=0, column=3, padx=2, pady=4)

    tk.Label(m_cer, text="Müşterinin yazılı listesi (varsa buraya yapıştır — broşürle karşılaştırılır, "
                         "fiyatlarda bu esas alınır):").grid(row=3, column=0, columnspan=4, sticky="w",
                                                             padx=4, pady=(6, 0))
    liste_kutu = scrolledtext.ScrolledText(m_cer, height=4, font=("Consolas", 9))
    liste_kutu.grid(row=4, column=0, columnspan=4, sticky="we", padx=4)
    m_cer.columnconfigure(1, weight=1)

    def brosurle_birlestir():
        if not liste_kutu.get("1.0", "end").strip():
            messagebox.showinfo("Birleştir", "Önce müşterinin yazılı listesini yukarıdaki kutuya yapıştır.")
            return
        if son_dosyalar:
            menu_oku(list(son_dosyalar))
            return
        y = filedialog.askopenfilenames(title="Broşür resimlerini seç", filetypes=[
            ("Menü", "*.jpg *.jpeg *.png *.webp *.pdf *.bmp"), ("Hepsi", "*.*")])
        if y:
            menu_oku(list(y))

    lb1 = tk.Frame(m_cer)
    lb1.grid(row=5, column=0, columnspan=4, sticky="we", padx=4, pady=(2, 4))
    btn_birlestir = tk.Button(lb1, text="Broşürle birleştir", command=brosurle_birlestir)
    btn_birlestir.pack(side="left")
    tk.Button(lb1, text="Temizle", command=lambda: liste_kutu.delete("1.0", "end")).pack(side="left", padx=6)
    tk.Label(lb1, text="Sıra fark etmez: broşürü önce seçtiysen listeyi yapıştırıp 'Broşürle birleştir'e bas.",
             fg="#777").pack(side="left", padx=6)

    # --- liste
    l_cer = tk.LabelFrame(kok, text=" 2) Ürün listesi  ( # Kategori   ## Pişme Şekli   ### Menü / Sadece Yemek   Ürün | arama ) ")
    l_cer.pack(fill="both", expand=True, padx=10, pady=4)
    kutu = scrolledtext.ScrolledText(l_cer, height=11, font=("Consolas", 10))
    kutu.pack(fill="both", expand=True, padx=4, pady=4)

    def dosyadan_yukle():
        yol = filedialog.askopenfilename(filetypes=[("Metin", "*.txt"), ("Hepsi", "*.*")])
        if yol:
            with open(yol, encoding="utf-8", errors="ignore") as f:
                kutu.delete("1.0", "end")
                kutu.insert("1.0", f.read())

    def listeyi_kaydet():
        yol = filedialog.asksaveasfilename(defaultextension=".txt",
                                           initialfile=(musteri_var.get() or "liste") + ".txt")
        if yol:
            with open(yol, "w", encoding="utf-8") as f:
                f.write(kutu.get("1.0", "end"))

    lb = tk.Frame(l_cer)
    lb.pack(fill="x", padx=4, pady=(0, 4))
    tk.Button(lb, text="Dosyadan yükle", command=dosyadan_yukle).pack(side="left")
    tk.Button(lb, text="Listeyi kaydet", command=listeyi_kaydet).pack(side="left", padx=6)
    tk.Button(lb, text="Temizle", command=lambda: kutu.delete("1.0", "end")).pack(side="left")

    # --- ayarlar
    a_cer = tk.LabelFrame(kok, text=" 3) İndir ")
    a_cer.pack(fill="x", padx=10, pady=4)
    tk.Label(a_cer, text="Ana klasör:").grid(row=0, column=0, sticky="w", padx=4)
    cikti_var = tk.StringVar(value=ayar.get("cikti", os.path.join(KLASOR, "Urun_Resimleri")))
    tk.Entry(a_cer, textvariable=cikti_var, width=60).grid(row=0, column=1, sticky="we")
    tk.Button(a_cer, text="Seç...", command=lambda: cikti_var.set(
        filedialog.askdirectory() or cikti_var.get())).grid(row=0, column=2, padx=4)

    tk.Label(a_cer, text="Aramaya ek:").grid(row=1, column=0, sticky="w", padx=4)
    ek_var = tk.StringVar(value=ayar.get("ek", ""))
    tk.Entry(a_cer, textvariable=ek_var, width=60).grid(row=1, column=1, sticky="we")
    tk.Label(a_cer, text="(ör. beyaz arka plan)").grid(row=1, column=2, sticky="w")

    tk.Label(a_cer, text="Ürün başına resim:").grid(row=2, column=0, sticky="w", padx=4)
    adet_var = tk.IntVar(value=ayar.get("adet", 1))
    tk.Spinbox(a_cer, from_=1, to=10, textvariable=adet_var, width=5).grid(row=2, column=1, sticky="w")
    klasor_arama_var = tk.BooleanVar(value=ayar.get("klasor_arama", False))
    cf = tk.Frame(a_cer)
    cf.grid(row=3, column=0, columnspan=3, sticky="w")
    tk.Checkbutton(cf, text="Klasör adlarını aramaya ekle",
                   variable=klasor_arama_var).pack(side="left")
    yenile_var = tk.BooleanVar(value=False)
    tk.Checkbutton(cf, text="Var olan resimleri silip yeniden indir",
                   variable=yenile_var).pack(side="left", padx=12)
    a_cer.columnconfigure(1, weight=1)

    log_kutu = scrolledtext.ScrolledText(kok, height=9, font=("Consolas", 9), state="disabled")

    def log(msg):
        def yaz():
            log_kutu.configure(state="normal")
            log_kutu.insert("end", msg + "\n")
            log_kutu.see("end")
            log_kutu.configure(state="disabled")
        kok.after(0, yaz)

    def hedef_klasor():
        """Ürün resimleri: tüm firmalar için ortak kütüphane (firma adı yok)."""
        return cikti_var.get()

    def firma_klasoru():
        """SQL ve firmaya özel dosyalar: Firmalar/<Firma adı>/"""
        ad = guvenli_ad(musteri_var.get())
        if not ad and pro_mu() and os.path.isfile(pro_var.get()):
            ad = pro_firma_adi(pro_var.get())
        elif not ad and os.path.isfile(sql_var.get()):
            ad = firma_adi_bul(sql_var.get())
            if ad:
                kok.after(0, lambda: musteri_var.set(ad))
        return os.path.join(ayar.get("firmalar", os.path.join(KLASOR, "Firmalar")), ad or "Firmasiz")

    def baslat():
        metin = kutu.get("1.0", "end")
        if not listeyi_ayristir(metin):
            messagebox.showwarning("Liste boş", "Önce menüyü okut veya ürün listesini yapıştır.")
            return
        duzenle = basliksiz_mi(metin)
        saglayici = saglayici_var.get()
        # Anahtar sorulmaz: varsa yapay zekâ, yoksa yerleşik sınıflandırıcı kullanılır
        key = ""
        if duzenle:
            key = "-" if saglayici == "ollama" else (
                ayar.get("gemini_key") or os.environ.get("GEMINI_API_KEY", ""))
        model = ayar.get("gemini_model" if saglayici == "gemini" else "ollama_model", "")
        ayar.update(cikti=cikti_var.get(), ek=ek_var.get(), adet=adet_var.get(),
                    klasor_arama=klasor_arama_var.get(), saglayici=saglayici)
        ayar_yaz(ayar)
        hedef = hedef_klasor()
        adet, ek, k_arama = max(1, adet_var.get()), ek_var.get(), klasor_arama_var.get()
        yenile = yenile_var.get()
        for b in (btn, btn_resim, btn_klasor):
            b.configure(state="disabled")
        btn.configure(text="İndiriliyor...")

        def is_parcacigi():
            nonlocal metin
            try:
                if duzenle:
                    yeni = ""
                    if key:
                        try:
                            yeni = listeyi_duzenle(metin, saglayici, key, model, log)
                            if not any(x.strip().startswith("#") for x in yeni.splitlines()):
                                yeni = ""
                        except Exception as e:
                            msg = str(e)
                            if "API_KEY_HATALI" in msg:
                                ayar.pop("gemini_key", None)
                                ayar_yaz(ayar)
                            log(f"Yapay zekâ ile ayrılamadı ({msg[:150]}).")
                            yeni = ""
                    if not yeni:
                        log("Yerleşik sınıflandırıcı ile kategorilere ayrılıyor...")
                        yeni = yerel_siniflandir(metin)
                    metin = yeni
                    son = metin
                    kok.after(0, lambda: (kutu.delete("1.0", "end"), kutu.insert("1.0", son)))
                    log("Kategoriler hazır (listeyi kutuda görebilirsin).\n")
                hepsini_indir(listeyi_ayristir(metin), hedef, adet, ek, log, k_arama, yenile,
                              kirpmalar=dict(brosur_kirp),
                              kontrol_yolu=KONTROL_SAYFASI)
                if (pro_var.get().strip() if pro_mu() else sql_var.get().strip()):
                    log("")
                    sql_yap_calistir(listeyi_ayristir(metin), hedef, metin)
            except Exception as e:
                log(f"HATA: {e}")
            finally:
                kok.after(0, lambda: ([b.configure(state="normal")
                                       for b in (btn, btn_resim, btn_klasor, btn_sql)],
                                      btn.configure(text="İndir")))
        threading.Thread(target=is_parcacigi, daemon=True).start()

    def klasoru_ac(h=None):
        h = h or hedef_klasor()
        if os.path.isdir(h):
            if os.name == "nt":
                os.startfile(h)
            else:
                import subprocess
                subprocess.Popen(["xdg-open" if sys.platform != "darwin" else "open", h])

    def api_degistir():
        ayar.pop("gemini_key", None)
        ayar_yaz(ayar)
        api_key_al()

    alt = tk.Frame(a_cer)
    alt.grid(row=4, column=0, columnspan=3, sticky="we", pady=4)
    tk.Button(alt, text="Resim klasörü", command=klasoru_ac).pack(side="left", padx=4)
    tk.Button(alt, text="Firma klasörü", command=lambda: klasoru_ac(firma_klasoru())).pack(side="left")

    def kontrol_ac():
        k = KONTROL_SAYFASI
        if os.path.exists(k):
            import webbrowser
            webbrowser.open("file:///" + os.path.abspath(k).replace(os.sep, "/"))
        else:
            messagebox.showinfo("Kontrol", "Önce 'İndir' ile resimleri indir.")
    tk.Button(alt, text="Resimleri kontrol et", command=kontrol_ac).pack(side="left", padx=4)
    tk.Button(alt, text="API anahtarını değiştir", command=api_degistir).pack(side="left")

    def guncelle(sessiz=False):
        """GitHub'daki son sürümü kontrol eder; yeniyse indirip kendini günceller."""
        repo = ayar.get("github_repo", GITHUB_REPO)
        token = ayar.get("github_token", "")

        def is_():
            try:
                surum, adres, notlar = github_son_surum(repo, token)
            except Exception as e:
                if not sessiz:
                    log(f"Güncelleme kontrolü olmadı: {e}")
                return
            if _surum_tuple(surum) <= _surum_tuple(SURUM):
                if not sessiz:
                    kok.after(0, lambda: messagebox.showinfo("Güncelle", f"Program güncel (v{SURUM})."))
                return

            def sor():
                metin = f"Yeni sürüm var: v{surum}  (şu an v{SURUM})"
                if notlar:
                    metin += "\n\n" + notlar[:600]
                if not getattr(sys, "frozen", False):
                    messagebox.showinfo("Güncelle", metin + "\n\nProgram .py olarak çalışıyor; "
                                        "exe'yi GitHub Releases'ten indir.")
                    return
                if messagebox.askyesno("Güncelle", metin + "\n\nŞimdi indirilip kurulsun mu?\n"
                                       "(Program kapanıp yeni sürümle açılır.)"):
                    threading.Thread(target=indir, daemon=True).start()
            kok.after(0, sor)

            def indir():
                try:
                    hedef = os.path.join(os.path.dirname(sys.executable), "UrunResimIndirici_yeni.exe")
                    log(f"v{surum} indiriliyor...")
                    son = [-10]

                    def ilerleme(y):
                        if y - son[0] >= 10:
                            son[0] = y
                            log(f"   %{y}")
                    hedef = github_exe_indir(adres, hedef, token, ilerleme)
                    log("İndi. Program kapanıp yeni sürümle açılacak...")
                    exe_degistir_ve_yeniden_ac(hedef, SURUM)
                    kok.after(800, kok.destroy)
                except Exception as e:
                    log(f"Güncelleme indirilemedi: {e}")
        threading.Thread(target=is_, daemon=True).start()

    kok.after(3000, lambda: guncelle(sessiz=True))      # açılışta sessizce kontrol et
    tk.Button(alt, text="Güncelle", command=guncelle).pack(side="left", padx=4)
    btn = tk.Button(alt, text="İndir", bg="#2e7d32", fg="white",
                    font=("Segoe UI", 10, "bold"), width=14, command=baslat)
    btn.pack(side="right", padx=4)

    # --- SQL
    q_cer = tk.LabelFrame(kok, text=" 4) POS çıktısı (isteğe bağlı) ")
    q_cer.pack(fill="x", padx=10, pady=4)
    mod_var = tk.StringVar(value=ayar.get("cikti_modu", "sql"))

    def pro_mu():
        return mod_var.get() == "pro"
    mf = tk.Frame(q_cer)
    mf.grid(row=5, column=0, columnspan=3, sticky="we", pady=(2, 0))
    tk.Label(mf, text="Çıktı:").pack(side="left", padx=4)
    tk.Radiobutton(mf, text="Eski SiparişMatik (.sql)", value="sql", variable=mod_var,
                   command=lambda: mod_degisti()).pack(side="left")
    tk.Radiobutton(mf, text="SiparişMatik Pro (siparismatik.db)", value="pro", variable=mod_var,
                   command=lambda: mod_degisti()).pack(side="left", padx=8)
    sql_satiri = []          # SQL dosyası satırı (Pro seçilince gizlenir, eskiye dönünce geri gelir)
    w = tk.Label(q_cer, text="SQL dosyası:")
    w.grid(row=0, column=0, sticky="w", padx=4)
    sql_satiri.append(w)
    sql_var = tk.StringVar(value=ayar.get("sql", ""))
    w = tk.Entry(q_cer, textvariable=sql_var, width=60)
    w.grid(row=0, column=1, sticky="we")
    sql_satiri.append(w)
    w = tk.Button(q_cer, text="Seç...", command=lambda: sql_var.set(
        filedialog.askopenfilename(filetypes=[("SQL", "*.sql"), ("Hepsi", "*.*")])
        or sql_var.get()))
    w.grid(row=0, column=2, padx=4)
    sql_satiri.append(w)
    ic = tk.Frame(q_cer)
    ic.grid(row=1, column=0, columnspan=3, sticky="we", pady=2)
    tk.Label(ic, text="Fiyatı yazmayanlara (TL):").pack(side="left", padx=4)
    fiyat_var = tk.StringVar(value=str(ayar.get("fiyat", 1)))
    tk.Entry(ic, textvariable=fiyat_var, width=7).pack(side="left")
    tk.Label(ic, text="KDV %:").pack(side="left", padx=(10, 4))
    kdv_var = tk.StringVar(value=str(ayar.get("kdv", 10)))
    tk.Entry(ic, textvariable=kdv_var, width=4).pack(side="left")
    tk.Label(ic, text="SQL kategorisi:").pack(side="left", padx=(10, 4))
    seviye_var = tk.IntVar(value=ayar.get("kategori_seviye", 1))
    tk.Radiobutton(ic, text="Kategori", value=1, variable=seviye_var).pack(side="left")
    tk.Radiobutton(ic, text="Kategori + Pişme + Menü", value=3,
                   variable=seviye_var).pack(side="left")
    q_cer.columnconfigure(1, weight=1)

    # --- SiparişMatik Pro alanları
    p_cer = tk.Frame(q_cer)
    p_cer.grid(row=6, column=0, columnspan=3, sticky="we")
    tk.Label(p_cer, text="Pro veritabanı:").grid(row=0, column=0, sticky="w", padx=4)
    pro_var = tk.StringVar(value=ayar.get("pro_db", ""))
    tk.Entry(p_cer, textvariable=pro_var, width=60).grid(row=0, column=1, sticky="we")
    tk.Button(p_cer, text="Seç...", command=lambda: pro_var.set(
        filedialog.askopenfilename(title="Pro'nun siparismatik.db dosyası (AppData\\Roaming\\siparismatik-pro)",
                                   filetypes=[("Pro veritabanı", "*.db"), ("Hepsi", "*.*")])
        or pro_var.get())).grid(row=0, column=2, padx=4)
    tk.Label(p_cer, text="Kurulacak PC kullanıcı adı:").grid(row=1, column=0, sticky="w", padx=4)
    try:
        import getpass
        _kul = getpass.getuser()
    except Exception:
        _kul = ""
    kullanici_var = tk.StringVar(value=ayar.get("pro_kullanici", _kul))
    tk.Entry(p_cer, textvariable=kullanici_var, width=30).grid(row=1, column=1, sticky="w")
    tk.Label(p_cer, text="Opsiyonlar:").grid(row=2, column=0, sticky="w", padx=4)
    opsiyon_var = tk.StringVar(value=ayar.get("pro_opsiyonlar", PRO_OPSIYON_VARSAYILAN))
    tk.Entry(p_cer, textvariable=opsiyon_var, width=60).grid(row=2, column=1, columnspan=2, sticky="we")
    pf = tk.Frame(p_cer)
    pf.grid(row=3, column=0, columnspan=3, sticky="w")
    icecek_ops_var = tk.BooleanVar(value=ayar.get("pro_icecek_opsiyon", False))
    tk.Checkbutton(pf, text="İçeceklere de opsiyon ekle", variable=icecek_ops_var).pack(side="left", padx=4)
    demo_var = tk.BooleanVar(value=ayar.get("pro_demo_temizle", True))
    tk.Checkbutton(pf, text="Demo verileri temizle (yönetici hesabı kalır)",
                   variable=demo_var).pack(side="left", padx=8)

    def kurulu_pro_temizle():
        yol = filedialog.askopenfilename(
            title="Temizlenecek siparismatik.db (Pro KAPALI olmalı)",
            filetypes=[("Pro veritabanı", "*.db"), ("Hepsi", "*.*")])
        if not yol:
            return
        if not messagebox.askyesno("Demo temizle", "SiparişMatik Pro kapalı mı?\n\n"
                                   "Örnek siparişler, müşteriler, e-fatura firmaları, kullanıcılar ve yazıcılar "
                                   "silinecek. Yönetici hesabına dokunulmaz. Önce yedek alınır."):
            return
        try:
            pro_demo_temizle_dosya(yol, log)
        except Exception as e:
            log(f"TEMİZLİK HATASI: {e}")
    tk.Button(pf, text="Kurulu Pro'da demo temizle...", command=kurulu_pro_temizle).pack(side="left", padx=4)
    p_cer.columnconfigure(1, weight=1)

    def mod_degisti():
        # grid_slaves() gizlenmiş widget'ları döndürmez; bu yüzden satır elle tutuluyor
        for w in sql_satiri:
            if pro_mu():
                w.grid_remove()
            else:
                w.grid()
        if pro_mu():
            p_cer.grid()
            btn_sql.configure(text="Listeden Pro DB oluştur")
        else:
            p_cer.grid_remove()
            btn_sql.configure(text="Listeden SQL oluştur")
        ayar["cikti_modu"] = mod_var.get()
        ayar_yaz(ayar)

    def kaynak_hazir():
        if pro_mu():
            if not os.path.isfile(pro_var.get()):
                messagebox.showwarning("Pro veritabanı yok", "Önce Pro'nun siparismatik.db dosyasını seç "
                                       "(yeni kurulumun AppData\\Roaming\\siparismatik-pro klasöründen).")
                return False
            return True
        if not os.path.isfile(sql_var.get()):
            messagebox.showwarning("SQL yok", "Önce SQL yedek dosyasını seç.")
            return False
        return True

    def cikti_yaz(kategoriler, kaynak_klasor, fiyat, kdv, metin, gercek=None):
        firma = firma_klasoru()
        log(f"Firma: {os.path.basename(firma)}")
        if pro_mu():
            ayar.update(pro_db=pro_var.get(), pro_kullanici=kullanici_var.get().strip(),
                        pro_opsiyonlar=opsiyon_var.get(), pro_icecek_opsiyon=icecek_ops_var.get(),
                        pro_demo_temizle=demo_var.get())
            ayar_yaz(ayar)
            pro_db_olustur(pro_var.get(), kategoriler, kaynak_klasor, firma, fiyat=fiyat, kdv=kdv,
                           kategori_seviye=seviye_var.get(), log=log, gercek_klasorler=gercek,
                           ek_odemeler=odeme_kodlari_bul(metin), kullanici=kullanici_var.get(),
                           opsiyonlar=opsiyon_var.get(), icecege_opsiyon=icecek_ops_var.get(),
                           demo_temizle=demo_var.get())
        else:
            sql_olustur(sql_var.get(), kategoriler, kaynak_klasor,
                        os.path.join(firma, os.path.basename(sql_var.get())), fiyat=fiyat, kdv=kdv,
                        kategori_seviye=seviye_var.get(), elle=ayar.get("sql_sutunlar"), log=log,
                        gercek_klasorler=gercek, ek_odemeler=odeme_kodlari_bul(metin))

    def sql_yap_calistir(kategoriler, hedef, metin=""):
        try:
            fiyat = float(fiyat_var.get().replace(",", "."))
            kdv = float(kdv_var.get().replace(",", "."))
        except ValueError:
            log("HATA: Fiyat/KDV sayı olmalı.")
            return
        ayar.update(sql=sql_var.get(), fiyat=fiyat, kdv=kdv, kategori_seviye=seviye_var.get())
        ayar_yaz(ayar)
        cikti_yaz(kategoriler, hedef, fiyat, kdv, metin)

    def sql_buton():
        if not kaynak_hazir():
            return
        metin_ = kutu.get("1.0", "end")
        if basliksiz_mi(metin_):
            metin_ = yerel_siniflandir(metin_)
        kategoriler = listeyi_ayristir(metin_)
        if not kategoriler:
            messagebox.showwarning("Liste boş", "Ürün listesi boş.")
            return
        hedef = hedef_klasor()

        def is_():
            try:
                sql_yap_calistir(kategoriler, hedef, metin_)
            except Exception as e:
                log(f"SQL HATASI: {e}")
        threading.Thread(target=is_, daemon=True).start()

    def klasorden_sql(yol):
        if not yol:
            return
        if not kaynak_hazir():
            return
        kutu_metni = kutu.get("1.0", "end")          # kutuda "ÖDEME: ..." satırı varsa o da kullanılır

        def is_():
            try:
                log(f"Klasör taranıyor: {yol}")
                kategoriler, gercek = klasoru_tara(yol)
                toplam = sum(len(v) for v in kategoriler.values())
                if not toplam:
                    log("Klasörde resim bulunamadı.")
                    return
                log(f"{len(kategoriler)} klasör, {toplam} ürün resmi bulundu.")
                try:
                    fiyat = float(fiyat_var.get().replace(",", "."))
                    kdv = float(kdv_var.get().replace(",", "."))
                except ValueError:
                    log("HATA: Fiyat/KDV sayı olmalı.")
                    return
                ayar.update(sql=sql_var.get(), fiyat=fiyat, kdv=kdv,
                            kategori_seviye=seviye_var.get())
                ayar_yaz(ayar)
                cikti_yaz(kategoriler, yol, fiyat, kdv, kutu_metni, gercek)
            except Exception as e:
                log(f"SQL HATASI: {e}")
        threading.Thread(target=is_, daemon=True).start()

    btn_sql = tk.Button(q_cer, text="Listeden SQL oluştur", command=sql_buton)
    btn_sql.grid(row=2, column=2, sticky="e", padx=4, pady=4)
    kb = tk.Frame(q_cer)
    kb.grid(row=3, column=0, columnspan=3, sticky="we", pady=(0, 4))
    tk.Label(kb, text="Hazır resim klasörü / zip'i varsa (klasör = kategori, dosya adı = ürün):",
             fg="#555").pack(side="left", padx=4)
    tk.Button(kb, text="Zip'ten", command=lambda: klasorden_sql(filedialog.askopenfilename(
        filetypes=[("Zip", "*.zip")]))).pack(side="right", padx=4)
    tk.Button(kb, text="Klasörden", command=lambda: klasorden_sql(
        filedialog.askdirectory())).pack(side="right")
    tk.Label(q_cer, text="SQL / Pro dosyası seçiliyse 'İndir' bitince otomatik oluşturulur; "
                         "Firmalar\\<firma> klasörüne kaydedilir.",
             fg="#555").grid(row=2, column=0, columnspan=2, sticky="w", padx=4)

    mod_degisti()
    log_kutu.pack(fill="both", expand=False, padx=10, pady=(4, 10))
    kok.mainloop()


# ---------------------------------------------------------------- giriş

def main():
    if len(sys.argv) == 1:
        pencere()
        return
    p = argparse.ArgumentParser(description="Menüden 600x600 ürün resmi indirici")
    p.add_argument("girdi", nargs="+", help="Liste (.txt) veya menü resimleri/PDF/klasör")
    p.add_argument("--musteri", default="", help="Firma adı (Firmalar/<ad> klasörü)")
    p.add_argument("--cikti", default=os.path.join(KLASOR, "Urun_Resimleri"))
    p.add_argument("--adet", type=int, default=1)
    p.add_argument("--ek", default="")
    p.add_argument("--klasor-arama", action="store_true")
    p.add_argument("--okuyucu", choices=["gemini", "ollama"], default="gemini")
    a = p.parse_args()

    if len(a.girdi) == 1 and a.girdi[0].lower().endswith(".txt"):
        with open(a.girdi[0], encoding="utf-8") as f:
            metin = f.read()
        if basliksiz_mi(metin):
            ayar = ayar_oku()
            key = ayar.get("gemini_key") or os.environ.get("GEMINI_API_KEY", "")
            yeni = ""
            if a.okuyucu == "ollama" or key:
                model = ayar.get("gemini_model" if a.okuyucu == "gemini" else "ollama_model", "")
                try:
                    yeni = listeyi_duzenle(metin, a.okuyucu, key, model, print)
                    if not any(x.strip().startswith("#") for x in yeni.splitlines()):
                        yeni = ""
                except Exception as e:
                    print("Yapay zekâ ile ayrılamadı:", e)
            metin = yeni or yerel_siniflandir(metin)
            print(metin)
    else:
        ayar = ayar_oku()
        key = ayar.get("gemini_key") or os.environ.get("GEMINI_API_KEY", "")
        if a.okuyucu == "gemini" and not key:
            print("Gemini anahtarı gerekli (aistudio.google.com/apikey). Pencereli modda bir kez gir.")
            sys.exit(1)
        model = ayar.get("gemini_model" if a.okuyucu == "gemini" else "ollama_model", "")
        metin = menuyu_oku(menu_dosyalarini_topla(a.girdi), a.okuyucu, key, model, print)
        print(metin)

    hedef = a.cikti                                    # resimler: ortak kütüphane
    hepsini_indir(listeyi_ayristir(metin), hedef, max(1, a.adet), a.ek, print, a.klasor_arama,
                  kontrol_yolu=KONTROL_SAYFASI)


if __name__ == "__main__":
    main()
