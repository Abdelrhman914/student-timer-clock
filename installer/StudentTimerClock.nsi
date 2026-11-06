; Student Timer Clock 0.3 - Windows installer (NSIS 3, Modern UI 2)
; Build:  makensis -DPAYLOAD=<publish folder> StudentTimerClock.nsi
; Per-user install (no admin rights needed), starts with Windows by default,
; Terms & Conditions must be accepted (checkbox) before installing.

Unicode true
SetCompressor /SOLID lzma
SetCompressorDictSize 64

!ifndef PAYLOAD
  !define PAYLOAD "..\publish"
!endif
!ifndef OUTFILE
  !define OUTFILE "StudentTimerClock version 0.3.exe"
!endif

!define APPNAME     "Student Timer Clock"
!define APPKEY      "StudentTimerClock"
!define APPEXE      "StudentTimerClock.exe"
!define VERSION     "0.3"
!define PUBLISHER   "Student Timer Clock"
!define UNINSTKEY   "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APPKEY}"
!define RUNKEY      "Software\Microsoft\Windows\CurrentVersion\Run"

; code signing of the installer and of the generated uninstaller (NSIS 3.08+)
!ifdef SIGNCMD
  !finalize '${SIGNCMD} "%1"' = 0
  !uninstfinalize '${SIGNCMD} "%1"' = 0
!endif

Name "${APPNAME} ${VERSION}"
OutFile "${OUTFILE}"
InstallDir "$LOCALAPPDATA\Programs\${APPKEY}"
InstallDirRegKey HKCU "Software\${APPKEY}" "InstallDir"
RequestExecutionLevel user
ShowInstDetails show
ShowUninstDetails show
BrandingText "${APPNAME} ${VERSION}"

VIProductVersion "0.3.0.0"
VIAddVersionKey "ProductName"     "${APPNAME}"
VIAddVersionKey "ProductVersion"  "${VERSION}"
VIAddVersionKey "FileVersion"     "0.3.0.0"
VIAddVersionKey "FileDescription" "${APPNAME} Setup"
VIAddVersionKey "CompanyName"     "${PUBLISHER}"
VIAddVersionKey "LegalCopyright"  "2026"

!include "MUI2.nsh"
!include "LogicLib.nsh"
!include "FileFunc.nsh"
!include "Sections.nsh"

!define MUI_ICON   "..\src\app.ico"
!define MUI_UNICON "..\src\app.ico"
!define MUI_ABORTWARNING
!define MUI_WELCOMEFINISHPAGE_BITMAP   "sidebar.bmp"
!define MUI_UNWELCOMEFINISHPAGE_BITMAP "sidebar.bmp"
!define MUI_HEADERIMAGE
!define MUI_HEADERIMAGE_RIGHT
!define MUI_HEADERIMAGE_BITMAP   "header.bmp"
!define MUI_HEADERIMAGE_UNBITMAP "header.bmp"
!define MUI_COMPONENTSPAGE_SMALLDESC

; ---------------------------------------------------------------- pages
!define MUI_WELCOMEPAGE_TITLE "Welcome to ${APPNAME} ${VERSION} Setup"
!define MUI_WELCOMEPAGE_TEXT "This will install ${APPNAME} on your computer.$\r$\n$\r$\nTimers, exam countdowns, stopwatches with named laps, customisable desktop widgets, a fullscreen classroom board and study statistics.$\r$\n$\r$\nNo administrator rights are needed - it installs just for you.$\r$\n$\r$\nClick Next to continue."
!insertmacro MUI_PAGE_WELCOME

!define MUI_PAGE_HEADER_TEXT "Terms and Conditions"
!define MUI_PAGE_HEADER_SUBTEXT "Please read and accept the terms before installing ${APPNAME}."
!define MUI_LICENSEPAGE_TEXT_TOP "Scroll down to read all of the terms and conditions."
!define MUI_LICENSEPAGE_TEXT_BOTTOM "You must tick the box below to accept the terms and conditions. Setup cannot continue until you do."
!define MUI_LICENSEPAGE_CHECKBOX
!define MUI_LICENSEPAGE_CHECKBOX_TEXT "I have read and accept the terms and conditions"
!insertmacro MUI_PAGE_LICENSE "terms.txt"

