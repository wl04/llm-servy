$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
[xml]$project = Get-Content "$root\src\LlmServy\llm-servy.csproj"
$version = [string]$project.Project.PropertyGroup.Version
$zip = "$root\artifacts\release\llm-servy-$version-win-x64.zip"
if (!(Test-Path $zip)) { throw 'Expected versioned release ZIP was not produced.' }
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::OpenRead($zip)
try {
    $names = @($archive.Entries | ForEach-Object { $_.FullName.Replace('\', '/') })
    foreach ($required in @('llm-servy.exe', 'llm-servy.dll', 'llm-servy.runtimeconfig.json', 'ru/llm-servy.resources.dll', 'wsl/wsl-bridge.sh', 'wsl/wsl-supervisor.py', 'LICENSE', 'README.md', 'README.ru.md', 'assets/llm-servy-logo.png', 'docs/development.md')) {
        if ($names -notcontains $required) { throw "Missing archive entry: $required" }
    }
    $runtimeEntry = $archive.Entries | Where-Object { $_.FullName -eq 'llm-servy.runtimeconfig.json' }
    $reader = [IO.StreamReader]::new($runtimeEntry.Open())
    try { $runtime = $reader.ReadToEnd() | ConvertFrom-Json } finally { $reader.Dispose() }
    if (!($runtime.runtimeOptions.frameworks | Where-Object { $_.name -eq 'Microsoft.WindowsDesktop.App' -and $_.version -like '10.*' })) {
        throw 'Expected an external .NET Desktop Runtime 10 dependency.'
    }
    if ($names -contains 'stale-test.txt' -or $names -contains 'coreclr.dll' -or $names -contains 'settings.json') { throw 'Unexpected stale file, bundled runtime or user settings.' }
} finally { $archive.Dispose() }
$expected = (Get-Content "$zip.sha256").Split(' ')[0]
if ((Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant() -ne $expected) { throw 'Checksum mismatch.' }
Write-Host 'PASS release archive contents and SHA-256'
