$ErrorActionPreference='Stop'
$src=$PSScriptRoot
New-Item -ItemType Directory -Path (Join-Path $src "dist") -Force | Out-Null
$exe=Join-Path $src 'dist\NOMAD-Emergencia-v6.exe'
$ps=Get-Content "$src\Install.ps1" -Raw
$linux=(Get-Content "$src\linux.sh" -Raw).Replace("`r`n","`n")
$compose=(Get-Content "$src\compose.yaml" -Raw).Replace('project-nomad:latest','project-nomad:1.35.1')
$ps.Replace('__LINUX__',[Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($linux))).Replace('__COMPOSE__',[Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($compose))) | Set-Content "$src\Embedded.ps1" -Encoding utf8BOM
$csc="$env:windir\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
& $csc /nologo /warnaserror /target:winexe /platform:x64 /optimize+ /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Xml.Linq.dll /reference:System.Web.Extensions.dll /reference:System.IO.Compression.FileSystem.dll "/resource:$src\Embedded.ps1,install" "/out:$exe" "$src\Launcher.cs" "$src\Catalog.cs"
if ($LASTEXITCODE -ne 0) { throw 'Falha na compilação.' }
$err=$null
[void][System.Management.Automation.Language.Parser]::ParseFile("$src\Embedded.ps1",[ref]$null,[ref]$err)
if ($err) { throw ($err | Out-String) }
$fixture=Join-Path $src ('kit-simulado-'+[guid]::NewGuid().ToString('N'))
$d=Join-Path $fixture 'Dados'
New-Item -ItemType Directory -Path $d -Force | Out-Null
Set-Content -LiteralPath "$d\NOMAD.tar" -Value 'arquivo simulado'
Set-Content -LiteralPath "$d\WSL-x64.msi" -Value 'arquivo simulado'
$entries=Get-ChildItem -LiteralPath $d -File | ForEach-Object { @{path=$_.Name;bytes=$_.Length;sha256=(Get-FileHash -LiteralPath $_.FullName).Hash} }
@{version=1;id=[guid]::NewGuid().ToString('N');files=@($entries)} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath "$d\Kit-pronto.json"
function Run-Test($argument,$expected,$label) {
    $p=Start-Process -FilePath $exe -ArgumentList $argument -WindowStyle Hidden -Wait -PassThru -RedirectStandardOutput "$src\$label.log" -RedirectStandardError "$src\$label-errors.log"
    if ($p.ExitCode -ne $expected) { Get-Content "$src\$label.log","$src\$label-errors.log"; throw "$label retornou $($p.ExitCode), esperado $expected" }
    "$label OK (código $($p.ExitCode))"
}
Run-Test '--self-test' 0 'progresso'
Run-Test "--verify-test `"$fixture`"" 0 'kit-integro'
Set-Content -LiteralPath "$d\NOMAD.tar" -Value 'arquivo corromp.'
Run-Test "--verify-test `"$fixture`"" 1 'kit-corrompido'
Run-Test "--verify-test `"$src`"" 1 'kit-ausente'
Run-Test '--catalog-test' 0 'catalogo-online'
Get-Content "$src\catalogo-online.log"
$p=Start-Process -FilePath $exe -ArgumentList "--preview `"$src\preview-final.png`"" -WindowStyle Hidden -Wait -PassThru
if ($p.ExitCode -ne 0 -or -not (Test-Path "$src\preview-final.png")) { throw 'Falha ao renderizar a janela' }
Get-Item -LiteralPath $exe,"$src\preview-final.png" | Select-Object Name,Length






