param(
    [Parameter(Mandatory)][string]$Distribution,
    [string]$Version='2.0.0',
    [string]$Repository='FlylCaruz/everbound',
    [string]$GitHubCli='gh'
)
$ErrorActionPreference='Stop'
$root=(Resolve-Path -LiteralPath $Distribution).Path
$expected=@('EverBoundLauncher.exe',"EverBound-Client-$Version.zip",'manifest.json','manifest.sig','LEIA-ME.md')
foreach($name in $expected) { if(!(Test-Path -LiteralPath (Join-Path $root $name))){throw "Falta o artefato $name"} }
$manifest=Get-Content -LiteralPath (Join-Path $root 'manifest.json') -Raw | ConvertFrom-Json
if($manifest.version -ne $Version){throw 'Versão do catálogo diferente da release.'}
if($manifest.packageSha256 -ne (Get-FileHash -LiteralPath (Join-Path $root "EverBound-Client-$Version.zip")).Hash.ToLowerInvariant()){throw 'Hash do pacote diferente do catálogo.'}
foreach($file in $manifest.files) { if($file.path -match '(?i)progression-data|client\.json|server\.json|\.sl2|\.co2|\.pfx|\.pem|\.key$|private-key'){throw 'Arquivo privado encontrado no catálogo.'} }
$components=Join-Path $root 'components'
New-Item -ItemType Directory -Force -Path $components | Out-Null
Add-Type -AssemblyName System.IO.Compression.FileSystem
$componentAssets=@()
$archive=[IO.Compression.ZipFile]::OpenRead((Join-Path $root "EverBound-Client-$Version.zip"))
try {
    foreach($file in ($manifest.files | Where-Object {$_.path.EndsWith('.dll') -and !$_.path.StartsWith('engine/')})) {
        $entry=$archive.GetEntry($file.path)
        if(!$entry){throw "Componente ausente: $($file.path)"}
        $destination=Join-Path $components ([IO.Path]::GetFileName($file.path))
        [IO.Compression.ZipFileExtensions]::ExtractToFile($entry,$destination,$true)
        if((Get-FileHash -LiteralPath $destination).Hash.ToLowerInvariant() -ne $file.sha256){throw 'Componente diferente do catálogo.'}
        $componentAssets+=$destination
    }
}finally{$archive.Dispose()}
$tag="v$Version"
$notes=Join-Path $root 'release-notes.md'
$body="EverBound $Version`n`nBaixe EverBoundLauncher.exe para instalar, atualizar e abrir o perfil.`n`n"+($manifest.notes | ForEach-Object {"- $_"} | Out-String)+"`nCompatível com Elden Ring 1.17 / 2.7.0.0. Progresso e convites do chat ficam no computador de cada jogador."
[IO.File]::WriteAllText($notes,$body,[Text.UTF8Encoding]::new($false))
& $GitHubCli release create $tag --repo $Repository --draft --title "EverBound $Version — Launcher e cliente" --notes-file $notes
if($LASTEXITCODE){throw 'Não foi possível criar o rascunho. Uma release existente não será sobrescrita.'}
$assets=@($expected | ForEach-Object {Join-Path $root $_})+$componentAssets
& $GitHubCli release upload $tag --repo $Repository @assets
if($LASTEXITCODE){throw 'Upload incompleto. A release permanece como rascunho.'}
& $GitHubCli release edit $tag --repo $Repository --draft=false --latest
if($LASTEXITCODE){throw 'Os arquivos foram enviados, mas a publicação não foi concluída.'}
& $GitHubCli release view $tag --repo $Repository --json url,assets
if($LASTEXITCODE){throw 'Não foi possível confirmar a publicação.'}
