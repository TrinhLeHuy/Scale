; Bộ cài TTC Weighing (Inno Setup 6). Không chạy trực tiếp: dùng build-installer.ps1
; để publish app, giải nén PostgreSQL và tải VC++ runtime vào installer\out trước khi biên dịch.
;
; Người cài chỉ chọn thư mục cài đặt. Mọi thứ khác (cơ sở dữ liệu, cấu hình) tự làm.
; Dữ liệu nằm trong <thư mục cài>\data, gỡ cài đặt không xóa thư mục này.

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#define AppExe "TTC.Weighing.WinForms.exe"
#define DbService "TTCWeighingDB"

[Setup]
AppId={{6F1B7C2E-4D3A-4B8E-9C51-2A7E0D3F9B64}
AppName=TTC - Hệ thống cân hợp nhất
AppVersion={#AppVersion}
AppVerName=TTC - Hệ thống cân hợp nhất {#AppVersion}
AppPublisher=TTC AgriS
VersionInfoVersion={#AppVersion}
DefaultDirName={sd}\TTC Weighing
DefaultGroupName=TTC Weighing
DisableProgramGroupPage=yes
DisableDirPage=no
DisableReadyPage=yes
UsePreviousAppDir=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
OutputDir=out
OutputBaseFilename=TTC-Weighing-Setup-{#AppVersion}
Compression=lzma2/max
SolidCompression=yes
LZMAUseSeparateProcess=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
UninstallDisplayIcon={app}\{#AppExe}
SetupIconFile=..\src\TTC.Weighing.WinForms\Assets\logo.ico
LanguageDetectionMethod=none
ShowLanguageDialog=no

[Languages]
Name: "vi"; MessagesFile: "Vietnamese.isl"

[CustomMessages]
AppTitle=TTC - Hệ thống cân hợp nhất
StatusVc=Đang cài Microsoft Visual C++ Runtime...
StatusConfig=Đang tạo cơ sở dữ liệu và cấu hình trạm cân...
ErrConfig=Cấu hình chưa hoàn tất (mã %1). Ứng dụng đã được chép nhưng có thể chưa chạy được.%n%nNhật ký lỗi ở thư mục:%n%2%n%nGửi tệp nhật ký mới nhất cho bộ phận IT rồi chạy lại bộ cài.
TaskDesktop=Tạo biểu tượng trên màn hình Desktop
LaunchApp=Mở ứng dụng cân

[Tasks]
Name: "desktopicon"; Description: "{cm:TaskDesktop}"

[Files]
Source: "out\app\*"; DestDir: "{app}"; Excludes: "appsettings.Development.json,appsettings.Local.json,*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "out\pgsql\*"; DestDir: "{app}\pgsql"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "scripts\configure.ps1"; DestDir: "{app}\setup"; Flags: ignoreversion
Source: "scripts\uninstall.ps1"; DestDir: "{app}\setup"; Flags: ignoreversion
Source: "..\db\schema.sql"; DestDir: "{app}\setup\db"; Flags: ignoreversion
Source: "..\db\seed.sql"; DestDir: "{app}\setup\db"; Flags: ignoreversion
Source: "..\db\upgrades\*.sql"; DestDir: "{app}\setup\db\upgrades"; Flags: ignoreversion skipifsourcedoesntexist
Source: "out\vc_redist.x64.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall
Source: "out\bundle-settings.json"; DestDir: "{tmp}"; Flags: deleteafterinstall skipifsourcedoesntexist

[Dirs]
; Ứng dụng ghi bằng chứng / outbox vào thư mục cài đặt khi chạy bằng tài khoản thường.
Name: "{app}\evidence"; Permissions: users-modify
Name: "{app}\outbox"; Permissions: users-modify

[Icons]
Name: "{autoprograms}\{cm:AppTitle}"; Filename: "{app}\{#AppExe}"; WorkingDir: "{app}"; IconFilename: "{app}\Assets\logo.ico"
Name: "{autodesktop}\{cm:AppTitle}"; Filename: "{app}\{#AppExe}"; WorkingDir: "{app}"; IconFilename: "{app}\Assets\logo.ico"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchApp}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\setup\uninstall.ps1"""; Flags: runhidden waituntilterminated; RunOnceId: "RemoveDbService"

[Code]
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
begin
  // Dừng dịch vụ CSDL khi nâng cấp để chép đè được tệp trong thư mục pgsql.
  Exec('powershell.exe',
    '-NoProfile -Command "$s = Get-Service -Name {#DbService} -ErrorAction SilentlyContinue; if ($s -and $s.Status -ne ''Stopped'') { Stop-Service -Name {#DbService} -Force; $s.WaitForStatus(''Stopped'', [TimeSpan]::FromSeconds(60)) }"',
    '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Result := '';
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  ResultCode: Integer;
begin
  if CurStep <> ssPostInstall then
    Exit;

  WizardForm.StatusLabel.Caption := CustomMessage('StatusVc');
  // /install /quiet: bỏ qua nếu máy đã có bản mới hơn, không khởi động lại.
  Exec(ExpandConstant('{tmp}\vc_redist.x64.exe'), '/install /quiet /norestart', '',
    SW_HIDE, ewWaitUntilTerminated, ResultCode);

  WizardForm.StatusLabel.Caption := CustomMessage('StatusConfig');
  if not Exec('powershell.exe',
    '-NoProfile -ExecutionPolicy Bypass -File "' + ExpandConstant('{app}\setup\configure.ps1') +
    '" -AppDir "' + ExpandConstant('{app}') + '" -BundleSettings "' + ExpandConstant('{tmp}\bundle-settings.json') + '"',
    '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
    ResultCode := -1;

  if ResultCode <> 0 then
    SuppressibleMsgBox(FmtMessage(CustomMessage('ErrConfig'), [IntToStr(ResultCode),
      ExpandConstant('{app}\data\logs')]), mbError, MB_OK, IDOK);
end;
