// Ürün Resim İndirici — Electron ana süreç
// Arayüz (src/) ↔ preload ↔ burası ↔ Motor.exe (C#, satır satır JSON)
const { app, BrowserWindow, ipcMain, dialog, shell, protocol, net, nativeTheme } = require('electron');
const path = require('path');
const fs = require('fs');
const os = require('os');
const { spawn, execFile } = require('child_process');
const { pathToFileURL } = require('url');

const GITHUB = { owner: 'MuammerGrG', repo: 'UrunResimIndirici' };
let pencere = null;

// ------------------------------------------------------------------ ayarlar
const ayarYolu = () => path.join(app.getPath('userData'), 'ayarlar.json');
const belgeler = () => path.join(app.getPath('documents'), 'UrunResimIndirici');
const VARSAYILAN = () => ({
  saglayici: 'gemini', geminiAnahtar: '', groqAnahtar: '', mistralAnahtar: '', model: '', ollamaModel: '',
  cikti: path.join(belgeler(), 'Urun_Resimleri'), firmalar: path.join(belgeler(), 'Firmalar'),
  ek: '', adet: 1, klasorArama: false, otomatikCikti: true,
  ciktiModu: 'sql', sql: '', proDb: '', proKullanici: os.userInfo().username || '',
  proOpsiyonlar: 'Soğanlı, Soğansız, Domatesli, Domatessiz, Marullu, Marulsuz, Soslu, Sossuz, Patatesli, Patatessiz, Turşulu, Turşusuz, Mayonezli, Mayonezsiz, Ketçaplı, Ketçapsız, Acılı, Acısız',
  proIcecekOpsiyon: false, proDemoTemizle: true, fiyat: 1, kdv: 10, seviye: 1, sutunlar: null,
  tema: 'sistem', eskiAktarildi: false,
});
let ayar = VARSAYILAN();

function ayarOku() {
  try { ayar = { ...VARSAYILAN(), ...JSON.parse(fs.readFileSync(ayarYolu(), 'utf8')) }; } catch { ayar = VARSAYILAN(); }
}
function ayarYaz() {
  try {
    fs.mkdirSync(path.dirname(ayarYolu()), { recursive: true });
    fs.writeFileSync(ayarYolu(), JSON.stringify(ayar, null, 2), 'utf8');
  } catch (e) { logDosya('Ayar yazılamadı: ' + e.message); }
}

// Eski (Python, v3.x) sürümün ayarlar.json'unu içe aktarır
function eskiAyarlariAktar(json, klasor) {
  const e = JSON.parse(json);
  const aktar = {
    geminiAnahtar: e.gemini_key, mistralAnahtar: e.mistral_key, saglayici: e.saglayici, model: e.gemini_model,
    ollamaModel: e.ollama_model, ek: e.ek, adet: e.adet, klasorArama: e.klasor_arama,
    ciktiModu: e.cikti_modu, sql: e.sql, proDb: e.pro_db, proKullanici: e.pro_kullanici, proOpsiyonlar: e.pro_opsiyonlar,
    proIcecekOpsiyon: e.pro_icecek_opsiyon, proDemoTemizle: e.pro_demo_temizle, fiyat: e.fiyat, kdv: e.kdv,
    seviye: e.kategori_seviye, sutunlar: e.sql_sutunlar,
  };
  const cikti = e.cikti || (klasor && path.join(klasor, 'Urun_Resimleri'));
  if (cikti && fs.existsSync(cikti)) aktar.cikti = cikti;
  const firmalar = e.firmalar || (klasor && path.join(klasor, 'Firmalar'));
  if (firmalar && fs.existsSync(firmalar)) aktar.firmalar = firmalar;
  let n = 0;
  for (const [k, v] of Object.entries(aktar)) if (v !== undefined && v !== null && v !== '') { ayar[k] = v; n++; }
  ayar.eskiAktarildi = true;
  ayarYaz();
  return n;
}

function kurulumKaynagi() {
  return new Promise((coz) => {
    if (process.platform !== 'win32') return coz('');
    execFile('reg', ['query', 'HKCU\\Software\\UrunResimIndirici', '/v', 'KurulumKaynagi'], { windowsHide: true }, (h, cikti) => {
      const m = !h && /KurulumKaynagi\s+REG_SZ\s+(.+)/.exec(cikti || '');
      coz(m ? m[1].trim() : '');
    });
  });
}