!insertmacro MUI_PAGE_DIRECTORY

!define MUI_PAGE_HEADER_SUBTEXT "Choose shortcuts and whether ${APPNAME} starts with Windows."
!define MUI_COMPONENTSPAGE_TEXT_TOP "Start automatically with Windows is recommended so your timers and reminders are always running. You can change this later in File > Settings."
!insertmacro MUI_PAGE_COMPONENTS

!insertmacro MUI_PAGE_INSTFILES

!define MUI_FINISHPAGE_TITLE "${APPNAME} is installed"
!define MUI_FINISHPAGE_RUN "$INSTDIR\${APPEXE}"
!define MUI_FINISHPAGE_RUN_TEXT "Start ${APPNAME} now"
!define MUI_FINISHPAGE_SHOWREADME ""
!define MUI_FINISHPAGE_SHOWREADME_NOTCHECKED
!define MUI_FINISHPAGE_SHOWREADME_TEXT "Show the terms and conditions"
!define MUI_FINISHPAGE_SHOWREADME_FUNCTION ShowTerms
!insertmacro MUI_PAGE_FINISH

!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES

!insertmacro MUI_LANGUAGE "English"

; ---------------------------------------------------------------- helpers
!macro CloseRunningApp
  DetailPrint "Closing ${APPNAME} if it is running..."
  nsExec::Exec 'taskkill /F /IM ${APPEXE}'
  Pop $0
  Sleep 800
!macroend

Function ShowTerms
  ExecShell "open" "$INSTDIR\terms.txt"
FunctionEnd

; ---------------------------------------------------------------- sections
Section "!${APPNAME} (required)" SecApp
  SectionIn RO
  !insertmacro CloseRunningApp
  SetOutPath "$INSTDIR"
  ; remove files of an older version first (keeps user data, which lives in %APPDATA%).
  ; 0.1 shipped ~240 loose runtime files; 0.3 is a single signed exe and its integrity check
  ; rejects unknown DLLs in the program folder, so old files must go.
  ${If} ${FileExists} "$INSTDIR\${APPEXE}"
    Delete "$INSTDIR\*.dll"
    Delete "$INSTDIR\*.json"
    Delete "$INSTDIR\*.pdb"
    Delete "$INSTDIR\integrity.sig"
    Delete "$INSTDIR\createdump.exe"
    RMDir /r "$INSTDIR\runtimes"
  ${EndIf}
  File /r "${PAYLOAD}\*.*"
  File "terms.txt"

  WriteUninstaller "$INSTDIR\Uninstall.exe"
  WriteRegStr HKCU "Software\${APPKEY}" "InstallDir" "$INSTDIR"
  WriteRegStr HKCU "Software\${APPKEY}" "Version" "${VERSION}"
  WriteRegDWORD HKCU "Software\${APPKEY}" "TermsAccepted" 1
  ; assume "no autostart" until the autostart section (below) says otherwise
  WriteRegDWORD HKCU "Software\${APPKEY}" "AutostartChoice" 0

  ; Add/Remove Programs entry
  WriteRegStr   HKCU "${UNINSTKEY}" "DisplayName"     "${APPNAME}"
  WriteRegStr   HKCU "${UNINSTKEY}" "DisplayVersion"  "${VERSION}"
  WriteRegStr   HKCU "${UNINSTKEY}" "Publisher"       "${PUBLISHER}"
  WriteRegStr   HKCU "${UNINSTKEY}" "DisplayIcon"     "$INSTDIR\${APPEXE}"
  WriteRegStr   HKCU "${UNINSTKEY}" "InstallLocation" "$INSTDIR"
  WriteRegStr   HKCU "${UNINSTKEY}" "UninstallString" '"$INSTDIR\Uninstall.exe"'
  WriteRegStr   HKCU "${UNINSTKEY}" "QuietUninstallString" '"$INSTDIR\Uninstall.exe" /S'
  WriteRegDWORD HKCU "${UNINSTKEY}" "NoModify" 1
  WriteRegDWORD HKCU "${UNINSTKEY}" "NoRepair" 1
  ${GetSize} "$INSTDIR" "/S=0K" $0 $1 $2
  IntFmt $0 "0x%08X" $0
  WriteRegDWORD HKCU "${UNINSTKEY}" "EstimatedSize" "$0"
