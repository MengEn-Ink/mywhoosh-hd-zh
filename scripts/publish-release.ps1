param([Parameter(Mandatory)][string]$Version,[bool]$PreRelease=$false)
$ErrorActionPreference='Stop'
if($env:GITHUB_ACTIONS -ne 'true'){throw 'Release evidence publisher is CI-only'}
$tag='v'+$Version
$bundle='artifacts/build-'+$Version
$zipName='mywhoosh-hd-zh-'+$Version+'-win-x64.zip'
$setupName='mywhoosh-hd-zh-'+$Version+'-win-x64-setup.exe'
$required=@($zipName,$setupName,'SHA256SUMS.txt','tests.json','gui-layout.json','installer-tests.json','quick-layout.json')
$files=$required|ForEach-Object{Join-Path $bundle $_}
$releaseArgs=@('release','create',$tag,'--target',$env:GITHUB_SHA,'--title',('MyWhoosh HD ZH '+$tag),'--notes-file','docs/RELEASE-NOTES.md')
if($PreRelease){$releaseArgs+='--prerelease'}
$releaseArgs+=$files
& gh @releaseArgs
if($LASTEXITCODE -ne 0){throw 'Release failed; never overwrite existing versions'}
$json=& gh release view $tag --json assets,url,tagName
if($LASTEXITCODE -ne 0){throw 'Cannot verify uploaded assets'}
$info=$json|ConvertFrom-Json
foreach($name in $required){if(@($info.assets|Where-Object{$_.name -eq $name -and $_.state -eq 'uploaded'}).Count -ne 1){throw ('Missing asset '+$name)}}
$verify=Join-Path $bundle 'remote-verification'
foreach($name in @($zipName,$setupName)){
  & gh release download $tag --pattern $name --dir $verify
  if($LASTEXITCODE -ne 0){throw ('Cannot redownload '+$name)}
  if((Get-FileHash -LiteralPath (Join-Path $bundle $name) -Algorithm SHA256).Hash -ne (Get-FileHash -LiteralPath (Join-Path $verify $name) -Algorithm SHA256).Hash){throw ('Redownload hash mismatch '+$name)}
}
$unpacked=Join-Path $verify 'unpacked'
Expand-Archive -LiteralPath (Join-Path $verify $zipName) -DestinationPath $unpacked
$cliVersion=& (Join-Path $unpacked 'whoosh-zh.exe') version
if($LASTEXITCODE -ne 0 -or $cliVersion.Trim() -ne $Version){throw 'Redownloaded CLI version failed'}
$payloadJson=& (Join-Path $unpacked 'whoosh-zh.exe') payload-selftest --directory (Join-Path $verify 'isolated-payload')
if($LASTEXITCODE -ne 0){throw 'Redownloaded payload test failed'}
$payload=$payloadJson|ConvertFrom-Json
$reports=@{}
foreach($kind in @('layout','quick')){
  $dir=Join-Path $verify ($kind+'-test')
  $p=Start-Process -FilePath (Join-Path $unpacked 'MyWhooshZH.exe') -ArgumentList @(('--'+$kind+'-selftest'),('"'+$dir+'"')) -Wait -PassThru -WindowStyle Hidden
  if($p.ExitCode -ne 0){throw ('Redownloaded GUI '+$kind+' test failed')}
  $name=if($kind -eq 'layout'){'ui-selftest.json'}else{'quick-selftest.json'}
  $reports[$kind]=Get-Content -LiteralPath (Join-Path $dir $name) -Raw -Encoding UTF8|ConvertFrom-Json
}
$layout=$reports['layout'];$quick=$reports['quick']
$installer=Get-Content -LiteralPath (Join-Path $bundle 'installer-tests.json') -Raw -Encoding UTF8|ConvertFrom-Json
if(-not $payload.pass -or -not $layout.pass -or -not $layout.payloadTest.pass -or $layout.payloadTest.version -ne $Version -or $layout.interactions.passed -lt 13 -or @($layout.stressViews).Count -ne 16 -or -not $quick.pass -or $quick.passed -lt 8 -or -not $installer.pass){throw 'Release report gate failed'}
$verification=@{zipSha256=(Get-FileHash -LiteralPath (Join-Path $verify $zipName) -Algorithm SHA256).Hash.ToLowerInvariant();setupSha256=(Get-FileHash -LiteralPath (Join-Path $verify $setupName) -Algorithm SHA256).Hash.ToLowerInvariant();redownloadHashMatches=$true;setupRedownloadHashMatches=$true;cliVersion=$cliVersion.Trim();payloadSelftestPass=$payload.pass;guiPayloadSelftestPass=$layout.payloadTest.pass;guiLayoutPass=$layout.pass;guiInteractions=$layout.interactions.passed;stressViews=@($layout.stressViews).Count;floatingPass=$quick.pass;floatingTests=$quick.passed;installerIntegrationPass=$installer.pass}
$result=@{tag=$tag;releaseUrl=$info.url;runUrl=('https://github.com/'+$env:GITHUB_REPOSITORY+'/actions/runs/'+$env:GITHUB_RUN_ID);commit=$env:GITHUB_SHA;assets=$info.assets;verification=$verification}|ConvertTo-Json -Depth 12
[IO.File]::WriteAllText('release-evidence.json',$result,[Text.UTF8Encoding]::new($false))
& gh release upload $tag 'release-evidence.json'
if($LASTEXITCODE -ne 0){throw 'Cannot upload evidence'}
$path='repos/'+$env:GITHUB_REPOSITORY+'/contents/.github/last-release.json'
$previousSha=& gh api $path --jq '.sha' 2>$null
$put=@('api','--method','PUT',$path,'-f',('message=ci: record verified assets for '+$tag),'-f',('content='+[Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($result))),'-f','branch=main')
if($LASTEXITCODE -eq 0 -and $previousSha){$put+=@('-f',('sha='+$previousSha))}
& gh @put
if($LASTEXITCODE -ne 0){throw 'Cannot record repository evidence'}
