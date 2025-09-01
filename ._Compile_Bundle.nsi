!include "LogicLib.nsh"
!include "FileFunc.nsh"

Name "Workstation Onboarding"
OutFile "WS_Setup.exe"
InstallDir "$TEMP\WS_Setup"
RequestExecutionLevel admin
ShowInstDetails show
Icon "Assets\AdvTechLogo.ico"
VIProductVersion "6.7.0.0"
VIAddVersionKey "ProductName" "Workstation Setup"
VIAddVersionKey "FileVersion" "6.7.0.0"
VIAddVersionKey "CompanyName" "Advantage Technologies"
VIAddVersionKey "FileDescription" "Workstation Setup"
VIAddVersionKey "LegalCopyright" "Copyright © 2025 Advantage Technologies"

Section "Main"
  SetOutPath $INSTDIR

  ; === Embed MSI + CAB ===
  File "C:\Users\dan\WS_Setup_6\WS_Setup_6.MSI\Deploy\Release\en-us\WS_Setup_6.MSI.msi"
  File "C:\Users\dan\WS_Setup_6\WS_Setup_6.MSI\Deploy\Release\en-us\cab1.cab"

  ; === Embed all bundle assets ===
  File /r "C:\Users\dan\WS_Setup_6\Assets\*.*"

  ; === Install certificate ===
  DetailPrint "Installing certificate..."
  ExecWait '"$INSTDIR\Assets\InstallCert.exe"'

  ; === Check for .NET Desktop Runtime 8.0.19 ===
  DetailPrint "Checking for .NET Desktop Runtime 8.0.19..."
  ReadRegStr $0 HKLM "SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Microsoft .NET Runtime - 8.0.19 (x64)" "DisplayName"
  ${If} $0 == ""
    DetailPrint "Installing .NET Desktop Runtime..."
    ExecWait '"$INSTDIR\Assets\windowsdesktop-runtime-8.0.19-win-x64.exe" /install /quiet /norestart'
  ${EndIf}

  ; === Run MSI ===
  DetailPrint "Running MSI installer..."
  ExecWait '"msiexec.exe /i "$INSTDIR\WS_Setup_6.msi" /quiet /norestart"'
  
SectionEnd

Section "Create Desktop Shortcut"

  ; Full path to the MSI-installed EXE
  CreateShortCut \
    "$DESKTOP\W_Setup_6.UI.lnk" \
    "C:\Program Files (x86)\Workstation Setup\W_Setup_6.UI.exe" \
    "" \
    "C:\Program Files (x86)\Workstation Setup\W_Setup_6.UI.exe" \
    0 \
    "" \
    "Launch Workstation Setup" \
    "C:\Program Files (x86)\Workstation Setup"

SectionEnd