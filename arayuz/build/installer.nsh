; Kurulum hangi klasörden başlatıldıysa (eski sürümün güncelleyicisi kurulumu kendi klasörüne indirir)
; orayı kaydet: program ilk açılışta oradaki ayarlar.json'u ve resim kütüphanesini bulur.
!macro customInstall
  WriteRegStr HKCU "Software\UrunResimIndirici" "KurulumKaynagi" "$EXEDIR"
!macroend