SectionEnd

Section "Start automatically with Windows" SecAutostart
  WriteRegStr HKCU "${RUNKEY}" "${APPKEY}" '"$INSTDIR\${APPEXE}" --autostart'
  WriteRegDWORD HKCU "Software\${APPKEY}" "AutostartChoice" 1
SectionEnd

Section "Start Menu shortcut" SecStartMenu
  CreateDirectory "$SMPROGRAMS\${APPNAME}"
  CreateShortcut "$SMPROGRAMS\${APPNAME}\${APPNAME}.lnk" "$INSTDIR\${APPEXE}" "" "$INSTDIR\${APPEXE}" 0
  CreateShortcut "$SMPROGRAMS\${APPNAME}\Uninstall ${APPNAME}.lnk" "$INSTDIR\Uninstall.exe"
SectionEnd

Section "Desktop shortcut" SecDesktop
  CreateShortcut "$DESKTOP\${APPNAME}.lnk" "$INSTDIR\${APPEXE}" "" "$INSTDIR\${APPEXE}" 0
SectionEnd

; if the user unticked autostart: make sure no old Run entry is left behind
Section "-AutostartCleanup"
  ${IfNot} ${SectionIsSelected} ${SecAutostart}
    DeleteRegValue HKCU "${RUNKEY}" "${APPKEY}"
  ${EndIf}
SectionEnd

!insertmacro MUI_FUNCTION_DESCRIPTION_BEGIN
  !insertmacro MUI_DESCRIPTION_TEXT ${SecApp}       "The ${APPNAME} program (includes everything it needs - no separate .NET install required)."
  !insertmacro MUI_DESCRIPTION_TEXT ${SecAutostart} "Recommended. Starts ${APPNAME} (quietly, in the tray if you choose so in Settings) when you sign in to Windows, so timers and reminders keep working."
  !insertmacro MUI_DESCRIPTION_TEXT ${SecStartMenu} "Adds ${APPNAME} to the Start menu."
  !insertmacro MUI_DESCRIPTION_TEXT ${SecDesktop}   "Puts a ${APPNAME} icon on your desktop."
!insertmacro MUI_FUNCTION_DESCRIPTION_END

; ---------------------------------------------------------------- uninstaller
Section "Uninstall"
  !insertmacro CloseRunningApp
  DeleteRegValue HKCU "${RUNKEY}" "${APPKEY}"
  Delete "$DESKTOP\${APPNAME}.lnk"
  RMDir /r "$SMPROGRAMS\${APPNAME}"
  RMDir /r "$INSTDIR"
  DeleteRegKey HKCU "${UNINSTKEY}"
  DeleteRegKey HKCU "Software\${APPKEY}"

  IfSilent done
  MessageBox MB_YESNO|MB_ICONQUESTION "Also delete your timers, stopwatches and statistics?$\r$\n$\r$\n($APPDATA\${APPKEY})$\r$\n$\r$\nChoose No to keep them in case you install ${APPNAME} again.$\r$\n$\r$\nYour backup files (Documents\Student Timer Clock Backups) are never deleted." /SD IDNO IDNO done
    RMDir /r "$APPDATA\${APPKEY}"
  done:
SectionEnd
