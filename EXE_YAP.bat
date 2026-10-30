@echo off
chcp 65001 >nul
cd /d "%~dp0"
set "SESSIZ="
if /i "%~1"=="/sessiz" set "SESSIZ=1"
echo ================================================
echo   UrunResimIndirici.exe olusturuluyor...
echo ================================================
where python >nul 2>&1
if errorlevel 1 (
    echo.
    echo Python yok. Once python.org'dan Python 3 kur.
    echo Kurulumda "Add python.exe to PATH" kutusunu ISARETLE.
    start https://www.python.org/downloads/windows/
    if not defined SESSIZ pause
    exit /b 1
)
python -m pip install --quiet --upgrade pip
python -m pip install --quiet --upgrade ddgs pillow requests pyinstaller
if exist "UrunResimIndirici.exe" del /q "UrunResimIndirici.exe" >nul 2>&1
python -m PyInstaller --noconfirm --clean --onefile --windowed ^
    --name "UrunResimIndirici" ^
    --collect-all ddgs ^
    UrunResimIndirici.py
if exist "dist\UrunResimIndirici.exe" (
    copy /y "dist\UrunResimIndirici.exe" "UrunResimIndirici.exe" >nul
    rmdir /s /q build dist >nul 2>&1
    del /q UrunResimIndirici.spec >nul 2>&1
    echo.
    echo HAZIR: UrunResimIndirici.exe
    if not defined SESSIZ (
        echo Artik sadece bu exe yeterli, istedigin bilgisayara kopyalayabilirsin.
        explorer /select,"%~dp0UrunResimIndirici.exe"
    )
) else (
    echo.
    echo EXE olusturulamadi. Yukaridaki hatanin ekran goruntusunu gonder.
)
if not defined SESSIZ pause
