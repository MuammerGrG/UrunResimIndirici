// Arayüze açılan güvenli köprü (window.api)
const { contextBridge, ipcRenderer, webUtils } = require('electron');

const dinle = (kanal) => (fn) => {
  const f = (_e, veri) => fn(veri);
  ipcRenderer.on(kanal, f);
  return () => ipcRenderer.removeListener(kanal, f);
};

contextBridge.exposeInMainWorld('api', {
  ayar: {
    al: () => ipcRenderer.invoke('ayar:al'),
    kaydet: (a) => ipcRenderer.invoke('ayar:kaydet', a),
    eskiAktar: () => ipcRenderer.invoke('ayar:eskiAktar'),
    eskiOtomatik: () => ipcRenderer.invoke('ayar:eskiOtomatik'),
  },
  motor: {
    yeniId: () => ipcRenderer.invoke('motor:yeniId'),
    istek: (komut, arg, id) => ipcRenderer.invoke('motor:istek', { komut, arg, id }),
    iptal: (id) => ipcRenderer.invoke('motor:iptal', id),
    olay: dinle('motor:olay'),
  },
  dosya: {
    ac: (s) => ipcRenderer.invoke('dosya:ac', s),
    kaydetSec: (s) => ipcRenderer.invoke('dosya:kaydetSec', s),
    metinOku: (y) => ipcRenderer.invoke('dosya:metinOku', y),
    metinYaz: (y, m) => ipcRenderer.invoke('dosya:metinYaz', y, m),
    var: (y) => ipcRenderer.invoke('dosya:var', y),
    klasorMu: (y) => ipcRenderer.invoke('dosya:klasorMu', y),
    goster: (y) => ipcRenderer.invoke('dosya:goster', y),
    logAc: () => ipcRenderer.invoke('dosya:logAc'),
    yolu: (f) => webUtils.getPathForFile(f),
  },
  yol: {
    birlestir: (...p) => ipcRenderer.invoke('yol:birlestir', ...p),
    ad: (y) => ipcRenderer.invoke('yol:ad', y),
  },
  disari: (url) => ipcRenderer.invoke('disari', url),
  surum: () => ipcRenderer.invoke('surum'),
  tema: (t) => ipcRenderer.invoke('tema', t),
  guncelleme: {
    kontrol: () => ipcRenderer.invoke('guncelleme:kontrol'),
    kur: () => ipcRenderer.invoke('guncelleme:kur'),
    olay: dinle('guncelleme'),
  },
});
