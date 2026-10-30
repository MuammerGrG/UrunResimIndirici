@echo off
setlocal EnableDelayedExpansion
chcp 65001 >nul
cd /d "%~dp0"
title Urun Resim Indirici - Guncelle

rem ==== GitHub deposu (degistirirsen programdaki ayarlar.json > github_repo ile ayni olsun)
set "REPO=MuammerGrG/UrunResimIndirici"

echo ================================================
echo   Urun Resim Indirici - GITHUB'DAN GUNCELLE
echo ================================================
echo.
echo Son surum indiriliyor: github.com/%REPO%/releases
set "YENI=%~dp0UrunResimIndirici_yeni.exe"
if exist "!YENI!" del /q "!YENI!"
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "$ProgressPreference='SilentlyContinue'; [Net.ServicePointManager]::SecurityProtocol=[Net.SecurityProtocolType]::Tls12;" ^
  "try { Invoke-WebRequest -UseBasicParsing -Uri 'https://github.com/%REPO%/releases/latest/download/UrunResimIndirici.exe' -OutFile '!YENI!'; exit 0 } catch { Write-Host $_.Exception.Message; exit 1 }"
if errorlevel 1 goto hata
if not exist "!YENI!" goto hata
for %%A in ("!YENI!") do if %%~zA LSS 1000000 goto hata

rem ---- acik programi kapat
tasklist /fi "imagename eq UrunResimIndirici.exe" | find /i "UrunResimIndirici.exe" >nul && (
    echo Acik olan program kapatiliyor...
    taskkill /im UrunResimIndirici.exe /f >nul 2>&1
    timeout /t 2 /nobreak >nul
)

rem ---- eskisini yedekle, yenisini koy (ayarlar.json, resimler, firmalar KORUNUR)
if not exist "eski_surum" mkdir "eski_surum"
if exist "UrunResimIndirici.exe" move /y "UrunResimIndirici.exe" "eski_surum\UrunResimIndirici_onceki.exe" >nul
move /y "!YENI!" "UrunResimIndirici.exe" >nul
if not exist "UrunResimIndirici.exe" goto hata

echo.
echo ================================================
echo   GUNCELLEME TAMAM - program aciliyor
echo ================================================
start "" "%~dp0UrunResimIndirici.exe"
timeout /t 3 >nul
exit /b 0

:hata
echo.
echo Guncelleme indirilemedi.
echo  - Internet baglantisini kontrol et
echo  - github.com/%REPO% deposu "Public" olmali
echo  - Actions sekmesinde derleme bitmis olmali (Releases'te exe gorunmeli)
if exist "!YENI!" del /q "!YENI!"
echo.
pause
exit /b 1
