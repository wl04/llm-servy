$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    dotnet publish src/LlmServy/llm-servy.csproj -c Release -r win-x64 --self-contained false -o artifacts/publish
    if ($LASTEXITCODE -ne 0) { throw "Publish failed ($LASTEXITCODE)." }
    Write-Host "Ready: $PSScriptRoot\artifacts\publish\llm-servy.exe"
} finally { Pop-Location }
