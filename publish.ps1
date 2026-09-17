param([string]$ExpectedTag)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Push-Location $PSScriptRoot
$staging = $null
try {
    $project = 'src/LlmServy/llm-servy.csproj'
    $version = (& dotnet msbuild $project -nologo -getProperty:Version).Trim()
    if ($LASTEXITCODE -ne 0) { throw 'Cannot read the project version.' }
    if ($version -notmatch '^\d+\.\d+\.\d+(?:-[0-9A-Za-z]+(?:[.-][0-9A-Za-z]+)*)?$') {
        throw "Unsupported release version: $version"
    }
    if ($ExpectedTag -and $ExpectedTag -cne "v$version") {
        throw "Tag $ExpectedTag does not match project version v$version."
    }

    $artifacts = Join-Path $PSScriptRoot 'artifacts'
    $staging = Join-Path $artifacts ('package-' + [Guid]::NewGuid().ToString('N'))
    $payload = Join-Path $staging 'payload'
    New-Item -ItemType Directory -Path $payload -Force | Out-Null
    dotnet publish $project -c Release -r win-x64 --self-contained false -o $payload
    if ($LASTEXITCODE -ne 0) { throw "Publish failed ($LASTEXITCODE)." }

    Copy-Item README.md, README.ru.md -Destination $payload
    New-Item -ItemType Directory -Path "$payload/assets" | Out-Null
    Copy-Item assets/llm-servy-logo.png -Destination "$payload/assets"
    Copy-Item docs -Destination $payload -Recurse
    Copy-Item global.json -Destination $payload
    $zipName = "llm-servy-$version-win-x64.zip"
    $zip = Join-Path $staging $zipName
    Compress-Archive -Path "$payload/*" -DestinationPath $zip -CompressionLevel Optimal
    $hash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
    [IO.File]::WriteAllText("$zip.sha256", "$hash  $zipName`n", [Text.Encoding]::ASCII)

    $release = Join-Path $artifacts 'release'
    New-Item -ItemType Directory -Path $release -Force | Out-Null
    Move-Item $zip, "$zip.sha256" -Destination $release -Force
    # Preserve the familiar local launch path, replacing only generated publish output.
    $publish = Join-Path $artifacts 'publish'
    if (Test-Path $publish) {
        if ((Get-Item $publish).Attributes -band [IO.FileAttributes]::ReparsePoint) {
            throw 'The publish directory must not be a link.'
        }
        Remove-Item $publish -Recurse -Force
    }
    Move-Item $payload -Destination $publish
    Write-Host "Ready: $release\$zipName"
    Write-Host "Requires .NET Desktop Runtime 10 x64. SHA-256: $hash"
} finally {
    if ($staging -and (Test-Path $staging)) { Remove-Item $staging -Recurse -Force }
    Pop-Location
}
