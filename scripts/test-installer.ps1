param([Parameter(Mandatory)][string]$Setup,[Parameter(Mandatory)][string]$BaselineSetup,[Parameter(Mandatory)][string]$Version,[Parameter(Mandatory)][string]$Output)
$ErrorActionPreference='Stop'
if($env:GITHUB_ACTIONS -ne 'true'){throw 'Installer integration is CI-only; do not modify the real user registry/shortcuts'}
if(Test-Path -LiteralPath $Output){throw 'Installer test output already exists'}
$root=[IO.Path]::GetFullPath($Output)
[IO.Directory]::CreateDirectory($root)|Out-Null
$app=Join-Path $root 'installed-app'
$shortcuts=Join-Path $root 'shortcuts'
$key='HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\MyWhooshHdZh-IsolatedInstallerTest_is1'
if(Test-Path -LiteralPath $key){throw 'Previous isolated test registration exists; keep for investigation'}
function RunSetup([string]$Installer=$Setup) {
    $p=Start-Process -FilePath $Installer -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',('/DIR="'+$app+'"'),('/TESTROOT="'+$root+'"'),('/LOG="'+(Join-Path $root ('install-'+[Guid]::NewGuid().ToString('N')+'.log'))+'"')) -Wait -PassThru
    if($p.ExitCode -ne 0){throw ('Installer failed '+$p.ExitCode)}
}
RunSetup $BaselineSetup
if((Get-ItemProperty -LiteralPath $key).DisplayVersion -ne '0.2.9'){throw 'Baseline version metadata absent'}
[IO.File]::WriteAllText((Join-Path $app 'user-keep.txt'),'owned by test user')
RunSetup
if((Get-Content -LiteralPath (Join-Path $app 'user-keep.txt') -Raw -Encoding UTF8) -ne 'owned by test user'){throw 'Upgrade overwrote unknown user file'}
if(-not(Test-Path -LiteralPath $key)){throw 'Uninstall registration absent'}
$reg=Get-ItemProperty -LiteralPath $key
if($reg.DisplayVersion -ne $Version -or $reg.InstallLocation.TrimEnd('\') -ne $app){throw 'Installed registry version/path wrong'}
Add-Type -TypeDefinition @'
using System;
using System.Text;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
[ComImport, Guid("00021401-0000-0000-C000-000000000046")] class ShellLink {}
[ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] interface IShellLinkW {
 void GetPath([Out,MarshalAs(UnmanagedType.LPWStr)] StringBuilder p,int c,IntPtr data,uint f);
 void GetIDList(out IntPtr p);void SetIDList(IntPtr p);
 void GetDescription([Out,MarshalAs(UnmanagedType.LPWStr)] StringBuilder p,int c);void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string p);
 void GetWorkingDirectory([Out,MarshalAs(UnmanagedType.LPWStr)] StringBuilder p,int c);void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string p);
 void GetArguments([Out,MarshalAs(UnmanagedType.LPWStr)] StringBuilder p,int c);void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string p);
 void GetHotkey(out short p);void SetHotkey(short p);void GetShowCmd(out int p);void SetShowCmd(int p);
 void GetIconLocation([Out,MarshalAs(UnmanagedType.LPWStr)] StringBuilder p,int c,out int i);void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string p,int i);
 void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string p,uint f);void Resolve(IntPtr h,uint f);void SetPath([MarshalAs(UnmanagedType.LPWStr)] string p);
}
public static class InstallerPaths {
 [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)] private static extern uint GetLongPathName(string p, StringBuilder b, uint n);
 public static string Long(string p) { var b=new StringBuilder(32768);uint n=GetLongPathName(p,b,(uint)b.Capacity);if(n==0 || n>=b.Capacity)throw new System.IO.IOException("Cannot resolve shortcut path: "+p);return b.ToString(); }
 public static string[] Link(string p) { object o=new ShellLink();try { ((IPersistFile)o).Load(p,0);var l=(IShellLinkW)o;var t=new StringBuilder(32768);var a=new StringBuilder(32768);var w=new StringBuilder(32768);l.GetPath(t,t.Capacity,IntPtr.Zero,0);l.GetArguments(a,a.Capacity);l.GetWorkingDirectory(w,w.Capacity);return new[]{t.ToString(),a.ToString(),w.ToString()}; }finally{Marshal.ReleaseComObject(o);} }
}
'@
$links=Get-ChildItem -LiteralPath $shortcuts -File -Filter '*.lnk'
if($links.Count -ne 2){throw 'Shortcuts missing'}
foreach($link in $links){
  $s=[InstallerPaths]::Link($link.FullName)
  $target=[InstallerPaths]::Long($s[0])
  $expected=[InstallerPaths]::Long((Join-Path $app 'MyWhooshZH.exe'))
  if($target -ne $expected -or [InstallerPaths]::Long($s[2]) -ne [InstallerPaths]::Long($app)){throw ('Shortcut target/workdir wrong: '+($s -join '|'))}
  if($link.BaseName.Contains('浮窗') -and $s[1] -ne '--floating'){throw ('Floating shortcut arguments wrong: '+$s[1])}
}
$cliVersion=& (Join-Path $app 'whoosh-zh.exe') version
if($LASTEXITCODE -ne 0 -or $cliVersion.Trim() -ne $Version){throw 'Installed CLI version failed'}
$quickOut=Join-Path $root 'installed-quick-test'
$p=Start-Process -FilePath (Join-Path $app 'MyWhooshZH.exe') -ArgumentList @('--quick-selftest',('"'+$quickOut+'"')) -Wait -PassThru
if($p.ExitCode -ne 0){throw 'Installed GUI quick test failed'}
RunSetup
if((Get-Content -LiteralPath (Join-Path $app 'user-keep.txt') -Raw -Encoding UTF8) -ne 'owned by test user'){throw 'Upgrade overwrote unknown user file'}
$uninstall=Join-Path $app 'unins000.exe'
$p=Start-Process -FilePath $uninstall -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART') -Wait -PassThru
if($p.ExitCode -ne 0){throw 'Uninstall failed'}
if(Test-Path -LiteralPath $key){throw 'Uninstall registration remains'}
if(Test-Path -LiteralPath (Join-Path $app 'MyWhooshZH.exe')){throw 'App executable remains'}
if((Test-Path -LiteralPath $shortcuts -PathType Container) -and @(Get-ChildItem -LiteralPath $shortcuts -File -Filter '*.lnk').Count -ne 0){throw 'Shortcuts remain'}
if(-not(Test-Path -LiteralPath (Join-Path $app 'user-keep.txt'))){throw 'Uninstall deleted unknown user file'}
$result=@{pass=$true;version=$Version;install=$true;shortcuts=$true;upgradeMetadataFrom='0.2.9';sameVersionReinstall=$true;uninstall=$true;unknownUserFilePreserved=$true;scope='CI isolated AppId, directory and shortcuts; production wizard not installed on user PC'}
[IO.File]::WriteAllText((Join-Path $root 'installer-tests.json'),($result|ConvertTo-Json -Depth 6),[Text.UTF8Encoding]::new($false))
