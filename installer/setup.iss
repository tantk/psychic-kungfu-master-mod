; Inno Setup script for JinGu Cheats
;
; Compile with:  iscc.exe installer\setup.iss
; Output:        release\JinGuCheats-Setup-v<version>.exe
;
; The script does what install.bat used to do, but with a real wizard UI:
;   - auto-detect game folder via Steam registry, with manual fallback
;   - extract bundled MelonLoader.x64.zip into the game folder
;   - patch the four stripped corlibs via MelonLoader's MonoBleedingEdgePatches
;   - copy plugin + UI exe + pre-launch scripts into Mods\
;   - optional: offer to set the Steam Launch Option on the finish page
;
; Inno's wizard handles UAC elevation, folder picker, license display,
; progress bar, and error dialogs — none of which the bat could do well.

#define MyAppName        "JinGu Cheats"
#define MyAppPublisher   "JinGu Cheats contributors"
#define MyAppURL         "https://github.com/tantk/psychic-kungfu-master-mod"
#ifndef MyAppVersion
#define MyAppVersion     "0.1.0"
#endif

[Setup]
AppId={{2C5B9F44-7E2C-4A6F-9B1A-3E5D6FCA0001}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}/issues
AppUpdatesURL={#MyAppURL}/releases
; Detected/picked at runtime; the "DefaultDirName" is just the picker's initial value.
DefaultDirName={code:DefaultGameDir}
DisableDirPage=no
DirExistsWarning=no
AppendDefaultDirName=no
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=admin
PrivilegesRequiredOverridesAllowed=dialog
OutputBaseFilename=JinGuCheats-Setup-v{#MyAppVersion}
OutputDir=..\release
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
SetupIconFile=..\ui\src-tauri\icons\icon.ico
ShowLanguageDialog=no
ChangesAssociations=no
; This is a game mod, not a Windows application. Don't pollute Settings → Apps with
; a fake "JinGu Cheats" entry, don't write to HKLM\...\Uninstall, don't create an
; uninstaller exe. To uninstall, users run uninstall.bat from the game's Mods\
; folder (which Setup copies in as part of the install).
Uninstallable=no
CreateUninstallRegKey=no
UsePreviousAppDir=no

[Languages]
; English only — Inno's bundle doesn't ship a Simplified Chinese .isl by default and the
; wizard text is short / mostly universal (folder picker, file names). README + UI are bilingual.
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
; Bundled binaries — payload dropped into the game folder.
Source: "..\plugin\bin\Release\JinGuCheats.dll"; DestDir: "{app}\Mods"; Flags: ignoreversion
Source: "..\ui\src-tauri\target\release\jingu-cheats-ui.exe"; DestDir: "{app}\Mods"; Flags: ignoreversion
Source: "..\tools\pre-launch.bat"; DestDir: "{app}\Mods"; Flags: ignoreversion
Source: "..\tools\pre-launch.ps1"; DestDir: "{app}\Mods"; Flags: ignoreversion
Source: "..\installer\uninstall.bat"; DestDir: "{app}\Mods"; Flags: ignoreversion
Source: "..\tools\jingu-doctor.ps1"; DestDir: "{app}\Mods"; Flags: ignoreversion
Source: "..\tools\JinGu-Doctor.bat"; DestDir: "{app}\Mods"; Flags: ignoreversion
Source: "..\downloads\MelonLoader.x64.zip"; DestDir: "{tmp}"; Flags: deleteafterinstall
Source: "..\README.md"; DestDir: "{app}\Mods"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}\Mods"; DestName: "JinGuCheats-LICENSE"; Flags: ignoreversion

[Tasks]
Name: "launchoption"; Description: "Add Steam Launch Option for auto-heal (recommended — repairs Mono corlibs after game updates)"; GroupDescription: "Optional:"

[Run]
; Open the README after install so users see the troubleshooting steps once
Filename: "{app}\Mods\README.md"; Description: "Open README"; Flags: postinstall shellexec skipifsilent unchecked nowait

[Code]
// === Helpers ===
function DefaultGameDir(Param: String): String;
var
  SteamPath: String;
begin
  // Look up Steam install path from registry; fall back to the common default.
  if RegQueryStringValue(HKLM, 'SOFTWARE\WOW6432Node\Valve\Steam', 'InstallPath', SteamPath)
     or RegQueryStringValue(HKCU, 'SOFTWARE\Valve\Steam', 'SteamPath', SteamPath) then
  begin
    Result := SteamPath + '\steamapps\common\JinGu\JinGu';
    if FileExists(Result + '\JinGu.exe') then exit;
  end;
  Result := 'C:\Program Files (x86)\Steam\steamapps\common\JinGu\JinGu';
end;

// Validate the picked dir contains JinGu.exe before letting setup proceed.
function NextButtonClick(CurPageID: Integer): Boolean;
var
  GameExe: String;
begin
  Result := True;
  if CurPageID = wpSelectDir then
  begin
    GameExe := WizardForm.DirEdit.Text + '\JinGu.exe';
    if not FileExists(GameExe) then
    begin
      MsgBox('The selected folder does not contain JinGu.exe.' + #13#10 + #13#10 +
             'Please pick your JinGu install folder (the one that contains JinGu.exe).' + #13#10 + #13#10 +
             'Typical location:' + #13#10 +
             'C:\Program Files (x86)\Steam\steamapps\common\JinGu\JinGu',
             mbError, MB_OK);
      Result := False;
    end;
  end;
end;

// Hide the "Select Start Menu Folder" page entirely (DisableProgramGroupPage already does that)
function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := False;
end;

// Run after files copied: extract MelonLoader, patch corlibs, optionally set Steam launch option.
procedure CurStepChanged(CurStep: TSetupStep);
var
  ResultCode: Integer;
  GameDir, MelonZip, ManagedDir, PatchesDir, F: String;
  Files: TArrayOfString;
  i: Integer;
begin
  if CurStep <> ssPostInstall then exit;

  GameDir := ExpandConstant('{app}');
  MelonZip := ExpandConstant('{tmp}\MelonLoader.x64.zip');

  // === 1. Extract MelonLoader if not already present ===
  if not FileExists(GameDir + '\version.dll') then
  begin
    WizardForm.StatusLabel.Caption := 'Extracting MelonLoader...';
    if not Exec('powershell.exe', '-NoProfile -ExecutionPolicy Bypass -Command "Expand-Archive -Path ''' + MelonZip + ''' -DestinationPath ''' + GameDir + ''' -Force"',
                '', SW_HIDE, ewWaitUntilTerminated, ResultCode) or (ResultCode <> 0) then
    begin
      MsgBox('Failed to extract MelonLoader.' + #13#10 +
             'PowerShell exited with code ' + IntToStr(ResultCode) + '.',
             mbError, MB_OK);
      exit;
    end;
    if not FileExists(GameDir + '\version.dll') then
    begin
      MsgBox('MelonLoader extraction completed but version.dll is missing.' + #13#10 +
             'The bundled zip may be corrupted — try downloading the release again.',
             mbError, MB_OK);
      exit;
    end;
  end;

  // === 2. Patch the four stripped corlibs ===
  WizardForm.StatusLabel.Caption := 'Patching Mono runtime...';
  ManagedDir := GameDir + '\JinGu_Data\Managed';
  PatchesDir := GameDir + '\MelonLoader\Dependencies\MonoBleedingEdgePatches';

  if not DirExists(PatchesDir) then
  begin
    MsgBox('MelonLoader''s patch folder is missing:' + #13#10 + PatchesDir + #13#10 +
           'The MelonLoader extraction may have failed.', mbError, MB_OK);
    exit;
  end;

  SetArrayLength(Files, 4);
  Files[0] := 'mscorlib.dll';
  Files[1] := 'System.dll';
  Files[2] := 'System.Core.dll';
  Files[3] := 'System.Runtime.dll';
  for i := 0 to GetArrayLength(Files) - 1 do
  begin
    F := Files[i];
    if FileExists(PatchesDir + '\' + F) then
    begin
      // Clear read-only first; Steam sometimes sets RO on the stripped originals.
      Exec('attrib.exe', '-R "' + ManagedDir + '\' + F + '"', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
      if not FileCopy(PatchesDir + '\' + F, ManagedDir + '\' + F, False) then
      begin
        MsgBox('Failed to copy ' + F + ' into Managed folder.' + #13#10 +
               'Setup may not have permission to write there — try running as administrator.',
               mbError, MB_OK);
        exit;
      end;
    end;
  end;

  // === 3. Optional: set Steam Launch Option ===
  // Skipped here — Steam stores launch options inside localconfig.vdf which is risky to edit
  // programmatically (varies by Steam version, requires Steam closed). Instead we tell the user
  // exactly what to paste in the finish-page message.
end;

// Append a custom message to the finish page describing the launch option.
procedure CurPageChanged(CurPageID: Integer);
var
  LaunchLine: String;
begin
  if CurPageID = wpFinished then
  begin
    if IsTaskSelected('launchoption') then
    begin
      LaunchLine := '"' + ExpandConstant('{app}') + '\Mods\pre-launch.bat" %command%';
      WizardForm.FinishedLabel.Caption :=
        'JinGu Cheats has been installed.' + #13#10 + #13#10 +
        'IMPORTANT — paste this into Steam to enable auto-heal:' + #13#10 + #13#10 +
        LaunchLine + #13#10 + #13#10 +
        'Steam → JinGu → Properties → General → Launch Options' + #13#10 + #13#10 +
        'This makes Steam re-patch the Mono runtime on every launch — needed because some game updates revert the patched files.' + #13#10 + #13#10 +
        'Launch the game through Steam to start playing.' + #13#10 + #13#10 +
        'To uninstall later: right-click ' + ExpandConstant('{app}') + '\Mods\uninstall.bat and Run as administrator.';
    end
    else
    begin
      WizardForm.FinishedLabel.Caption :=
        'JinGu Cheats has been installed.' + #13#10 + #13#10 +
        'Launch the game through Steam to start playing. The cheat UI opens automatically a couple seconds after the game window appears.' + #13#10 + #13#10 +
        'Tip: if a game update ever reverts the Mono patch, run setup again — it''s safe to re-install on top of an existing copy.' + #13#10 + #13#10 +
        'To uninstall later: right-click ' + ExpandConstant('{app}') + '\Mods\uninstall.bat and Run as administrator.';
    end;
  end;
end;