async function eskiyiOtomatikBul() {
  if (ayar.eskiAktarildi) return null;
  const adaylar = [];
  const kaynak = await kurulumKaynagi();
  if (kaynak) adaylar.push(kaynak);
  for (const k of ['desktop', 'downloads', 'documents']) {
    try {
      const d = app.getPath(k);
      adaylar.push(d, path.join(d, 'UrunResimIndirici'), path.join(d, 'Ürün Resim İndirici'));
    } catch { /* yok */ }
  }
  for (const k of adaylar) {
    const y = path.join(k, 'ayarlar.json');
    try {
      const json = fs.readFileSync(y, 'utf8');
      const e = JSON.parse(json);
      if (!('gemini_key' in e) && !('cikti' in e) && !('sql' in e)) continue;
      const n = eskiAyarlariAktar(json, k);
      return { klasor: k, sayi: n };
    } catch { /* yok */ }
  }
  ayar.eskiAktarildi = true;
  ayarYaz();
  return null;
}

// ------------------------------------------------------------------ log dosyası
const logYolu = () => path.join(app.getPath('userData'), 'son_log.txt');
function logDosya(m) {
  try { fs.appendFileSync(logYolu(), new Date().toTimeString().slice(0, 8) + ' ' + m + '\n', 'utf8'); } catch { /* */ }
}

// ------------------------------------------------------------------ motor
const motor = { surec: null, sayac: 0, bekleyen: new Map(), tampon: '', hazir: null };

function motorYolu() {
  if (app.isPackaged) return path.join(process.resourcesPath, 'motor', 'Motor.exe');
  return process.env.MOTOR || path.join(__dirname, '..', 'motor-cikti', process.platform === 'win32' ? 'Motor.exe' : 'Motor');
}

function motorBaslat() {
  const yol = motorYolu();
  motor.tampon = '';
  let hazirCoz;
  motor.hazir = new Promise((c) => { hazirCoz = c; });
  const s = spawn(yol, [], { windowsHide: true, stdio: ['pipe', 'pipe', 'pipe'] });
  motor.surec = s;
  s.stdout.setEncoding('utf8');
  s.stdout.on('data', (parca) => {
    motor.tampon += parca;
    let i;
    while ((i = motor.tampon.indexOf('\n')) >= 0) {
      const satir = motor.tampon.slice(0, i).trim();
      motor.tampon = motor.tampon.slice(i + 1);
      if (!satir) continue;
      let m;
      try { m = JSON.parse(satir); } catch { logDosya('motor: ' + satir); continue; }
      if (m.olay === 'hazir') { hazirCoz(m); continue; }
      if (m.olay) {
        if (m.olay === 'log') logDosya(m.metin);
        pencere?.webContents.send('motor:olay', m);
        continue;
      }
      const b = motor.bekleyen.get(m.id);
      if (!b) continue;
      motor.bekleyen.delete(m.id);
      if (m.tamam) b.coz(m.sonuc);
      else {
        if (m.ayrinti) logDosya(m.ayrinti);
        b.reddet({ hata: m.hata, kod: m.kod || '' });
      }
    }
  });
  s.stderr.setEncoding('utf8');
  s.stderr.on('data', (d) => logDosya('motor hata: ' + d));
  s.on('error', (e) => {
    logDosya('Motor başlatılamadı: ' + e.message);
    hazirCoz({ hata: e.message });
  });
  s.on('exit', (kod) => {
    logDosya('Motor kapandı: ' + kod);
    for (const b of motor.bekleyen.values()) b.reddet({ hata: 'Motor beklenmedik şekilde kapandı; işlemi tekrar başlat.', kod: 'motor' });
    motor.bekleyen.clear();
    motor.surec = null;
  });
}

async function motorIstek(komut, arg, id) {
  if (!motor.surec) motorBaslat();
  const h = await motor.hazir;
  if (h && h.hata) throw { hata: 'Motor başlatılamadı: ' + h.hata, kod: 'motor' };
  return new Promise((coz, reddet) => {
    motor.bekleyen.set(id, { coz, reddet });
    motor.surec.stdin.write(JSON.stringify({ id, komut, arg }) + '\n');
  });
}

// ------------------------------------------------------------------ pencere
protocol.registerSchemesAsPrivileged([{ scheme: 'resim', privileges: { standard: true, secure: true, supportFetchAPI: true, stream: true } }]);

