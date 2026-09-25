@echo off
cd /d "F:\ProjectTotNghiep"

set "PATH=F:\axmol\tools\cmdline;C:\Program Files\CMake\bin;%PATH%"

echo.
echo ===== ProjectTotNghiep Game =====
echo.

where axmol
axmol --version

where cmake
cmake --version

echo.
echo Working directory:
cd

cmd /k