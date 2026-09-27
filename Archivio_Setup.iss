; -----------------------------------------------------------------------------
; Inno Setup Script for "Archivio" Video Metadata Manager (Framework-Dependent .NET 10.0)
; -----------------------------------------------------------------------------

#define AppName "Archivio"
#define AppVersion "1.2.0"
#define AppPublisher "tuoza"
#define AppExeName "Archivio.exe"
#define ReleasePublishDir "Archivio\bin\Release\net10.0-windows10.0.19041.0\win-x64\publish"

[Setup]
; アプリ固有の内部ID（GUID）。必要に応じてユニークなIDを設定可能。
AppId={{9F7B5F8C-2E3D-4A2D-BE14-C9A8E3F2E71B}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
; 保存されるインストーラーのファイル名と出力場所
OutputDir=Installer_Output
OutputBaseFilename=Archivio_Setup
; 生成されたマルチサイズ解像度アイコンをセットアップのアイコンにも適用！
SetupIconFile=Archivio\Assets\Archivio.ico
Compression=lzma2/ultra
SolidCompression=yes
; 64ビットOS（Windows 10 / 11 64bit）専用であることを明示
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
WizardStyle=modern

[Languages]
Name: "japanese"; MessagesFile: "compiler:Languages\Japanese.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[InstallDelete]
; 古い自己完結型（Self-Contained）ビルドの残骸ファイルが混在すると、
; .NETホストが自己完結型と誤認して起動エラー（Required: 'Microsoft.NETCore.App', version '10.0.0' (x64)）を引き起こすため、
; 競合する古いランタイム関連ファイルをインストールの開始時にクリーンアップします。
Type: files; Name: "{app}\coreclr.dll"
Type: files; Name: "{app}\hostfxr.dll"
Type: files; Name: "{app}\hostpolicy.dll"
Type: files; Name: "{app}\System.Private.CoreLib.dll"
Type: files; Name: "{app}\clrjit.dll"
Type: files; Name: "{app}\clretwrc.dll"
Type: files; Name: "{app}\clrgc.dll"
Type: files; Name: "{app}\clrgcexp.dll"
Type: files; Name: "{app}\createdump.exe"
Type: files; Name: "{app}\mscordaccore.dll"
Type: files; Name: "{app}\mscordbi.dll"
Type: files; Name: "{app}\mscorrc.dll"

[Files]
; メインのEXEファイル
Source: "{#ReleasePublishDir}\{#AppExeName}"; DestDir: "{app}"; Flags: ignoreversion
; 利用案内をインストール先に同梱（アプリの右クリックメニューから表示）
Source: "README.md"; DestDir: "{app}"; Flags: ignoreversion
; その他の依存DLLやアセット群（メインEXEは除外して二重コピーを防止）
Source: "{#ReleasePublishDir}\*"; DestDir: "{app}"; Excludes: "{#AppExeName}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(AppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[Code]
// .NET 10.0 Desktop Runtime が物理的にディスク上に存在するか確認する関数
// レジストリ未登録やZIP解凍配置の環境でも確実に検出可能な極めて頑強なディスクスキャンロジック
function IsDotNet10InstalledOnDisk(): Boolean;
var
  FindRec: TFindRec;
  SearchPath: string;
begin
  Result := False;
  // C:\Program Files\dotnet\shared\Microsoft.WindowsDesktop.App\10.* を探索
  SearchPath := ExpandConstant('{pf}\dotnet\shared\Microsoft.WindowsDesktop.App\10.*');
  
  if FindFirst(SearchPath, FindRec) then
  begin
    try
      repeat
        // ディレクトリ属性を持っており、かつ . や .. でない有効なフォルダを見つけた場合
        if (FindRec.Attributes and FILE_ATTRIBUTE_DIRECTORY) <> 0 then
        begin
          if (FindRec.Name <> '.') and (FindRec.Name <> '..') then
          begin
            Result := True;
            Break;
          end;
        end;
      until not FindNext(FindRec);
    finally
      FindClose(FindRec);
    end;
  end;
end;

// .NET 10.0 Desktop Runtime、Core Runtime、または .NET 10.0 SDKのいずれかが
// インストールされているかを多重にチェックする超強力な検出関数
function IsDotNet10Installed(): Boolean;
var
  Versions: TArrayOfString;
  I: Integer;
begin
  Result := False;

  // 1. 一般ユーザー環境用の「Windows Desktop Runtime (x64)」のレジストリチェック
  if RegGetSubkeyNames(HKLM64, 'SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App', Versions) then
  begin
    for I := 0 to GetArrayLength(Versions) - 1 do
    begin
      if Pos('10.', Versions[I]) = 1 then
      begin
        Result := True;
        Exit;
      end;
    end;
  end;

  // 2. 基本ランタイム「.NET Core Runtime (x64)」のレジストリチェック
  if RegGetSubkeyNames(HKLM64, 'SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.NETCore.App', Versions) then
  begin
    for I := 0 to GetArrayLength(Versions) - 1 do
    begin
      if Pos('10.', Versions[I]) = 1 then
      begin
        Result := True;
        Exit;
      end;
    end;
  end;

  // 3. 開発者環境用の「.NET SDK (x64)」のレジストリチェック
  if RegGetSubkeyNames(HKLM64, 'SOFTWARE\dotnet\Setup\InstalledVersions\x64\sdk', Versions) then
  begin
    for I := 0 to GetArrayLength(Versions) - 1 do
    begin
      if Pos('10.', Versions[I]) = 1 then
      begin
        Result := True;
        Exit;
      end;
    end;
  end;

  // 4. 【最強のフォールバック】レジストリ未登録でも、ディスク上の実体フォルダを直接スキャン！
  if IsDotNet10InstalledOnDisk() then
  begin
    Result := True;
    Exit;
  end;
end;

// インストーラー起動時の初期化チェック処理
function InitializeSetup(): Boolean;
var
  ErrorCode: Integer;
begin
  Result := True;
  
  // .NET 10.0 関連環境が一つも見つからない場合のみ警告する
  if not IsDotNet10Installed() then
  begin
    if MsgBox('Archivio の実行には「.NET 10.0 Desktop Runtime (x64)」が必要です。' + #13#10 +
              'しかし、このPCにはインストールされていないようです。' + #13#10#13#10 +
              'マイクロソフトの公式ダウンロードページを開き、インストールを行いますか？', 
              mbConfirmation, MB_YESNO) = idYes then
    begin
      // ダウンロードページをブラウザで開く
      ShellExec('open', 'https://dotnet.microsoft.com/download/dotnet/10.0', '', '', SW_SHOWNORMAL, ewNoWait, ErrorCode);
    end;
    
    // インストール処理を中断する
    Result := False;
  end;
end;
