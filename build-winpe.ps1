$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath $PSScriptRoot
& $env:ComSpec /d /c .\build.bat
if ($LASTEXITCODE -ne 0) { throw 'Falha ao compilar o executavel.' }

# Pasta nova a cada pacote: nenhum dado de atendimento ou ferramenta local é copiado.
$stage = Join-Path $PSScriptRoot ('artifacts\strelec-package-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $stage -Force | Out-Null
@('Ferramentas', 'OS') | ForEach-Object { New-Item -ItemType Directory -Path (Join-Path $stage $_) | Out-Null }
@('DiagnosticoTecnico.exe', 'Iniciar-WinPE.cmd', 'LEIA-ME-WINPE.txt', 'LEIA-ME.txt') | ForEach-Object {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $_) -Destination $stage
}

$assembly = [Reflection.Assembly]::LoadFile((Join-Path $PSScriptRoot 'DiagnosticoTecnico.exe'))
$config = $assembly.CreateInstance('Diagnostico.AppConfig')
$config.TipoTestes = 'Offline'
$jsonType = $assembly.GetType('Diagnostico.Json')
$serializer = $jsonType.GetField('Serializer').GetValue($null)
$json = $serializer.GetType().GetMethod('Serialize').Invoke($serializer, @($config))
$pretty = $jsonType.GetMethod('Pretty').Invoke($null, @($json))
[IO.File]::WriteAllText((Join-Path $stage 'config.json'), $pretty, (New-Object Text.UTF8Encoding $false))

Add-Type -AssemblyName System.IO.Compression.FileSystem
$destination = Join-Path $PSScriptRoot 'artifacts\DiagnosticoTecnico-Strelec.zip'
$temporaryZip = Join-Path $PSScriptRoot ('artifacts\strelec-' + [Guid]::NewGuid().ToString('N') + '.zip')
[IO.Compression.ZipFile]::CreateFromDirectory($stage, $temporaryZip)
Copy-Item -LiteralPath $temporaryZip -Destination $destination -Force
Remove-Item -LiteralPath $temporaryZip
Write-Output ('Pacote pronto: ' + $destination)