function pencereAc() {
  pencere = new BrowserWindow({
    width: 1320, height: 860, minWidth: 980, minHeight: 640,
    title: 'Ürün Resim İndirici', backgroundColor: nativeTheme.shouldUseDarkColors ? '#14171c' : '#eef1ef',
    autoHideMenuBar: true, show: false,
    icon: path.join(__dirname, 'build', 'icon.ico'),
    webPreferences: { preload: path.join(__dirname, 'preload.js'), contextIsolation: true, nodeIntegration: false, sandbox: true, spellcheck: false },
  });
  pencere.setMenu(null);
  pencere.once('ready-to-show', () => pencere.show());
  pencere.loadFile(path.join(__dirname, 'src', 'index.html'));
  pencere.webContents.setWindowOpenHandler(({ url }) => {
    if (/^https:\/\//.test(url)) shell.openExternal(url);
    return { action: 'deny' };
  });
  pencere.webContents.on('will-navigate', (e) => e.preventDefault());
  pencere.webContents.on('console-message', (_e, seviye, mesaj, satir, kaynak) => {
    if (seviye >= 2) logDosya(`arayüz: ${mesaj} (${path.basename(kaynak || '')}:${satir})`);
  });
  pencere.webContents.on('preload-error', (_e, yol, hata) => logDosya(`preload hatası: ${hata?.message || hata}`));
  pencere.webContents.on('render-process-gone', (_e, d) => logDosya(`arayüz kapandı: ${d.reason}`));
}

// ------------------------------------------------------------------ güncelleme
let guncelleyici = null;
function guncellemeKur() {
  if (!app.isPackaged) return;
  try {
    guncelleyici = require('electron-updater').autoUpdater;
  } catch (e) { logDosya('Güncelleyici yüklenemedi: ' + e.message); return; }
  guncelleyici.autoDownload = true;
  guncelleyici.autoInstallOnAppQuit = true;
  const gonder = (durum, veri = {}) => pencere?.webContents.send('guncelleme', { durum, ...veri });
  guncelleyici.on('update-available', (i) => gonder('var', { surum: i.version, notlar: notTemizle(i.releaseNotes) }));
  guncelleyici.on('update-not-available', () => gonder('guncel'));
  guncelleyici.on('download-progress', (p) => gonder('iniyor', { yuzde: Math.round(p.percent) }));
  guncelleyici.on('update-downloaded', (i) => gonder('hazir', { surum: i.version, notlar: notTemizle(i.releaseNotes) }));
  guncelleyici.on('error', (e) => { logDosya('Güncelleme: ' + String(e?.message || e).split('\n')[0]); gonder('hata', { mesaj: String(e?.message || e).slice(0, 200) }); });
  setTimeout(() => guncelleyici.checkForUpdates().catch(() => {}), 4000);
}
function notTemizle(n) {
  const metin = Array.isArray(n) ? n.map((x) => x.note || '').join('\n') : String(n || '');
  return metin.replace(/<[^>]+>/g, '\n').split('\n')
    .filter((x) => !/^\s*(co-authored-by|claude-session|signed-off-by)\s*:/i.test(x) && !x.includes('claude.ai/code'))
    .join('\n').replace(/\n{3,}/g, '\n\n').trim().slice(0, 800);
}

// ------------------------------------------------------------------ IPC
function ipc() {
  ipcMain.handle('ayar:al', () => ({ ...ayar }));
  ipcMain.handle('ayar:kaydet', (_e, yeni) => { ayar = { ...ayar, ...yeni }; ayarYaz(); return { ...ayar }; });
  ipcMain.handle('ayar:eskiAktar', async () => {
    const r = await dialog.showOpenDialog(pencere, { title: 'Eski sürümün ayarlar.json dosyası', filters: [{ name: 'Ayarlar', extensions: ['json'] }], properties: ['openFile'] });
    if (r.canceled || !r.filePaths[0]) return null;
    const n = eskiAyarlariAktar(fs.readFileSync(r.filePaths[0], 'utf8'), path.dirname(r.filePaths[0]));
    return { sayi: n, ayar: { ...ayar } };
  });
  ipcMain.handle('ayar:eskiOtomatik', () => eskiyiOtomatikBul());

  let sayac = 0;
  ipcMain.handle('motor:istek', async (_e, { komut, arg, id }) => {
    const i = id || ++sayac + 1000000;
    try { return { tamam: true, sonuc: await motorIstek(komut, arg || {}, i) }; } catch (h) { return { tamam: false, ...(h.hata ? h : { hata: String(h) }) }; }
  });
  ipcMain.handle('motor:yeniId', () => ++sayac);
  ipcMain.handle('motor:iptal', (_e, id) => { motor.surec?.stdin.write(JSON.stringify({ id: 0, komut: 'iptal', arg: { id } }) + '\n'); });

  ipcMain.handle('dosya:ac', async (_e, s = {}) => {
    const r = await dialog.showOpenDialog(pencere, { title: s.baslik, defaultPath: s.yol || undefined, filters: s.filtre, properties: s.ozellik || ['openFile'] });
    return r.canceled ? [] : r.filePaths;
  });
  ipcMain.handle('dosya:kaydetSec', async (_e, s = {}) => {
    const r = await dialog.showSaveDialog(pencere, { title: s.baslik, defaultPath: s.yol, filters: s.filtre });
    return r.canceled ? '' : r.filePath;
  });
  ipcMain.handle('dosya:metinOku', (_e, y) => fs.readFileSync(y, 'utf8'));
  ipcMain.handle('dosya:metinYaz', (_e, y, m) => { fs.mkdirSync(path.dirname(y), { recursive: true }); fs.writeFileSync(y, m, 'utf8'); return true; });
  ipcMain.handle('dosya:var', (_e, y) => !!y && fs.existsSync(y));
  ipcMain.handle('dosya:klasorMu', (_e, y) => { try { return fs.statSync(y).isDirectory(); } catch { return false; } });
  ipcMain.handle('dosya:goster', (_e, y) => {
    if (!y) return;
    if (fs.existsSync(y) && fs.statSync(y).isDirectory()) shell.openPath(y);
    else if (fs.existsSync(y)) shell.showItemInFolder(y);
    else { fs.mkdirSync(y, { recursive: true }); shell.openPath(y); }
  });
  ipcMain.handle('dosya:logAc', () => shell.openPath(logYolu()));
  ipcMain.handle('yol:birlestir', (_e, ...p) => path.join(...p));
  ipcMain.handle('yol:ad', (_e, y) => path.basename(y || ''));
  ipcMain.handle('disari', (_e, url) => { if (/^https:\/\//.test(url)) shell.openExternal(url); });
  ipcMain.handle('surum', () => app.getVersion());
  ipcMain.handle('guncelleme:kontrol', async () => {
    if (!guncelleyici) return { durum: 'gelistirme' };
    try { await guncelleyici.checkForUpdates(); return { durum: 'soruldu' }; } catch (e) { return { durum: 'hata', mesaj: e.message }; }
  });
  ipcMain.handle('guncelleme:kur', () => { guncelleyici?.quitAndInstall(false, true); });
  ipcMain.handle('tema', (_e, t) => { nativeTheme.themeSource = t === 'sistem' ? 'system' : t === 'koyu' ? 'dark' : 'light'; });
}

// ------------------------------------------------------------------ başlat
if (!app.requestSingleInstanceLock()) app.quit();
else {
  app.on('second-instance', () => { if (pencere) { if (pencere.isMinimized()) pencere.restore(); pencere.focus(); } });
  app.whenReady().then(() => {
    ayarOku();
    try { fs.writeFileSync(logYolu(), `Ürün Resim İndirici v${app.getVersion()}  ${new Date().toLocaleString('tr-TR')}\n`, 'utf8'); } catch { /* */ }
    nativeTheme.themeSource = ayar.tema === 'koyu' ? 'dark' : ayar.tema === 'acik' ? 'light' : 'system';
    protocol.handle('resim', (istek) => {
      const u = new URL(istek.url);
      const yol = decodeURIComponent(u.pathname.replace(/^\//, ''));
      return net.fetch(pathToFileURL(yol).toString());
    });
    ipc();
    motorBaslat();
    pencereAc();
    guncellemeKur();
  });
  app.on('window-all-closed', () => {
    try { motor.surec?.stdin.write(JSON.stringify({ id: 0, komut: 'kapat', arg: {} }) + '\n'); } catch { /* */ }
    app.quit();
  });
}
