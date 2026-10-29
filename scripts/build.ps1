param([string]$Version='0.1.0',[string]$Dotnet='dotnet')
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
$sdkRoot=Split-Path -Parent (Get-Command $Dotnet).Source
foreach($license in @('LICENSE.txt','ThirdPartyNotices.txt')){ $p=Join-Path $sdkRoot $license; if(Test-Path -LiteralPath $p -PathType Leaf){Copy-Item -LiteralPath $p -Destination (Join-Path $bundle ('DOTNET-'+$license))}else{throw ('Missing runtime distribution notice: '+$license)} }
& (Join-Path $bundle 'whoosh-zh.exe') version
if($LASTEXITCODE -ne 0){throw 'Published CLI version smoke test failed'}
& (Join-Path $bundle 'whoosh-zh.exe') --help
if($LASTEXITCODE -ne 0){throw 'Published CLI help smoke test failed'}
$hashes=Get-ChildItem -LiteralPath $bundle -File -Recurse | Sort-Object FullName | ForEach-Object {((Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant())+'  '+$_.FullName.Substring($bundle.Length+1).Replace('\','/')}
[IO.File]::WriteAllLines((Join-Path $bundle 'SHA256SUMS.txt'),$hashes,(New-Object Text.UTF8Encoding $false))
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip=Join-Path $out ('mywhoosh-hd-zh-'+$Version+'-win-x64.zip')
[IO.Compression.ZipFile]::CreateFromDirectory($bundle,$zip)
$zipHash=(Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText((Join-Path $out 'SHA256SUMS.txt'),$zipHash+'  '+[IO.Path]::GetFileName($zip)+"`n",(New-Object Text.UTF8Encoding $false))
Write-Output ('RELEASE_ZIP='+$zip)
