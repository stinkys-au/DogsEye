param([switch]$Publish)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
Push-Location $projectRoot
try {
    dotnet build DogsEye.slnx -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
    dotnet run --project tests/DogsEye.Tests -c Release --no-build
    if ($LASTEXITCODE -ne 0) { throw 'Acceptance tests failed.' }
    if ($Publish) {
        dotnet publish src/DogsEye.App -c Release -r win-x64 --self-contained true -o artifacts/DogsEye
        if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
        Copy-Item README.md,VALIDATION.md artifacts/DogsEye/ -Force
        foreach ($license in Get-ChildItem 'sdk/*Agreement*.pdf') {
            $destination = Join-Path 'artifacts/DogsEye' $license.Name
            # Leave an identical license in place, including when a PDF reader has it open.
            if (!(Test-Path -LiteralPath $destination) -or
                (Get-FileHash -LiteralPath $license.FullName).Hash -ne (Get-FileHash -LiteralPath $destination).Hash) {
                Copy-Item -LiteralPath $license.FullName -Destination $destination -Force
            }
        }
    }
} finally { Pop-Location }
