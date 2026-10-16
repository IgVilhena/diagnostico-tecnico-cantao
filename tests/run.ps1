$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath (Split-Path -Parent $PSScriptRoot)
New-Item -ItemType Directory -Path artifacts -Force | Out-Null
$previousTestRoot = $env:DIAG_UI_TEST_DIR
try {
    $env:DIAG_UI_TEST_DIR = Join-Path (Get-Location) 'artifacts'
    $parameters = New-Object System.CodeDom.Compiler.CompilerParameters
    $parameters.GenerateInMemory = $true
    $parameters.CompilerOptions = '/codepage:65001'
    @('System.dll','System.Core.dll','System.Drawing.dll','System.Windows.Forms.dll','System.Management.dll','System.Runtime.Serialization.dll','System.Xml.dll') | ForEach-Object { $parameters.ReferencedAssemblies.Add($_) | Out-Null }
    $sources = @(Get-ChildItem -LiteralPath src -Filter *.cs | ForEach-Object { $_.FullName }) + @(Get-ChildItem -LiteralPath tests -Filter *.cs | ForEach-Object { $_.FullName })
    Add-Type -Path $sources -CompilerParameters $parameters
    $result = [UiSmoke]::Main()
    Get-Content -LiteralPath artifacts/test-results.txt -Encoding UTF8
    if ($result -ne 0) { throw 'Falha na verificacao visual.' }
    $keyboardResult = [KeyboardRegression]::Run()
    Get-Content -LiteralPath artifacts/keyboard-results.txt -Encoding UTF8
    if ($keyboardResult -ne 0) { throw 'Falha na verificacao do teclado.' }
    $winpeResult = [WinPeRegression]::Run()
    Get-Content -LiteralPath artifacts/winpe-results.txt -Encoding UTF8
    if ($winpeResult -ne 0) { throw 'Falha na verificacao de compatibilidade WinPE.' }
}
finally { $env:DIAG_UI_TEST_DIR = $previousTestRoot }
