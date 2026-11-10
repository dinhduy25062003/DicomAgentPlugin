Unicode true
!include "MUI2.nsh"
!include "nsDialogs.nsh"
!include "LogicLib.nsh"
!include "x64.nsh"
!ifndef VERSION
!define VERSION "0.1.1"
!endif
!ifndef ROOT
!error "ROOT must be the absolute repository path"
!endif
Name "DICOM Desktop Agent"
OutFile "${ROOT}/publish/installers/DicomAgent-${VERSION}-win-x64-Setup.exe"
InstallDir "$PROGRAMFILES64\DicomAgent"
RequestExecutionLevel admin
SetCompressor /SOLID lzma
SetDatablockOptimize on
ShowInstDetails show
ShowUninstDetails show
Var OperatorControl
Var OperatorUser
Var PsExecutable
!insertmacro MUI_PAGE_WELCOME
Page custom OperatorPage OperatorLeave
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_LANGUAGE "English"

Function .onInit
  ${IfNot} ${RunningX64}
    MessageBox MB_ICONSTOP "This package requires 64-bit Windows."
    Abort
  ${EndIf}
  SetRegView 64
  SetShellVarContext all
  StrCpy $INSTDIR "$PROGRAMFILES64\DicomAgent"
  StrCpy $PsExecutable "$WINDIR\Sysnative\WindowsPowerShell\v1.0\powershell.exe"
  ReadEnvStr $0 USERDOMAIN
  ReadEnvStr $1 USERNAME
  StrCpy $OperatorUser "$0\$1"
FunctionEnd

Function OperatorPage
  !insertmacro MUI_HEADER_TEXT "Local operator account" "This account can read DICOM files and configure the Agent."
  nsDialogs::Create 1018
  Pop $0
  ${NSD_CreateLabel} 0 0 100% 42u "Exit DICOM Desktop Agent from its tray menu before installing or upgrading.$\r$\nEnter the operator account (DOMAIN\user or COMPUTER\user)."
  Pop $0
  ${NSD_CreateText} 0 48u 100% 14u "$OperatorUser"
  Pop $OperatorControl
  ${NSD_CreateLabel} 0 76u 100% 65u "The Host service starts automatically under a restricted service identity. No firewall ports are opened. Configure device allowlists and LAN firewall rules after installation.$\r$\nSign out and sign in again after installation to activate operator access."
  Pop $0
  nsDialogs::Show
FunctionEnd

Function OperatorLeave
  ${NSD_GetText} $OperatorControl $OperatorUser
  ${If} $OperatorUser == ""
    MessageBox MB_ICONSTOP "Enter a Windows operator account."
    Abort
  ${EndIf}
FunctionEnd

Section "DICOM Agent"
  InitPluginsDir
  SetOutPath "$PLUGINSDIR\payload\host"
  File /r /x *.pdb "${ROOT}/publish/windows/host/*"
  SetOutPath "$PLUGINSDIR\payload\desktop"
  File /r /x *.pdb "${ROOT}/publish/windows/desktop/*"
  SetOutPath "$PLUGINSDIR\payload\scu"
  File /r /x *.pdb "${ROOT}/publish/windows/scu/*"
  SetOutPath "$PLUGINSDIR\payload"
  File "${ROOT}/deploy/windows/install.ps1"
  File "${ROOT}/deploy/windows/installer/install-setup.ps1"
  FileOpen $0 "$PLUGINSDIR\operator.txt" w
  FileWriteUTF16LE /BOM $0 "$OperatorUser"
  FileClose $0
  ExecWait '"$PsExecutable" -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "$PLUGINSDIR\payload\install-setup.ps1" -Payload "$PLUGINSDIR\payload" -OperatorFile "$PLUGINSDIR\operator.txt"' $0
  ${If} $0 != 0
    MessageBox MB_ICONSTOP "Installation failed. See %TEMP%\DicomAgent-setup.log. Customer DICOM files have been preserved."
    SetErrorLevel 1
    Abort
  ${EndIf}
  SetOutPath "$INSTDIR"
  File "${ROOT}/deploy/windows/uninstall.ps1"
  WriteUninstaller "$INSTDIR\Uninstall.exe"
  CreateDirectory "$SMPROGRAMS\DICOM Desktop Agent"
  CreateShortCut "$SMPROGRAMS\DICOM Desktop Agent\DICOM Desktop Agent.lnk" "$INSTDIR\desktop\DicomAgent.Desktop.exe"
  CreateShortCut "$SMPROGRAMS\DICOM Desktop Agent\Uninstall.lnk" "$INSTDIR\Uninstall.exe"
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\DicomAgent" "DisplayName" "DICOM Desktop Agent"
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\DicomAgent" "DisplayVersion" "${VERSION}"
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\DicomAgent" "InstallLocation" "$INSTDIR"
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\DicomAgent" "UninstallString" '$\"$INSTDIR\Uninstall.exe$\"'
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\DicomAgent" "DisplayIcon" "$INSTDIR\desktop\DicomAgent.Desktop.exe"
  WriteRegDWORD HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\DicomAgent" "NoModify" 1
  WriteRegDWORD HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\DicomAgent" "NoRepair" 1
SectionEnd

Function un.onInit
  SetRegView 64
  SetShellVarContext all
  StrCpy $INSTDIR "$PROGRAMFILES64\DicomAgent"
  StrCpy $PsExecutable "$WINDIR\Sysnative\WindowsPowerShell\v1.0\powershell.exe"
FunctionEnd

Section "Uninstall"
  ExecWait '"$PsExecutable" -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "$INSTDIR\uninstall.ps1"' $0
  ${If} $0 != 0
    MessageBox MB_ICONSTOP "Service removal failed. No customer data was deleted."
    SetErrorLevel 1
    Abort
  ${EndIf}
  Delete "$SMPROGRAMS\DICOM Desktop Agent\DICOM Desktop Agent.lnk"
  Delete "$SMPROGRAMS\DICOM Desktop Agent\Uninstall.lnk"
  RMDir "$SMPROGRAMS\DICOM Desktop Agent"
  DeleteRegKey HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\DicomAgent"
  Delete "$INSTDIR\uninstall.ps1"
  Delete "$INSTDIR\Uninstall.exe"
  # Preserve host/desktop and simulator folders: they may contain customer files.
  DetailPrint "Service removed. DICOM files, settings, database and application folders have been preserved."
SectionEnd
