param(
    [string]$Version = '2.0.0',
    [Parameter(Mandatory)][string]$Profile,
    [Parameter(Mandatory)][string]$Game,
    [string]$Repository = 'FlylCaruz/everbound',
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '../../outputs/EverBound-Launcher')
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Security
$build = (New-Item -ItemType Directory -Force -Path "$PSScriptRoot/build").FullName
$output = (New-Item -ItemType Directory -Force -Path $OutputDirectory).FullName
$payload = (New-Item -ItemType Directory -Force -Path "$build/payload-$Version").FullName
$encoding = [Text.UTF8Encoding]::new($false)
$files = [Collections.Generic.List[object]]::new()
function Add-File([string]$source,[string]$relative,[bool]$preserve=$false) {
    if ($relative -match '(?i)progression-data|server\.json|client\.json|\.sl2|\.co2|\.pfx|\.pem|\.key$|private-key') { throw "Arquivo privado na distribuição: $relative" }
    $destination = Join-Path $payload $relative
    New-Item -ItemType Directory -Force -Path (Split-Path $destination) | Out-Null
    Copy-Item -LiteralPath $source -Destination $destination -Force
    $files.Add([ordered]@{path=$relative;sha256=(Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash.ToLowerInvariant();size=(Get-Item -LiteralPath $destination).Length;preserve=$preserve})
}
foreach ($relative in @(
    'EverBoundStartup.dll','EverBoundTalisman5.dll','EverBoundEquipmentSlots.dll','EverBoundCharacterPreview.dll','EverBoundUnifiedInventory.dll','EverBoundProgression-effects.dll',
    'EROverlay/EROverlay.dll','EROverlay/overlays/EverBoundChat.dll','EROverlay/overlays/EverBoundLevelUp.dll','EROverlay/overlays/Minimap.dll',
    'EROverlay/LICENSE','EROverlay/LICENSE_bcdec.txt','chat/LEIA-ME.md',
    'mod-inventario/menu/01_000_fe.gfx','mod-inventario/menu/02_010_equiptop.gfx','mod-inventario/menu/02_011_equip.gfx','mod-inventario/menu/02_020_inventory.gfx'
)) { Add-File (Join-Path $Profile $relative) $relative }
foreach ($relative in @('EROverlay/configs/common.ini','EROverlay/configs/input.ini','EROverlay/configs/levelup.ini','EROverlay/configs/minimap.ini','EROverlay/configs/style.ini')) {
    Add-File (Join-Path $Profile $relative) $relative $true
}
$engineSource=if(Test-Path -LiteralPath (Join-Path $Profile 'engine/modengine2_launcher.exe')){Join-Path $Profile 'engine'}else{$Game}
foreach ($relative in @('modengine2_launcher.exe','modengine2/bin/modengine2.dll','modengine2/bin/lua.dll','modengine2/crashpad/crashpad_handler.exe','modengine2/crashpad/zlib1.dll')) {
    Add-File (Join-Path $engineSource $relative) ('engine/'+$relative)
}
$licensePath = Join-Path $build 'ModEngine2-LICENSE.txt'
if (!(Test-Path -LiteralPath $licensePath)) { Invoke-WebRequest 'https://raw.githubusercontent.com/soulsmods/ModEngine2/main/LICENSE-MIT' -OutFile $licensePath }
Add-File $licensePath 'licenses/ModEngine2-LICENSE.txt'
$keyRoot = Join-Path $env:USERPROFILE '.codex/EverBound-Publisher'
New-Item -ItemType Directory -Force -Path $keyRoot | Out-Null
$keyPath = Join-Path $keyRoot 'release-signing.dpapi'
$rsa = [Security.Cryptography.RSACryptoServiceProvider]::new(3072)
$rsa.PersistKeyInCsp = $false
try {
    if (Test-Path -LiteralPath $keyPath) {
        $secret = [Security.Cryptography.ProtectedData]::Unprotect([IO.File]::ReadAllBytes($keyPath),$null,[Security.Cryptography.DataProtectionScope]::CurrentUser)
        $rsa.FromXmlString($encoding.GetString($secret))
        [Array]::Clear($secret,0,$secret.Length)
    } else {
        $secret = $encoding.GetBytes($rsa.ToXmlString($true))
        [IO.File]::WriteAllBytes($keyPath,[Security.Cryptography.ProtectedData]::Protect($secret,$null,[Security.Cryptography.DataProtectionScope]::CurrentUser))
        [Array]::Clear($secret,0,$secret.Length)
    }
    $zipName = "EverBound-Client-$Version.zip"
    $zipPath = Join-Path $output $zipName
    $temporaryZip = Join-Path $build ('package-'+[guid]::NewGuid().ToString('N')+'.zip')
    $zip = [IO.Compression.ZipFile]::Open($temporaryZip,[IO.Compression.ZipArchiveMode]::Create)
    try { foreach ($item in $files) { [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip,(Join-Path $payload $item.path),$item.path,[IO.Compression.CompressionLevel]::Optimal) } }
    finally { $zip.Dispose() }
    Copy-Item -LiteralPath $temporaryZip -Destination $zipPath -Force
    Remove-Item -LiteralPath $temporaryZip
    $manifest = [ordered]@{
        schema=1;version=$Version;gameVersion='2.7.0.0';published=(Get-Date).ToUniversalTime().ToString('o')
        package="https://github.com/$Repository/releases/download/v$Version/$zipName"
        packageSha256=(Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
        packageSize=(Get-Item -LiteralPath $zipPath).Length
        notes=@('Instalação, atualização e abertura em um único launcher.','HUD e inventário EverBound com três colunas.','Progressão com EXP, ouro e pontos de atributo.','Minimapa circular com moldura dourada.','Chat entre jogadores com comandos e permissões.','Inicialização direta, sem confirmações repetidas.')
        files=@($files)
    }
    $jsonPath = Join-Path $output 'manifest.json'
    [IO.File]::WriteAllText($jsonPath,($manifest|ConvertTo-Json -Depth 8),$encoding)
    $signature = $rsa.SignData([IO.File]::ReadAllBytes($jsonPath),[Security.Cryptography.CryptoConfig]::MapNameToOID('SHA256'))
    $sigPath = Join-Path $output 'manifest.sig'
    [IO.File]::WriteAllText($sigPath,[Convert]::ToBase64String($signature),$encoding)
    [IO.File]::WriteAllText((Join-Path $build 'release-public.xml'),$rsa.ToXmlString($false),$encoding)
} finally { $rsa.Dispose() }
# A small vector emblem is drawn directly for the Windows icon; background art is kept intact.
$iconPath=Join-Path $PSScriptRoot 'assets/launcher.ico'
if (!(Test-Path -LiteralPath $iconPath)) {
    $bitmap=[Drawing.Bitmap]::new(128,128)
    $g=[Drawing.Graphics]::FromImage($bitmap);$g.SmoothingMode='AntiAlias'
    $g.Clear([Drawing.Color]::FromArgb(16,23,20))
    $pen=[Drawing.Pen]::new([Drawing.Color]::FromArgb(194,163,95),4)
    $g.DrawEllipse($pen,15,15,98,98);$g.DrawEllipse($pen,23,23,82,82)
    $g.DrawLine($pen,64,30,64,97);$g.DrawLines($pen,[Drawing.Point[]]@([Drawing.Point]::new(32,46),[Drawing.Point]::new(64,71),[Drawing.Point]::new(96,46)))
    $g.DrawLines($pen,[Drawing.Point[]]@([Drawing.Point]::new(45,31),[Drawing.Point]::new(64,49),[Drawing.Point]::new(83,31)))
    $icon=[Drawing.Icon]::FromHandle($bitmap.GetHicon());$stream=[IO.File]::Create($iconPath);$icon.Save($stream);$stream.Dispose();$icon.Dispose();$pen.Dispose();$g.Dispose();$bitmap.Dispose()
}
$compiler=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
$exePath=Join-Path $output 'EverBoundLauncher.exe'
$compilePath=Join-Path $build 'EverBoundLauncher.exe'
$compilerArgs=@('/nologo','/target:winexe','/platform:x64','/optimize+','/codepage:65001',
    ('/out:'+$compilePath),('/win32manifest:'+(Join-Path $PSScriptRoot 'app.manifest')),('/win32icon:'+$iconPath),
    '/r:System.Windows.Forms.dll','/r:System.Drawing.dll','/r:System.Web.Extensions.dll','/r:System.IO.Compression.dll','/r:System.IO.Compression.FileSystem.dll',
    ('/resource:'+$jsonPath+',release.json'),('/resource:'+$sigPath+',release.sig'),('/resource:'+$zipPath+',release.zip'),
    ('/resource:'+(Join-Path $build 'release-public.xml')+',release-public.xml'),
    ('/resource:'+(Join-Path $PSScriptRoot 'assets/everbound-world.png')+',world.png'),('/resource:'+$iconPath+',launcher.ico'),
    (Join-Path $PSScriptRoot 'Core.cs'),(Join-Path $PSScriptRoot 'Launcher.cs'),(Join-Path $PSScriptRoot 'AssemblyInfo.cs'))
$compilerArgs=$compilerArgs | ForEach-Object { if($_ -match '^[A-Za-z]:'){ $_.Replace('/','\') }elseif($_ -match '^/(out|win32manifest|win32icon|resource):'){ $at=$_.IndexOf(':')+1;$_.Substring(0,$at)+$_.Substring($at).Replace('/','\') }else{$_} }
& $compiler @compilerArgs
if ($LASTEXITCODE) { throw 'Falha ao compilar o launcher.' }
Copy-Item -LiteralPath $compilePath -Destination $exePath -Force
Write-Output "Launcher: $exePath"
Write-Output "Pacote: $zipPath"
Write-Output "Arquivos distribuídos: $($files.Count)"
