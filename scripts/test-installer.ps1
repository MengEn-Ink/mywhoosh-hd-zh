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
$wsh=New-Object -ComObject WScript.Shell
$links=Get-ChildItem -LiteralPath $shortcuts -File -Filter '*.lnk'
if($links.Count -ne 2){throw 'Shortcuts missing'}
Add-Type -TypeDefinition @'
using System;
using System.Text;
using System.Runtime.InteropServices;
public static class InstallerPaths {
 [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)] private static extern uint GetLongPathName(string p, StringBuilder b, uint n);
 public static string Long(string p) { var b=new StringBuilder(32768);uint n=GetLongPathName(p,b,(uint)b.Capacity);if(n==0 || n>=b.Capacity)throw new System.IO.IOException("Cannot resolve shortcut path: "+p);return b.ToString(); }
}
'@
foreach($link in $links){
  $s=$wsh.CreateShortcut($link.FullName)
  $target=[InstallerPaths]::Long($s.TargetPath)
  $expected=[InstallerPaths]::Long((Join-Path $app 'MyWhooshZH.exe'))
  if($target -ne $expected){throw ('Shortcut target wrong: actual='+$s.TargetPath+' resolved='+$target+' expected='+$expected)}
  if($link.BaseName.Contains('浮窗') -and $s.Arguments -ne '--floating'){throw ('Floating shortcut arguments wrong: '+$s.Arguments)}
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
if(@(Get-ChildItem -LiteralPath $shortcuts -File -Filter '*.lnk').Count -ne 0){throw 'Shortcuts remain'}
if(-not(Test-Path -LiteralPath (Join-Path $app 'user-keep.txt'))){throw 'Uninstall deleted unknown user file'}
$result=@{pass=$true;version=$Version;install=$true;shortcuts=$true;upgradeMetadataFrom='0.2.9';sameVersionReinstall=$true;uninstall=$true;unknownUserFilePreserved=$true;scope='CI isolated AppId, directory and shortcuts; production wizard not installed on user PC'}
[IO.File]::WriteAllText((Join-Path $root 'installer-tests.json'),($result|ConvertTo-Json -Depth 6),[Text.UTF8Encoding]::new($false))
