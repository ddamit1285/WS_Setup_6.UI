@echo off
setlocal

REM Install to Local Machine Root CA
certutil -f -p "St@ff1234!" -importpfx "%~dp0CodeSign_Expires_20260709.pfx" NoRoot

REM Import public portion into Trusted Root
certutil -addstore -f Root "%~dp0CodeSign_Expires_20260709.pfx"

REM Import public portion into Trusted Publishers
certutil -addstore -f TrustedPublisher "%~dp0CodeSign_Expires_20260709.pfx"

endlocal
exit /b 0