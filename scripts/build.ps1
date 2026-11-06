param([string]$Version='0.3.0',[string]$Dotnet='dotnet',[string]$InnoCompiler='')
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
if($Version -notmatch '^\d+\.\d+\.\d+(?:-[a-z0-9.-]+)?$'){throw 'Invalid release version'}
$out=Join-Path $root ('artifacts\build-'+$Version)
if(Test-Path -LiteralPath $out){throw 'Build directory exists; use a clean checkout or a new version, not silent overwrite'}
[IO.Directory]::CreateDirectory($out)|Out-Null
& $Dotnet run --project (Join-Path $root 'tests\Whoosh.Tests\Whoosh.Tests.csproj') -c Release -- --result (Join-Path $out 'tests.json')
if($LASTEXITCODE -ne 0){throw 'Regression tests failed'}
foreach($name in @('Whoosh.Cli','Whoosh.Gui')){
  & $Dotnet publish (Join-Path $root ('src\'+$name+'\'+$name+'.csproj')) -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=None -p:DebugSymbols=false -p:Version=$Version -o (Join-Path $out $name)
  if($LASTEXITCODE -ne 0){throw ('Publish failed: '+$name)}
}
$bundle=Join-Path $out 'mywhoosh-hd-zh-win-x64'
[IO.Directory]::CreateDirectory($bundle)|Out-Null
Copy-Item -LiteralPath (Join-Path $out 'Whoosh.Cli\whoosh-zh.exe') -Destination $bundle
Copy-Item -LiteralPath (Join-Path $out 'Whoosh.Gui\MyWhooshZH.exe') -Destination $bundle
foreach($doc in @('README.md','THIRD-PARTY-NOTICES.md','docs\QUICKSTART.md')){Copy-Item -LiteralPath (Join-Path $root $doc) -Destination $bundle}
$docDir=Join-Path $bundle 'docs'; [IO.Directory]::CreateDirectory($docDir)|Out-Null
Get-ChildItem -LiteralPath (Join-Path $root 'docs') -File | ForEach-Object {Copy-Item -LiteralPath $_.FullName -Destination $docDir}
$licenseDir=Join-Path $root 'licenses'; if(Test-Path -LiteralPath $licenseDir -PathType Container){Copy-Item -LiteralPath $licenseDir -Destination $bundle -Recurse}
$sdkRoot=Split-Path -Parent (Get-Command $Dotnet).Source
foreach($license in @('LICENSE.txt','ThirdPartyNotices.txt')){ $p=Join-Path $sdkRoot $license; if(Test-Path -LiteralPath $p -PathType Leaf){Copy-Item -LiteralPath $p -Destination (Join-Path $bundle ('DOTNET-'+$license))}else{throw ('Missing runtime distribution notice: '+$license)} }
$cliVersion=& (Join-Path $bundle 'whoosh-zh.exe') version
if($LASTEXITCODE -ne 0 -or $cliVersion.Trim() -ne $Version){throw 'Published CLI version smoke test failed'}
& (Join-Path $bundle 'whoosh-zh.exe') --help
if($LASTEXITCODE -ne 0){throw 'Published CLI help smoke test failed'}
& (Join-Path $bundle 'whoosh-zh.exe') payload-selftest --directory (Join-Path $out 'published-payload-selftest')
if($LASTEXITCODE -ne 0){throw 'Published embedded payload first-use/repair selftest failed'}
$layoutDir=Join-Path $out 'gui-layout'
$gui=Start-Process -FilePath (Join-Path $bundle 'MyWhooshZH.exe') -ArgumentList @('--layout-selftest',('"'+$layoutDir+'"')) -Wait -PassThru -WindowStyle Hidden
if($gui.ExitCode -ne 0){throw 'Published GUI layout smoke test failed'}
$layoutReport=Join-Path $layoutDir 'ui-selftest.json'
if(-not(Test-Path -LiteralPath $layoutReport -PathType Leaf)){throw 'GUI report missing'}
$layout=Get-Content -LiteralPath $layoutReport -Raw -Encoding UTF8 | ConvertFrom-Json
if(-not $layout.pass -or $layout.payloadTest.version -ne $Version -or $layout.mode -ne 'layout-only' -or @($layout.pages).Count -ne 4 -or @($layout.warnings).Count -ne 0 -or @($layout.smallView.warnings).Count -ne 0 -or @($layout.tinyView.warnings).Count -ne 0){throw 'GUI layout verification failed'}
if(@(Get-ChildItem -LiteralPath $layoutDir -Filter '*.png' -File).Count -ne 25 -or @($layout.stressViews).Count -ne 16 -or -not $layout.payloadTest.pass -or -not $layout.interactions.pass -or $layout.interactions.passed -lt 13){throw 'GUI stress/interaction screenshots incomplete'}
Copy-Item -LiteralPath $layoutReport -Destination (Join-Path $out 'gui-layout.json')
$quickDir=Join-Path $out 'quick-layout'
$quick=Start-Process -FilePath (Join-Path $bundle 'MyWhooshZH.exe') -ArgumentList @('--quick-selftest',('"'+$quickDir+'"')) -Wait -PassThru -WindowStyle Hidden
if($quick.ExitCode -ne 0){throw 'Published floating panel test failed'}
$q=Get-Content -LiteralPath (Join-Path $quickDir 'quick-selftest.json') -Raw -Encoding UTF8 | ConvertFrom-Json
if(-not $q.pass -or $q.passed -lt 8){throw 'Floating panel report failed'}
Copy-Item -LiteralPath (Join-Path $quickDir 'quick-selftest.json') -Destination (Join-Path $out 'quick-layout.json')
$hashes=Get-ChildItem -LiteralPath $bundle -File -Recurse | Sort-Object FullName | ForEach-Object {((Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant())+'  '+$_.FullName.Substring($bundle.Length+1).Replace('\','/')}
[IO.File]::WriteAllLines((Join-Path $bundle 'SHA256SUMS.txt'),$hashes,(New-Object Text.UTF8Encoding $false))
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip=Join-Path $out ('mywhoosh-hd-zh-'+$Version+'-win-x64.zip')
[IO.Compression.ZipFile]::CreateFromDirectory($bundle,$zip)
$zipHash=(Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
if(-not $InnoCompiler){
  $candidate=@('C:\Program Files (x86)\Inno Setup 6\ISCC.exe','C:\Program Files\Inno Setup 6\ISCC.exe') | Where-Object {Test-Path -LiteralPath $_ -PathType Leaf} | Select-Object -First 1
  if($candidate){$InnoCompiler=$candidate}else{throw 'Inno Setup 6 compiler required for installer release'}
}
& $InnoCompiler '/Qp' ('/DAppVersion='+$Version) ('/DBundleDir='+$bundle) ('/DBuildOutput='+$out) (Join-Path $root 'scripts\installer.iss')
if($LASTEXITCODE -ne 0){throw 'Installer compilation failed'}
$setup=Join-Path $out ('mywhoosh-hd-zh-'+$Version+'-win-x64-setup.exe')
if(-not(Test-Path -LiteralPath $setup -PathType Leaf)){throw 'Setup missing'}
if($env:GITHUB_ACTIONS -eq 'true'){
  $testSetup=Join-Path $out 'installer-isolated-test.exe'
  $baseline=Join-Path $out 'installer-baseline-test.exe'
  & $InnoCompiler '/Qp' '/DIsolatedTest=1' '/DAppVersion=0.2.9' ('/DBundleDir='+$bundle) ('/DBuildOutput='+$out) (Join-Path $root 'scripts\installer.iss')
  if($LASTEXITCODE -ne 0){throw 'Baseline installer compilation failed'}
  Move-Item -LiteralPath $testSetup -Destination $baseline
  & $InnoCompiler '/Qp' '/DIsolatedTest=1' ('/DAppVersion='+$Version) ('/DBundleDir='+$bundle) ('/DBuildOutput='+$out) (Join-Path $root 'scripts\installer.iss')
  if($LASTEXITCODE -ne 0){throw 'Isolated installer compilation failed'}
  & (Join-Path $root 'scripts\test-installer.ps1') -Setup $testSetup -BaselineSetup $baseline -Version $Version -Output (Join-Path $out 'installer-integration')
  Copy-Item -LiteralPath (Join-Path $out 'installer-integration\installer-tests.json') -Destination (Join-Path $out 'installer-tests.json')
}
$setupHash=(Get-FileHash -LiteralPath $setup -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText((Join-Path $out 'SHA256SUMS.txt'),$zipHash+'  '+[IO.Path]::GetFileName($zip)+"`n"+$setupHash+'  '+[IO.Path]::GetFileName($setup)+"`n",(New-Object Text.UTF8Encoding $false))
Write-Output ('RELEASE_ZIP='+$zip)
Write-Output ('RELEASE_SETUP='+$setup)
