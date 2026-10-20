@echo off
rem Xem bot luat (trai) danh voi bot ML (phai) co cua so. Khong sua code, khong anh huong run.bat.
rem usage: run_botvsbot.bat [xac_suat_nhay_sau_Set]   vd: run_botvsbot.bat 1.0  (mac dinh 0.314)
rem Log ghi vao Logs\auto\xem_truc_tiep, KHONG tron vao Logs\v1.
set myDir=%~dp0
cd /d %myDir%
set BUILD_CFG=RelWithDebInfo
if exist %myDir%run.bat.txt set /p BUILD_CFG=< %myDir%run.bat.txt
set SPIKE_BOT_VS_BOT=1
set SPIKE_JUMP_AFTER_SET=%1
start /D %myDir%Content build/bin/MyGame/%BUILD_CFG%/MyGame.exe
