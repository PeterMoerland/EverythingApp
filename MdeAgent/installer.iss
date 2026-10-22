; Inno Setup-script voor de MDE Werkplek-agent.
; Bouwen via build.ps1 (geeft /DVersie mee) of: ISCC.exe /DVersie=1.0.0 installer.iss

#ifndef Versie
  #define Versie "1.0.0"
#endif

[Setup]
AppId={{7D2B5C1E-4A7F-4B0E-9C8A-3E6F1D2A9B04}
AppName=MDE Werkplek-agent
AppVersion={#Versie}
AppPublisher=MDE Automation
DefaultDirName={autopf}\MDE\Agent
DefaultGroupName=MDE
DisableProgramGroupPage=yes
OutputDir=uitvoer
OutputBaseFilename=MdeAgent-setup-{#Versie}
Compression=lzma2
SolidCompression=yes
PrivilegesRequired=admin
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\MdeAgent.exe
WizardStyle=modern
CloseApplications=yes

[Languages]
Name: "dutch"; MessagesFile: "compiler:Languages\Dutch.isl"

[Files]
Source: "dist\MdeAgent\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "README.md"; DestDir: "{app}"; Flags: ignoreversion
; Npcap-installer meeleveren als hij naast dit script staat (download van npcap.com).
Source: "npcap-*.exe"; DestDir: "{tmp}"; Flags: ignoreversion deleteafterinstall skipifsourcedoesntexist

[Dirs]
Name: "{commonappdata}\MDE\Agent"; Permissions: users-modify
Name: "{commonappdata}\MDE\Agent\log"
Name: "{commonappdata}\MDE\Agent\werk"

[Icons]
Name: "{group}\MDE Werkplek-agent (console)"; Filename: "{app}\MdeAgent.exe"
Name: "{group}\MDE Werkplek-agent - token tonen"; Filename: "{app}\MdeAgent.exe"; Parameters: "--toon-token"
Name: "{group}\MDE Werkplek-agent - instellingen"; Filename: "notepad.exe"; Parameters: """{commonappdata}\MDE\Agent\agent.json"""
Name: "{group}\MDE Werkplek-agent - tray-icoon"; Filename: "{app}\MdeAgentTray.exe"
; tray-icoon voor elke gebruiker bij het aanmelden
Name: "{commonstartup}\MDE Werkplek-agent"; Filename: "{app}\MdeAgentTray.exe"

[Run]
; Npcap alleen als er nog geen zit (WinPcap-compatibel, nodig voor pysoem).
Filename: "{tmp}\{code:NpcapBestand}"; Parameters: "/winpcap_mode=yes"; StatusMsg: "Npcap installeren…"; Check: NpcapNodig; Flags: waituntilterminated skipifdoesntexist
; Bestaande service netjes vervangen.
Filename: "{app}\MdeAgent.exe"; Parameters: "--service stop";    Flags: runhidden waituntilterminated; StatusMsg: "Oude service stoppen…"
Filename: "{app}\MdeAgent.exe"; Parameters: "--service remove";  Flags: runhidden waituntilterminated
Filename: "{app}\MdeAgent.exe"; Parameters: "--service install"; Flags: runhidden waituntilterminated; StatusMsg: "Service registreren…"
; Ingelogde gebruikers mogen deze service starten en stoppen (tray-icoon: Agent herstarten), verder niets
; (besluit gebruiker 22-10-2026). Standaardrechten voor SYSTEM/Administrators/interactief blijven.
Filename: "{sys}\sc.exe"; Parameters: "sdset MdeAgent ""D:(A;;CCLCSWRPWPDTLOCRRC;;;SY)(A;;CCDCLCSWRPWPDTLOCRSDRCWDWO;;;BA)(A;;CCLCSWRPWPDTLOCRRC;;;AU)(A;;CCLCSWLOCRRC;;;IU)(A;;CCLCSWLOCRRC;;;SU)S:(AU;FA;CCDCLCSWRPWPDTLOCRSDRCWDWO;;;WD)"""; Flags: runhidden waituntilterminated; StatusMsg: "Rechten van de service zetten…"
Filename: "{app}\MdeAgent.exe"; Parameters: "--service start";   Flags: runhidden waituntilterminated; StatusMsg: "Service starten…"
; tray-icoon meteen tonen (bij de volgende aanmelding start het vanzelf)
Filename: "{app}\MdeAgentTray.exe"; Flags: nowait runasoriginaluser skipifdoesntexist
Filename: "{app}\MdeAgent.exe"; Parameters: "--toon-token"; Description: "Token tonen (voor in appsettings van de app)"; Flags: postinstall nowait skipifsilent

[UninstallRun]
Filename: "{sys}\taskkill.exe"; Parameters: "/F /IM MdeAgentTray.exe"; Flags: runhidden waituntilterminated; RunOnceId: "tray"
Filename: "{app}\MdeAgent.exe"; Parameters: "--service stop";   Flags: runhidden waituntilterminated; RunOnceId: "stop"
Filename: "{app}\MdeAgent.exe"; Parameters: "--service remove"; Flags: runhidden waituntilterminated; RunOnceId: "remove"

[Code]
var
  TokenPagina: TInputQueryWizardPage;

{ De service moet stil staan vóór de bestanden vervangen worden, anders blijft
  de oude MdeAgent.exe in gebruik en draait na installatie nog de oude versie. }
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  Code: Integer;
begin
  Result := '';
  Exec(ExpandConstant('{sys}\sc.exe'), 'stop MdeAgent', '', SW_HIDE, ewWaitUntilTerminated, Code);
  Sleep(2500);
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM MdeAgent.exe', '', SW_HIDE, ewWaitUntilTerminated, Code);
  { ook de tray-icoontjes (alle sessies), anders blijft MdeAgentTray.exe in gebruik }
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM MdeAgentTray.exe', '', SW_HIDE, ewWaitUntilTerminated, Code);
  Sleep(500);
end;

function NpcapNodig: Boolean;
begin
  Result := not FileExists(ExpandConstant('{sys}\Npcap\wpcap.dll'));
end;

function NpcapBestand(Param: String): String;
var
  Zoek: TFindRec;
begin
  Result := 'npcap-niet-meegeleverd.exe';
  if FindFirst(ExpandConstant('{tmp}\npcap-*.exe'), Zoek) then
  begin
    Result := Zoek.Name;
    FindClose(Zoek);
  end;
end;

procedure InitializeWizard;
begin
  TokenPagina := CreateInputQueryPage(wpSelectDir,
    'Koppeling met de EverythingApp',
    'Token',
    'Deze pc heeft nog geen token. Vul het token in dat in de app staat (appsettings: Productie:AgentToken). ' +
    'Leeg laten kan; instellen kan later met installeer.bat /token.');
  TokenPagina.Add('Token:', False);
end;

{ Staat er al een token in agent.json (bijwerken), dan de tokenpagina overslaan: het bestaande blijft staan.
  Een gebruiker vulde het bij de download uit de app anders opnieuw in (22-10-2026). Wijzigen: installeer.bat /token.
  agent.json schrijft de agent met indent=2, dus "token" staat op een eigen regel. }
function HeeftToken: Boolean;
var
  Regels: TArrayOfString;
  i, p: Integer;
  r: String;
begin
  Result := False;
  if not LoadStringsFromFile(ExpandConstant('{commonappdata}\MDE\Agent\agent.json'), Regels) then Exit;
  for i := 0 to GetArrayLength(Regels) - 1 do
  begin
    p := Pos('"token"', Regels[i]);
    if p > 0 then
    begin
      r := Copy(Regels[i], p + 7, Length(Regels[i]));
      p := Pos('"', r);
      Result := (p > 0) and (Copy(r, p + 1, 1) <> '"') and (Copy(r, p + 1, 1) <> '');
      Exit;
    end;
  end;
end;

function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := (PageID = TokenPagina.ID) and HeeftToken;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  Pad, Inhoud, Token: String;
  Regels: TArrayOfString;
  i: Integer;
  Gevonden: Boolean;
begin
  if CurStep <> ssPostInstall then Exit;
  Token := Trim(TokenPagina.Values[0]);
  if Token = '' then Exit;

  Pad := ExpandConstant('{commonappdata}\MDE\Agent\agent.json');
  if LoadStringsFromFile(Pad, Regels) then
  begin
    Gevonden := False;
    for i := 0 to GetArrayLength(Regels) - 1 do
      if Pos('"token"', Regels[i]) > 0 then
      begin
        Regels[i] := '  "token": "' + Token + '",';
        Gevonden := True;
      end;
    if not Gevonden then
    begin
      SetArrayLength(Regels, GetArrayLength(Regels) + 1);
      for i := GetArrayLength(Regels) - 1 downto 2 do Regels[i] := Regels[i - 1];
      Regels[1] := '  "token": "' + Token + '",';
    end;
    SaveStringsToFile(Pad, Regels, False);
  end
  else
  begin
    Inhoud := '{' + #13#10 + '  "token": "' + Token + '"' + #13#10 + '}' + #13#10;
    SaveStringToFile(Pad, Inhoud, False);
  end;
end;
