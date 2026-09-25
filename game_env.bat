@echo off
:: Di chuyển đến thư mục của dự án
cd /d "%~dp0"

:: Cấu hình đường dẫn các công cụ chuyên biệt cho dự án
set "PYTHON_ROOT=F:\"
set "AXMOL_ROOT=F:\axmol"
set "CMAKE_ROOT=C:\Program Files\CMake\bin"

:: Đưa Python 2.7.16, Axmol và CMake lên đầu PATH
:: Biến môi trường chỉ có hiệu lực trong phiên cmd/terminal này (không ảnh hưởng toàn bộ máy)
set "PATH=%PYTHON_ROOT%;%PYTHON_ROOT%Scripts;%AXMOL_ROOT%\tools\cmdline;%CMAKE_ROOT%;%PATH%"

echo ======================================================================
echo             ProjectTotNghiep - Local Game Environment
echo ======================================================================
echo.
echo [1] Python 2.7.16:
where python
python --version
echo.
echo [2] Axmol:
where axmol
call axmol --version
echo.
echo [3] CMake:
where cmake
cmake --version
echo.
echo ======================================================================
echo Working directory: %cd%
echo (Cac cong cu va bien moi truong chi ap dung trong terminal nay)
echo ======================================================================
echo.

:: Giữ cửa sổ terminal mở khi chạy trực tiếp hoặc click đúp
if "%1"=="--no-cmd" goto :eof
cmd /k