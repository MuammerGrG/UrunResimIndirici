// Ürün Resim İndirici — arayüz mantığı
'use strict';
const api = window.api;
const $ = (s, k = document) => k.querySelector(s);
const $$ = (s, k = document) => [...k.querySelectorAll(s)];

const D = {
  ayar: {},
  adim: 'kaynak',
  dosyalar: [],          // menü / broşür dosyaları
  kirpmalar: {},         // broşürden kesilecek ürün fotoğrafları
  kategoriler: [],       // son ayrıştırma
  odeme: [],
  basliksiz: false,
  galeri: new Map(),     // "yol/ad" → {durum, resim}
  is: null,              // süren uzun işlem {id, ad}
  ciktiOk: false,
};

// ------------------------------------------------------------------ küçük yardımcılar
const yaziSayi = (n) => (n == null ? '' : Number(n).toLocaleString('tr-TR', { maximumFractionDigits: 2 }));
const kacis = (s) => String(s ?? '').replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
const resimUrl = (yol) => !yol ? '' : api.resimUrl ? api.resimUrl(yol) : `resim://yerel/${encodeURIComponent(yol)}?v=${Date.now()}`;
const guvenliAd = (s) => String(s || '').replace(/[\\/:*?"<>|]+/g, '').trim().replace(/\.+$/, '').replace(/\s+/g, ' ');
function gecikmeli(fn, ms) { let t; return (...a) => { clearTimeout(t); t = setTimeout(() => fn(...a), ms); }; }

function oturumKaydet() {
  try {
    localStorage.setItem('oturum', JSON.stringify({
      firma: $('#firma').value, liste: $('#liste').value, metin: $('#metin').value,
      dosyalar: D.dosyalar, kirpmalar: D.kirpmalar, adim: D.adim,
    }));
  } catch { /* tarayıcı deposu yoksa önemsiz */ }
}
const oturumKaydetG = gecikmeli(oturumKaydet, 400);

// ------------------------------------------------------------------ kayıt (log)
const kayitSatirlari = [];
function log(m) {
  const satir = String(m ?? '');
  kayitSatirlari.push(satir);
  if (kayitSatirlari.length > 4000) kayitSatirlari.splice(0, 1000);
  const pre = $('#kayit-metin');
  const span = document.createElement('span');
  if (/^(HATA|.*HATASI)|^\s*✗|^Uyarı/.test(satir)) span.className = 'h';
  else if (/^\s*✓/.test(satir)) span.className = 'ok';
  span.textContent = satir + '\n';
  pre.appendChild(span);
  while (pre.childNodes.length > 3000) pre.removeChild(pre.firstChild);
  pre.scrollTop = pre.scrollHeight;
  const gorunen = satir.trim();
  if (gorunen) $('#kayit-son').textContent = gorunen;
}

// ------------------------------------------------------------------ motor
async function motor(komut, arg = {}, { ad, sessiz } = {}) {
  const id = await api.motor.yeniId();
  if (ad) mesgulBasla(id, ad);
  try {
    const r = await api.motor.istek(komut, arg, id);
    if (!r.tamam) {
      const h = new Error(r.hata || 'Bilinmeyen hata');
      h.kod = r.kod;
      throw h;
    }
    return r.sonuc;
  } catch (e) {
    if (!sessiz) {
      if (e.kod === 'iptal') log('Durduruldu.');
      else log('HATA: ' + e.message);
      if (e.kod === 'anahtar') ayarlariAc('Menü okumak için geçerli bir Gemini anahtarı gerekiyor.');
    }
    throw e;
  } finally {
    if (ad) mesgulBitir(id);
  }
}

function mesgulBasla(id, ad) {
  D.is = { id, ad };
  $('#mesgul-yazi').textContent = ad;
  $('#mesgul').hidden = false;
  $('#mesgul-nokta').classList.add('mesgul');
  durumlariGuncelle();
}
function mesgulBitir(id) {
  if (D.is && D.is.id !== id) return;
  D.is = null;
  $('#mesgul').hidden = true;
  $('#mesgul-nokta').classList.remove('mesgul');
  durumlariGuncelle();
}

api.motor.olay((o) => {
  if (o.olay === 'log') log(o.metin);
  else if (o.olay === 'metin') { $('#metin').value = o.metin; listeyiYenile(); }
  else if (o.olay === 'urun') urunOlayi(o);
});

// ------------------------------------------------------------------ soru penceresi
function sor(metin, dugmeler = [{ deger: 'evet', yazi: 'Tamam', birincil: true }]) {
  return new Promise((coz) => {
    const d = $('#soru');
    $('#soru-metin').textContent = metin;
    const k = $('#soru-dugmeler');
    k.innerHTML = '';
    for (const b of dugmeler) {
      const e = document.createElement('button');
      e.value = b.deger; e.textContent = b.yazi; e.className = b.birincil ? 'birincil' : 'hafif';
      k.appendChild(e);
    }
    d.returnValue = '';
    d.onclose = () => coz(d.returnValue || null);
    d.showModal();
  });
}

// ------------------------------------------------------------------ adımlar
function adimaGit(a) {
  D.adim = a;
  for (const b of $$('.adim')) {
    if (b.dataset.adim === a) b.setAttribute('aria-current', 'step');
    else b.removeAttribute('aria-current');
  }
  for (const p of $$('.panel')) p.hidden = p.dataset.panel !== a;
  if (a === 'resim') galeriYukle();
  if (a === 'cikti') modGoster();
  $('.sahne').scrollTop = 0;
  oturumKaydetG();
}
for (const b of $$('.adim')) b.addEventListener('click', () => adimaGit(b.dataset.adim));

function durumlariGuncelle() {
  const urun = D.kategoriler.reduce((t, k) => t + k.urunler.length, 0);
  const pors = D.kategoriler.reduce((t, k) => t + k.urunler.reduce((x, u) => x + u.porsiyonlar.length, 0), 0);
  $('#t-kat').textContent = D.kategoriler.length;
  $('#t-urun').textContent = urun;
  $('#t-pors').textContent = pors;
  let resimli = 0;
  for (const k of D.kategoriler) for (const u of k.urunler) if (D.galeri.get(k.yol + '/' + u.ad)?.resim) resimli++;
  $('#t-resim').textContent = D.galeri.size ? `${resimli}/${urun}` : '–';

  $('#b-kaynak').textContent = D.dosyalar.length ? `${D.dosyalar.length} menü dosyası` : $('#liste').value.trim() ? 'yazılı liste' : 'menü, liste ya da Excel';
  $('#b-liste').textContent = urun ? `${urun} ürün, ${D.kategoriler.length} kategori` : 'boş';
  $('#b-resim').textContent = D.galeri.size ? `${resimli} / ${urun} resimli` : 'indirilmedi';
  $('.adim[data-adim="kaynak"]').classList.toggle('bitti', urun > 0);
  $('.adim[data-adim="liste"]').classList.toggle('bitti', urun > 0 && !D.basliksiz);
  $('.adim[data-adim="resim"]').classList.toggle('bitti', urun > 0 && resimli === urun);
  $('.adim[data-adim="cikti"]').classList.toggle('bitti', D.ciktiOk);

  const mesgul = !!D.is;
  for (const id of ['#btn-oku', '#btn-indir', '#btn-olustur', '#btn-siniflandir']) $(id).disabled = mesgul;
  $('#btn-oku').disabled = mesgul || !D.dosyalar.length;
  $('#btn-oku').textContent = $('#liste').value.trim() && D.dosyalar.length ? 'Broşürle listeyi birleştir' : 'Menüyü oku';
  $('#btn-indir').textContent = mesgul && D.is?.ad?.startsWith('Resim') ? 'İndiriliyor…' : 'Resimleri indir';
}

// ------------------------------------------------------------------ 1) kaynak
function dosyalariGoster() {
  const ul = $('#dosyalar');
  ul.innerHTML = '';
  D.dosyalar.forEach((y, i) => {
    const li = document.createElement('li');
    li.title = y;
    li.textContent = y.split(/[\\/]/).pop();
    const x = document.createElement('button');
    x.textContent = '×'; x.title = 'Çıkar'; x.setAttribute('aria-label', 'Çıkar');
    x.onclick = () => { D.dosyalar.splice(i, 1); dosyalariGoster(); };
    li.appendChild(x);
    ul.appendChild(li);
  });
  durumlariGuncelle();
  oturumKaydetG();
}
function dosyaEkle(yollar) {
  for (const y of yollar) if (y && !D.dosyalar.includes(y)) D.dosyalar.push(y);
  D.kirpmalar = {};
  dosyalariGoster();
}

$('#btn-menu-dosya').onclick = async () => dosyaEkle(await api.dosya.ac({
  baslik: 'Menü resimleri ya da PDF', ozellik: ['openFile', 'multiSelections'],
  filtre: [{ name: 'Menü', extensions: ['jpg', 'jpeg', 'png', 'webp', 'bmp', 'gif', 'pdf'] }, { name: 'Hepsi', extensions: ['*'] }],
}));
$('#btn-menu-klasor').onclick = async () => dosyaEkle(await api.dosya.ac({ baslik: 'Menü klasörü', ozellik: ['openDirectory'] }));

const birak = $('#birak');
birak.addEventListener('dragover', (e) => { e.preventDefault(); birak.classList.add('uzerinde'); });
birak.addEventListener('dragleave', (e) => { if (!birak.contains(e.relatedTarget)) birak.classList.remove('uzerinde'); });
birak.addEventListener('drop', (e) => {
  e.preventDefault();
  birak.classList.remove('uzerinde');
  dosyaEkle([...e.dataTransfer.files].map((f) => api.dosya.yolu(f)));
});
document.addEventListener('dragover', (e) => e.preventDefault());
document.addEventListener('drop', (e) => e.preventDefault());

function saglayici() {
  const a = D.ayar;
  return { ad: a.saglayici || 'gemini', geminiAnahtar: a.geminiAnahtar || '', mistralAnahtar: a.mistralAnahtar || '',
    model: a.saglayici === 'ollama' ? a.ollamaModel || '' : a.model || '' };
}

$('#btn-oku').onclick = async () => {
  const s = saglayici();
  if (s.ad === 'gemini' && !s.geminiAnahtar && !s.mistralAnahtar) {
    ayarlariAc('Menüyü okumak için ücretsiz Gemini anahtarını bir kez gir.');
    return;
  }
  let liste = $('#liste').value.trim();
  const mevcut = $('#metin').value.trim();
  if (!liste && mevcut && D.basliksiz) liste = mevcut;
  try {
    const r = await motor('menu_oku', { yollar: D.dosyalar, liste, saglayici: s }, { ad: 'Menü okunuyor' });
    if (mevcut && mevcut !== liste) {
      const c = await sor('Ürün listesi kutusu dolu. Okunan menü ne yapılsın?', [
        { deger: 'iptal', yazi: 'Vazgeç' }, { deger: 'ekle', yazi: 'Sonuna ekle' }, { deger: 'degistir', yazi: 'Yerine koy', birincil: true }]);
      if (c === 'iptal' || !c) return;
      $('#metin').value = c === 'ekle' ? mevcut + '\n\n' + r.metin : r.metin;
    } else $('#metin').value = r.metin;
    D.kirpmalar = r.kirpmalar || {};
    log('Listeyi kontrol et, sonra resimlere geç.\n');
    await listeyiYenile();
    adimaGit('liste');
  } catch { /* log'a yazıldı */ }
};

$('#btn-liste-aktar').onclick = async () => {
  const l = $('#liste').value.trim();
  if (!l) return;
  const m = $('#metin').value.trim();
  if (m) {
    const c = await sor('Ürün listesi kutusu dolu.', [{ deger: 'iptal', yazi: 'Vazgeç' }, { deger: 'ekle', yazi: 'Sonuna ekle' }, { deger: 'degistir', yazi: 'Yerine koy', birincil: true }]);
    if (!c || c === 'iptal') return;
    $('#metin').value = c === 'ekle' ? m + '\n' + l : l;
  } else $('#metin').value = l;
  await listeyiYenile();
  adimaGit('liste');
};
$('#btn-liste-temizle').onclick = () => { $('#liste').value = ''; durumlariGuncelle(); oturumKaydetG(); };
$('#liste').addEventListener('input', () => {
  $('#liste-not').textContent = D.dosyalar.length && $('#liste').value.trim() ? 'Menü okunurken bu liste broşürle karşılaştırılır.' : '';
  durumlariGuncelle(); oturumKaydetG();
});

$('#btn-excel-oku').onclick = async () => {
  const [y] = await api.dosya.ac({ baslik: 'Müşterinin Excel ürün listesi', filtre: [{ name: 'Excel', extensions: ['xlsx', 'xlsm', 'xls'] }] });
  if (!y) return;
  try {
    const r = await motor('excel_oku', { yol: y });
    let hedef = '#metin';
    if ($('#metin').value.trim()) {
      const c = await sor('Ürün listesi kutusu dolu. Excel nereye konsun?', [
        { deger: 'iptal', yazi: 'Vazgeç' }, { deger: 'liste', yazi: 'Yazılı liste kutusuna (broşürle karşılaştır)' }, { deger: 'metin', yazi: 'Ürün listesinin yerine', birincil: true }]);
      if (!c || c === 'iptal') return;
      hedef = c === 'liste' ? '#liste' : '#metin';
    }
    $(hedef).value = r.metin;
    log(`Excel'den ${r.urunSayisi} ürün yüklendi: ${y.split(/[\\/]/).pop()}`);
    if (hedef === '#metin') { await listeyiYenile(); adimaGit('liste'); } else durumlariGuncelle();
  } catch { /* */ }
};

$('#btn-txt-oku').onclick = async () => {
  const [y] = await api.dosya.ac({ baslik: 'Liste dosyası', filtre: [{ name: 'Metin', extensions: ['txt'] }, { name: 'Hepsi', extensions: ['*'] }] });
  if (!y) return;
  $('#metin').value = await api.dosya.metinOku(y);
  await listeyiYenile();
  adimaGit('liste');
};

$('#btn-hazir-klasor').onclick = async () => {
  const c = await sor('Hazır resimler nerede?', [{ deger: 'iptal', yazi: 'Vazgeç' }, { deger: 'zip', yazi: 'Zip dosyası' }, { deger: 'klasor', yazi: 'Klasör', birincil: true }]);
  if (!c || c === 'iptal') return;
  const [y] = c === 'zip'
    ? await api.dosya.ac({ baslik: 'Resim zip dosyası', filtre: [{ name: 'Zip', extensions: ['zip'] }] })
    : await api.dosya.ac({ baslik: 'Hazır resim klasörü', ozellik: ['openDirectory'] });
  if (!y) return;
  $('#hazirKlasor').value = y;
  $$('input[name="kaynak"]').forEach((r) => { r.checked = r.value === 'klasor'; });
  kaynakDegisti();
  try {
    const r = await motor('klasor_tara', { yol: y }, { ad: 'Klasör taranıyor' });
    log(`${r.kategoriler.length} klasör, ${r.urunSayisi} ürün resmi bulundu.`);
  } catch { /* */ }
  adimaGit('cikti');
};

// ------------------------------------------------------------------ 2) liste
async function listeyiYenile() {
  const metin = $('#metin').value;
  oturumKaydetG();
  try {
    const r = await motor('ayristir', { metin }, { sessiz: true });
    D.kategoriler = r.kategoriler; D.odeme = r.odeme; D.basliksiz = r.basliksiz;
  } catch { D.kategoriler = []; D.odeme = []; D.basliksiz = false; }
  $('#basliksiz').hidden = !D.basliksiz;
  onizlemeCiz();
  durumlariGuncelle();
}
const listeyiYenileG = gecikmeli(listeyiYenile, 300);
$('#metin').addEventListener('input', listeyiYenileG);

function onizlemeCiz() {
  const k = $('#onizleme');
  if (!D.kategoriler.length) {
    k.innerHTML = '<p class="bos">Liste boş. Menüyü okut, yazılı listeyi aktar ya da buraya yapıştır.</p>';
    return;
  }
  const parca = [];
  for (const kat of D.kategoriler) {
    const yol = kat.yol.split('/');
    parca.push(`<div class="on-kat"><h3><span>${kacis(yol[0])}${yol.length > 1 ? ` <span class="on-yol">/ ${kacis(yol.slice(1).join(' / '))}</span>` : ''}</span><small>${kat.urunler.length} ürün</small></h3>`);
    for (const u of kat.urunler) {
      parca.push(`<div class="on-urun"><span>${kacis(u.ad)}</span><span class="fiyat${u.fiyat ? '' : ' yok'}">${u.fiyat ? yaziSayi(u.fiyat) + ' ₺' : 'fiyat yok'}</span>`);
      if (u.porsiyonlar.length) parca.push(`<div class="porsiyonlar">${u.porsiyonlar.map((p) => `<span>${kacis(p.ad)}${p.fiyat ? ' ' + yaziSayi(p.fiyat) + ' ₺' : ''}</span>`).join('')}</div>`);
      parca.push('</div>');
    }
    parca.push('</div>');
  }
  const odemeAd = { NAKIT: 'Nakit', KREDIKARTI: 'Kredi kartı', SODEXO: 'Sodexo / Pluxee', MULTINET: 'Multinet', SMARTICKET: 'Ticket / Edenred', SETCARD: 'Setcard', METROPOL: 'Metropol', PAYEKART: 'Paye', TOKENFLEX: 'Tokenflex', WINWIN: 'Win Win', YEMEKMATIK: 'Yemekmatik', CIOCARD: 'CIO Card', ENCARD: 'En Kart', TEBCARD: 'TEB Kart', BISTRACARD: 'Bistra', MAXIMACARD: 'Maxima', ADVANTAGE: 'Advantage', MAXIMUM: 'Maximum' };
  parca.push(`<div class="odeme-satiri">Seçilecek ödeme tipleri: <span class="etiket">Nakit</span><span class="etiket">Kredi kartı</span>${D.odeme.filter((x) => x !== 'NAKIT' && x !== 'KREDIKARTI').map((x) => `<span class="etiket">${kacis(odemeAd[x] || x)}</span>`).join('')}</div>`);
  k.innerHTML = parca.join('');
}

$('#btn-siniflandir').onclick = async () => {
  try {
    const r = await motor('siniflandir', { metin: $('#metin').value, saglayici: saglayici() }, { ad: 'Kategorilere ayrılıyor' });
    $('#metin').value = r.metin;
    log(r.yontem === 'yapay' ? 'Yapay zekâ ile kategorilere ayrıldı.' : 'Yerleşik sınıflandırıcı ile kategorilere ayrıldı.');
    await listeyiYenile();
  } catch { /* */ }
};
$('#btn-metin-temizle').onclick = async () => {
  if (!$('#metin').value.trim()) return;
  if (await sor('Ürün listesi silinsin mi?', [{ deger: 'iptal', yazi: 'Vazgeç' }, { deger: 'evet', yazi: 'Sil', birincil: true }]) !== 'evet') return;
  $('#metin').value = ''; D.kirpmalar = {};
  listeyiYenile();
};
$('#btn-txt-kaydet').onclick = async () => {
  const y = await api.dosya.kaydetSec({ baslik: 'Listeyi kaydet', yol: await api.yol.birlestir(await firmaKlasoru(), 'liste.txt'), filtre: [{ name: 'Metin', extensions: ['txt'] }] });
  if (y) { await api.dosya.metinYaz(y, $('#metin').value); log('Liste kaydedildi: ' + y); }
};
$('#btn-excel-yaz').onclick = async () => {
  if (!D.kategoriler.length) return;
  const firma = await firmaKlasoru();
  const y = await api.dosya.kaydetSec({ baslik: 'Menüyü Excel olarak kaydet', yol: await api.yol.birlestir(firma, 'menu.xlsx'), filtre: [{ name: 'Excel', extensions: ['xlsx'] }] });
  if (!y) return;
  try { await motor('excel_yaz', { metin: $('#metin').value, resimKok: D.ayar.cikti, hedef: y, firma: firmaAdi() }, { ad: 'Excel yazılıyor' }); } catch { /* */ }
};
$('#btn-resime-gec').onclick = () => adimaGit('resim');

// ------------------------------------------------------------------ 3) resimler
function kartAnahtar(yol, ad) { return yol + '/' + ad; }
const durumYazi = { var: 'hazır', indi: 'internetten', yedek: 'bulunamadı, adı yazılı kart', yok: 'bulunamadı', brosur: 'broşürden kesildi', bekliyor: 'sırada', elle: 'elle seçildi' };

async function galeriYukle() {
  if (!D.ayar.cikti) return;
  try {
    const r = await motor('galeri', { metin: $('#metin').value, cikti: D.ayar.cikti }, { sessiz: true });
    for (const k of r.kategoriler)
      for (const u of k.urunler) {
        const a = kartAnahtar(k.yol, u.ad);
        const eski = D.galeri.get(a);
        D.galeri.set(a, { durum: u.resim ? (eski?.durum && eski.durum !== 'bekliyor' ? eski.durum : 'var') : (eski?.durum || 'bekliyor'), resim: u.resim });
      }
    galeriCiz(r.kategoriler);
  } catch { galeriCiz([]); }
  durumlariGuncelle();
}

function galeriCiz(kategoriler) {
  const g = $('#galeri');
  if (!kategoriler.length) {
    g.innerHTML = '<p class="ipucu">Önce ürün listesini hazırla.</p>';
    return;
  }
  g.innerHTML = '';
  for (const k of kategoriler) {
    const b = document.createElement('section');
    b.className = 'galeri-kat';
    const resimli = k.urunler.filter((u) => D.galeri.get(kartAnahtar(k.yol, u.ad))?.resim).length;
    b.innerHTML = `<h3>${kacis(k.yol.split('/').join(' / '))} <small>${resimli}/${k.urunler.length}</small></h3><div class="izgara"></div>`;
    const iz = $('.izgara', b);
    for (const u of k.urunler) iz.appendChild(kartYap(k.yol, u));
    g.appendChild(b);
  }
}

function kartYap(yol, u) {
  const a = kartAnahtar(yol, u.ad);
  const d = D.galeri.get(a) || { durum: 'bekliyor' };
  const kart = document.createElement('article');
  kart.className = 'urun-kart';
  kart.dataset.anahtar = a;
  kart.dataset.durum = d.durum;
  kart.innerHTML = `<div class="gorsel">${d.resim ? `<img alt="" src="${resimUrl(d.resim)}">` : '<span class="yok">resim yok</span>'}</div>
    <div class="ad">${kacis(u.ad)}</div><div class="durum">${durumYazi[d.durum] || ''}</div>
    <div class="eylem"><button data-e="yenile">Farklı resim</button><button data-e="sec">Dosya seç</button></div>`;
  $('[data-e="yenile"]', kart).onclick = () => resimYenile(yol, u, kart);
  $('[data-e="sec"]', kart).onclick = async () => {
    const [f] = await api.dosya.ac({ baslik: u.ad + ' için resim', filtre: [{ name: 'Resim', extensions: ['jpg', 'jpeg', 'png', 'webp', 'bmp', 'gif'] }] });
    if (f) resimAyarla(yol, u, kart, f);
  };
  kart.addEventListener('dragover', (e) => { e.preventDefault(); kart.classList.add('uzerinde'); });
  kart.addEventListener('dragleave', () => kart.classList.remove('uzerinde'));
  kart.addEventListener('drop', (e) => {
    e.preventDefault(); e.stopPropagation();
    kart.classList.remove('uzerinde');
    const f = e.dataTransfer.files[0];
    if (f) resimAyarla(yol, u, kart, api.dosya.yolu(f));
  });
  return kart;
}

function kartGuncelle(a, durum, resim) {
  D.galeri.set(a, { durum, resim });
  const kart = $$('.urun-kart').find((k) => k.dataset.anahtar === a);
  if (!kart) return;
  kart.classList.remove('calisiyor');
  kart.dataset.durum = durum;
  $('.gorsel', kart).innerHTML = resim ? `<img alt="" src="${resimUrl(resim)}">` : '<span class="yok">resim yok</span>';
  $('.durum', kart).textContent = durumYazi[durum] || '';
  const kat = kart.closest('.galeri-kat');
  if (kat) {
    const kartlar = $$('.urun-kart', kat);
    $('h3 small', kat).textContent = `${kartlar.filter((k) => D.galeri.get(k.dataset.anahtar)?.resim).length}/${kartlar.length}`;
  }
  durumlariGuncelle();
}

async function resimYenile(yol, u, kart) {
  kart.classList.add('calisiyor');
  try {
    const r = await motor('resim_yenile', { cikti: D.ayar.cikti, yol, ad: u.ad, arama: u.arama, ek: $('#ek').value, klasorArama: $('#klasorArama').checked });
    kartGuncelle(kartAnahtar(yol, u.ad), r.durum === 'yok' ? D.galeri.get(kartAnahtar(yol, u.ad))?.durum || 'yok' : r.durum, r.resim);
  } catch { kart.classList.remove('calisiyor'); }
}
async function resimAyarla(yol, u, kart, dosya) {
  kart.classList.add('calisiyor');
  try {
    const r = await motor('resim_ayarla', { cikti: D.ayar.cikti, yol, ad: u.ad, dosya });
    kartGuncelle(kartAnahtar(yol, u.ad), 'elle', r.resim);
  } catch { kart.classList.remove('calisiyor'); }
}

function urunOlayi(o) {
  const a = kartAnahtar(o.yol, o.ad);
  kartGuncelle(a, o.durum, o.resim);
  $('#ilerleme').hidden = false;
  $('#ilerleme-cubuk').style.width = `${Math.round((o.sira / o.toplam) * 100)}%`;
  $('#ilerleme-yazi').textContent = `${o.sira} / ${o.toplam}`;
  const sonraki = $$('.urun-kart').find((k) => k.dataset.durum === 'bekliyor');
  sonraki?.classList.add('calisiyor');
}

for (const id of ['adet', 'ek', 'klasorArama', 'otomatikCikti']) {
  $('#' + id).addEventListener('change', () => {
    const e = $('#' + id);
    ayarKaydet({ [id]: e.type === 'checkbox' ? e.checked : e.type === 'number' ? Math.max(1, Math.min(10, +e.value || 1)) : e.value });
  });
}

$('#btn-indir').onclick = async () => {
  if (!D.kategoriler.length) { log('Önce ürün listesini hazırla.'); adimaGit('liste'); return; }
  if (D.basliksiz) log('Liste kategorisiz; önce kategorilere ayrılacak.');
  $('#ilerleme').hidden = false;
  $('#ilerleme-cubuk').style.width = '0%';
  $('#ilerleme-yazi').textContent = 'başlıyor';
  if ($('#yenile').checked) for (const [a, d] of D.galeri) D.galeri.set(a, { ...d, durum: 'bekliyor' });
  await galeriYukle();
  $$('.urun-kart').find((k) => k.dataset.durum === 'bekliyor')?.classList.add('calisiyor');
  try {
    const r = await motor('indir', {
      metin: $('#metin').value, cikti: D.ayar.cikti, adet: +$('#adet').value || 1, ek: $('#ek').value,
      klasorArama: $('#klasorArama').checked, yenile: $('#yenile').checked, kirpmalar: D.kirpmalar, saglayici: saglayici(),
    }, { ad: 'Resimler indiriliyor' });
    $('#metin').value = r.metin;
    $('#yenile').checked = false;
    await listeyiYenile();
    await galeriYukle();
    $('#ilerleme-yazi').textContent = r.basarisiz.length ? `bitti, ${r.basarisiz.length} ürünün resmi bulunamadı` : 'bitti';
    if ($('#otomatikCikti').checked && (D.ayar.ciktiModu === 'pro' ? D.ayar.proDb : D.ayar.sql)) {
      log('');
      await ciktiOlustur();
    }
  } catch {
    $$('.urun-kart.calisiyor').forEach((k) => k.classList.remove('calisiyor'));
    $('#ilerleme-yazi').textContent = 'durdu';
  }
};
$('#btn-kutuphane').onclick = () => api.dosya.goster(D.ayar.cikti);

// ------------------------------------------------------------------ 4) çıktı
function firmaAdi() { return guvenliAd($('#firma').value); }
async function firmaKlasoru() {
  let ad = firmaAdi();
  if (!ad) {
    try {
      const r = await motor('firma_adi', D.ayar.ciktiModu === 'pro' ? { pro: D.ayar.proDb } : { sql: D.ayar.sql }, { sessiz: true });
      ad = guvenliAd(r.ad);
      if (ad) { $('#firma').value = ad; oturumKaydetG(); }
    } catch { /* */ }
  }
  return api.yol.birlestir(D.ayar.firmalar, ad || 'Firmasiz');
}

function modGoster() {
  const mod = D.ayar.ciktiModu === 'pro' ? 'pro' : 'sql';
  for (const b of $$('[data-mod]')) b.setAttribute('aria-checked', String(b.dataset.mod === mod));
  for (const e of $$('[data-mod-alan]')) e.hidden = e.dataset.modAlan !== mod;
  $('#btn-olustur').textContent = mod === 'pro' ? 'Pro veritabanını oluştur' : 'SQL oluştur';
  $('#b-cikti').textContent = D.ciktiOk ? 'oluşturuldu' : mod === 'pro' ? 'SiparişMatik Pro' : 'SiparişMatik .sql';
}
for (const b of $$('[data-mod]')) b.onclick = () => { ayarKaydet({ ciktiModu: b.dataset.mod }); modGoster(); };

const ciktiAlanlari = ['sql', 'proDb', 'proKullanici', 'proOpsiyonlar', 'proIcecekOpsiyon', 'proDemoTemizle', 'fiyat', 'kdv', 'seviye'];
for (const id of ciktiAlanlari) {
  $('#' + id).addEventListener('change', () => {
    const e = $('#' + id);
    ayarKaydet({ [id]: e.type === 'checkbox' ? e.checked : id === 'seviye' ? +e.value : e.value });
  });
}
$('#btn-sql-sec').onclick = async () => {
  const [y] = await api.dosya.ac({ baslik: 'SQL yedeği', yol: D.ayar.sql || undefined, filtre: [{ name: 'SQL', extensions: ['sql'] }, { name: 'Hepsi', extensions: ['*'] }] });
  if (y) { $('#sql').value = y; ayarKaydet({ sql: y }); }
};
$('#btn-pro-sec').onclick = async () => {
  const [y] = await api.dosya.ac({ baslik: "Pro'nun siparismatik.db dosyası (AppData\\Roaming\\siparismatik-pro)", filtre: [{ name: 'Pro veritabanı', extensions: ['db'] }, { name: 'Hepsi', extensions: ['*'] }] });
  if (y) { $('#proDb').value = y; ayarKaydet({ proDb: y }); }
};
function kaynakDegisti() { $('#klasor-alan').hidden = $('input[name="kaynak"]:checked').value !== 'klasor'; }
$$('input[name="kaynak"]').forEach((r) => r.addEventListener('change', kaynakDegisti));
$('#btn-klasor-sec').onclick = async () => { const [y] = await api.dosya.ac({ baslik: 'Hazır resim klasörü', ozellik: ['openDirectory'] }); if (y) $('#hazirKlasor').value = y; };
$('#btn-zip-sec').onclick = async () => { const [y] = await api.dosya.ac({ baslik: 'Resim zip dosyası', filtre: [{ name: 'Zip', extensions: ['zip'] }] }); if (y) $('#hazirKlasor').value = y; };

async function ciktiOlustur() {
  const pro = D.ayar.ciktiModu === 'pro';
  const kaynak = pro ? $('#proDb').value.trim() : $('#sql').value.trim();
  if (!kaynak || !(await api.dosya.var(kaynak))) {
    log(pro ? "Önce Pro'nun siparismatik.db dosyasını seç (yeni kurulumun AppData\\Roaming\\siparismatik-pro klasöründen)." : 'Önce müşterinin SQL yedeğini seç.');
    adimaGit('cikti');
    sonucGoster({ hata: pro ? 'Pro veritabanı seçilmedi.' : 'SQL yedeği seçilmedi.' });
    return;
  }
  const klasorModu = $('input[name="kaynak"]:checked').value === 'klasor';
  const hazir = $('#hazirKlasor').value.trim();
  if (klasorModu && !hazir) { sonucGoster({ hata: 'Hazır resim klasörü ya da zip seçilmedi.' }); return; }
  if (!klasorModu && !D.kategoriler.length) { sonucGoster({ hata: 'Ürün listesi boş.' }); return; }
  const firma = await firmaKlasoru();
  const ortak = {
    metin: $('#metin').value, resimKok: D.ayar.cikti, klasor: klasorModu ? hazir : '',
    fiyat: $('#fiyat').value, kdv: $('#kdv').value, seviye: +$('#seviye').value, firma: firma.split(/[\\/]/).pop(),
  };
  try {
    let r;
    if (pro) {
      r = await motor('pro', { ...ortak, db: kaynak, hedef: firma, kullanici: $('#proKullanici').value, opsiyonlar: $('#proOpsiyonlar').value,
        icecekOpsiyon: $('#proIcecekOpsiyon').checked, demoTemizle: $('#proDemoTemizle').checked }, { ad: 'Pro veritabanı yazılıyor' });
    } else {
      r = await motor('sql', { ...ortak, dump: kaynak, hedef: await api.yol.birlestir(firma, await api.yol.ad(kaynak)), sutunlar: D.ayar.sutunlar }, { ad: 'SQL yazılıyor' });
    }
    D.ciktiOk = true;
    sonucGoster({ ...r, pro, firma });
  } catch (e) { sonucGoster({ hata: e.message }); }
  modGoster();
  durumlariGuncelle();
}
$('#btn-olustur').onclick = ciktiOlustur;

function sonucGoster(r) {
  const s = $('#sonuc');
  s.className = 'sonuc ' + (r.hata ? 'hatali' : 'basarili');
  if (r.hata) { s.innerHTML = `<h2>Oluşturulamadı</h2><p>${kacis(r.hata)}</p>`; return; }
  s.innerHTML = `<h2>${r.pro ? 'Pro veritabanı hazır' : 'SQL hazır'}</h2>
    <dl><dt>Kategori</dt><dd>${r.kategori}</dd><dt>Ürün</dt><dd>${r.urun}</dd><dt>Dosya</dt><dd>${kacis(r.hedef)}</dd></dl>
    ${r.eksik.length ? `<p class="eksik">Resmi olmayan ${r.eksik.length} ürün: ${kacis(r.eksik.join(', '))}</p>` : '<p class="eksik">Tüm ürünlerin resmi var.</p>'}
    ${r.pro ? `<p class="ipucu">Kurulum: Pro'yu kapat, AppData\\Roaming\\siparismatik-pro içindeki siparismatik.db, -wal ve -shm dosyalarını yedeğe al, bu siparismatik.db'yi ve product-images klasörünü oraya kopyala, Pro'yu aç.</p>` : ''}
    <div class="sira"><button class="birincil" id="btn-sonuc-ac">Klasörü aç</button></div>`;
  $('#btn-sonuc-ac').onclick = () => api.dosya.goster(r.pro ? r.hedef : r.hedef);
}

$('#btn-demo-temizle').onclick = async () => {
  const [y] = await api.dosya.ac({ baslik: 'Temizlenecek siparismatik.db (Pro KAPALI olmalı)', filtre: [{ name: 'Pro veritabanı', extensions: ['db'] }] });
  if (!y) return;
  const c = await sor('SiparişMatik Pro kapalı mı?\n\nÖrnek siparişler, müşteriler, e-fatura firmaları, kullanıcılar ve yazıcılar silinecek. Yönetici hesabına dokunulmaz. Önce yedek alınır.',
    [{ deger: 'iptal', yazi: 'Vazgeç' }, { deger: 'evet', yazi: 'Demo verilerini sil', birincil: true }]);
  if (c !== 'evet') return;
  try { await motor('pro_demo_temizle', { db: y }, { ad: 'Demo veriler siliniyor' }); } catch { /* */ }
};

// ------------------------------------------------------------------ ayarlar
async function ayarKaydet(yeni) { D.ayar = await api.ayar.kaydet(yeni); }

function ayarlariFormaYaz() {
  const a = D.ayar;
  for (const id of ['geminiAnahtar', 'mistralAnahtar', 'model', 'ollamaModel', 'cikti', 'firmalar']) $('#' + id).value = a[id] || '';
  for (const b of $$('[data-saglayici]')) b.setAttribute('aria-checked', String(b.dataset.saglayici === (a.saglayici || 'gemini')));
  for (const b of $$('[data-tema]')) b.setAttribute('aria-checked', String(b.dataset.tema === (a.tema || 'sistem')));
}
function ayarlariAc(uyari) {
  ayarlariFormaYaz();
  $('#ayar-uyari').hidden = !uyari;
  $('#ayar-uyari').textContent = uyari || '';
  $('#ayarlar').showModal();
  if (uyari) $('#geminiAnahtar').focus();
}
$('#btn-ayarlar').onclick = () => ayarlariAc();
for (const b of $$('[data-saglayici]')) b.onclick = () => $$('[data-saglayici]').forEach((x) => x.setAttribute('aria-checked', String(x === b)));
for (const b of $$('[data-tema]')) b.onclick = () => {
  $$('[data-tema]').forEach((x) => x.setAttribute('aria-checked', String(x === b)));
  temaUygula(b.dataset.tema);
};
for (const b of $$('[data-klasor-sec]')) b.onclick = async () => {
  const [y] = await api.dosya.ac({ baslik: 'Klasör seç', ozellik: ['openDirectory', 'createDirectory'] });
  if (y) $('#' + b.dataset.klasorSec).value = y;
};
$('#ayarlar').addEventListener('close', async () => {
  if ($('#ayarlar').returnValue !== 'kaydet') { temaUygula(D.ayar.tema); return; }
  const yeni = {};
  for (const id of ['geminiAnahtar', 'mistralAnahtar', 'model', 'ollamaModel', 'cikti', 'firmalar']) yeni[id] = $('#' + id).value.trim();
  yeni.saglayici = $('[data-saglayici][aria-checked="true"]')?.dataset.saglayici || 'gemini';
  yeni.tema = $('[data-tema][aria-checked="true"]')?.dataset.tema || 'sistem';
  await ayarKaydet(yeni);
  log('Ayarlar kaydedildi.');
  galeriYukle();
});
$('#btn-eski-aktar').onclick = async () => {
  const r = await api.ayar.eskiAktar();
  if (!r) return;
  D.ayar = r.ayar;
  ayarlariFormaYaz();
  formuDoldur();
  log(`Eski sürümden ${r.sayi} ayar aktarıldı.`);
};
function temaUygula(t) {
  if (t === 'koyu') document.documentElement.dataset.tema = 'koyu';
  else if (t === 'acik') document.documentElement.dataset.tema = 'acik';
  else delete document.documentElement.dataset.tema;
  api.tema(t || 'sistem');
}

function formuDoldur() {
  const a = D.ayar;
  $('#adet').value = a.adet || 1;
  $('#ek').value = a.ek || '';
  $('#klasorArama').checked = !!a.klasorArama;
  $('#otomatikCikti').checked = a.otomatikCikti !== false;
  for (const id of ['sql', 'proDb', 'proKullanici', 'proOpsiyonlar']) $('#' + id).value = a[id] || '';
  $('#proIcecekOpsiyon').checked = !!a.proIcecekOpsiyon;
  $('#proDemoTemizle').checked = a.proDemoTemizle !== false;
  $('#fiyat').value = a.fiyat ?? 1;
  $('#kdv').value = a.kdv ?? 10;
  $('#seviye').value = String(a.seviye || 1);
  temaUygula(a.tema);
  modGoster();
}

// ------------------------------------------------------------------ güncelleme
api.guncelleme.olay((g) => {
  const k = $('#guncelleme');
  if (g.durum === 'iniyor') { k.hidden = false; k.innerHTML = `Yeni sürüm iniyor… %${g.yuzde}`; }
  else if (g.durum === 'var') { k.hidden = false; k.textContent = `v${g.surum} indiriliyor…`; log(`Yeni sürüm bulundu: v${g.surum}`); }
  else if (g.durum === 'hazir') {
    k.hidden = false;
    k.innerHTML = `<span>v${kacis(g.surum)} hazır</span><button class="birincil" id="btn-kur">Yeniden başlat ve kur</button>`;
    $('#btn-kur').onclick = async () => {
      if (D.is && await sor('Süren bir işlem var. Yine de kapatılıp güncellensin mi?', [{ deger: 'iptal', yazi: 'Vazgeç' }, { deger: 'evet', yazi: 'Güncelle', birincil: true }]) !== 'evet') return;
      oturumKaydet();
      api.guncelleme.kur();
    };
    if (g.notlar) log('Yenilikler:\n' + g.notlar);
  } else if (g.durum === 'guncel') $('#ayar-surum').textContent = 'Program güncel.';
  else if (g.durum === 'hata') $('#ayar-surum').textContent = 'Güncelleme denetlenemedi.';
});
$('#btn-guncelleme').onclick = async () => {
  $('#ayar-surum').textContent = 'Denetleniyor…';
  const r = await api.guncelleme.kontrol();
  if (r.durum === 'gelistirme') $('#ayar-surum').textContent = 'Geliştirme sürümünde güncelleme yok.';
  else if (r.durum === 'hata') $('#ayar-surum').textContent = 'Denetlenemedi: ' + r.mesaj;
};

// ------------------------------------------------------------------ genel düğmeler
$('#btn-durdur').onclick = () => { if (D.is) api.motor.iptal(D.is.id); };
$('#btn-firma-klasor').onclick = async () => api.dosya.goster(await firmaKlasoru());
$('#firma').addEventListener('input', oturumKaydetG);
$('#btn-yeni').onclick = async () => {
  if (await sor('Yeni müşteriye başlansın mı? Firma adı, liste ve menü dosyaları temizlenir (resim kütüphanesi ve ayarlar kalır).',
    [{ deger: 'iptal', yazi: 'Vazgeç' }, { deger: 'evet', yazi: 'Temizle', birincil: true }]) !== 'evet') return;
  $('#firma').value = ''; $('#liste').value = ''; $('#metin').value = '';
  D.dosyalar = []; D.kirpmalar = {}; D.galeri.clear(); D.ciktiOk = false;
  $('#hazirKlasor').value = '';
  $('#sonuc').className = 'sonuc';
  $('#sonuc').innerHTML = '<p class="bos">Çıktı, firma klasörüne kaydedilir.</p>';
  $('#ilerleme').hidden = true;
  dosyalariGoster();
  await listeyiYenile();
  adimaGit('kaynak');
};
$('#kayit-ac').onclick = () => {
  const g = $('#kayit-govde');
  g.hidden = !g.hidden;
  $('#kayit-ac').setAttribute('aria-expanded', String(!g.hidden));
  if (!g.hidden) $('#kayit-metin').scrollTop = 1e9;
};
$('#btn-kayit-kopyala').onclick = () => navigator.clipboard.writeText(kayitSatirlari.join('\n'));
$('#btn-kayit-dosya').onclick = () => api.dosya.logAc();
$('#btn-kayit-temizle').onclick = () => { kayitSatirlari.length = 0; $('#kayit-metin').innerHTML = ''; };

document.addEventListener('keydown', (e) => {
  if (e.key === 'Escape' && D.is && !$('dialog[open]')) api.motor.iptal(D.is.id);
});

// ------------------------------------------------------------------ açılış
(async function ac() {
  try {
  D.ayar = await api.ayar.al();
  $('#surum').textContent = 'v' + (await api.surum());
  $('#ayar-surum').textContent = 'Sürüm ' + (await api.surum());
  let o = {};
  try { o = JSON.parse(localStorage.getItem('oturum') || '{}'); } catch { /* */ }
  $('#firma').value = o.firma || '';
  $('#liste').value = o.liste || '';
  $('#metin').value = o.metin || '';
  D.dosyalar = o.dosyalar || [];
  D.kirpmalar = o.kirpmalar || {};
  formuDoldur();
  dosyalariGoster();
  await listeyiYenile();
  adimaGit(o.adim || 'kaynak');
  log('Hazır.');
  const eski = await api.ayar.eskiOtomatik();
  if (eski) {
    D.ayar = await api.ayar.al();
    formuDoldur();
    log(`Eski sürümün ayarları aktarıldı (${eski.sayi} ayar): ${eski.klasor}`);
  }
  if ((D.ayar.saglayici || 'gemini') === 'gemini' && !D.ayar.geminiAnahtar && !D.ayar.mistralAnahtar)
    log('Menü resmi okumak için Ayarlar\'dan ücretsiz Gemini anahtarını gir. Yazılı liste ve Excel anahtarsız da çalışır.');
  console.warn('arayüz hazır');
  } catch (e) { console.error('açılış hatası: ' + (e && e.stack || e)); log('HATA: ' + (e && e.message || e)); }
})();
window.addEventListener('error', (e) => console.error('hata: ' + e.message));
window.addEventListener('unhandledrejection', (e) => console.error('söz hatası: ' + (e.reason && e.reason.message || e.reason)));
