param([ValidateSet('preparar','instalar','verificar','demonstrar')][string]$Mode)
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false)
$kit = [IO.Path]::GetFullPath($env:NOMAD_KIT_ROOT)
$data = Join-Path $kit 'Dados'
$library = Join-Path $data 'Acervo'
$archive = Join-Path $data 'NOMAD.tar'
$receipt = Join-Path $data 'Kit-pronto.json'
$msi = Join-Path $data 'WSL-x64.msi'
$wslHash = '2313efc5873e3f8552f42e77f950fabd913c0289078fa89d2100f3421a525eaa'
$ubuntuHash = 'bb415d824822c4b878125729af451a5d18fb13d1cf5cbed9a7393ad64ac6039e'
$selected = $env:NOMAD_KIT_SELECTION -split ',' | Where-Object { $_ }
$base = 'http://127.0.0.1:8080'

function Check-Pause {
    if ($env:NOMAD_KIT_PAUSE -and (Test-Path -LiteralPath $env:NOMAD_KIT_PAUSE)) {
        throw 'Preparação pausada. Clique em Baixar e preparar offline com a mesma seleção para retomar.'
    }
}
function Progress-Line([int]$Percent,[string]$Label) {
    Check-Pause
    Write-Host "PROGRESS|$Percent|$Label"
}
function Transfer-Label([string]$Name,[long]$Done,[long]$Total,[double]$Rate) {
    $amount = "{0:N2} GB" -f ($Done/1e9)
    if ($Total -gt 0) { $amount += " de {0:N2} GB" -f ($Total/1e9) }
    $eta = if ($Rate -gt 0 -and $Total -gt $Done) {
        "{0:N2} h restantes" -f (($Total-$Done)/$Rate/3600)
    } elseif ($Total -le 0) { 'tamanho total indisponível' }
    else { 'calculando tempo restante' }
    $speed = if ($Rate -gt 0) { " · {0:N2} MB/s" -f ($Rate/1e6) } else { '' }
    return "$Name · $amount$speed · $eta"
}
function Wsl-Run {
    & wsl.exe @args
    if ($LASTEXITCODE -ne 0) { throw "Falha no WSL (código $LASTEXITCODE). Consulte os detalhes." }
}
function Linux-Path([string]$Name,[string]$Path) {
    $value = Wsl-Run -d $Name -u root --exec wslpath -u $Path
    return (($value -join '').Replace([string][char]0,'')).Trim()
}
function Linux-Step([string]$Name,[string]$Step,[string]$Source) {
    $script = Linux-Path $Name $linuxFile
    $linuxSource = if ($Source) { Linux-Path $Name $Source } else { '-' }
    Wsl-Run -d $Name -u root --exec bash $script $Step $linuxSource
}
function Has-Distro([string]$Name) {
    $list = & wsl.exe --list --quiet 2>$null
    return [bool]($list | Where-Object { $_.Replace([string][char]0,'').Trim() -eq $Name })
}
function Get-Hash([string]$Path) {
    $stream = [IO.File]::OpenRead($Path)
    $sha = [Security.Cryptography.SHA256]::Create()
    try { return [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-','') }
    finally { $sha.Dispose(); $stream.Dispose() }
}
function Download([string]$Url,[string]$Destination,[long]$Expected=0,[string]$Hash='') {
    if (-not (Test-Path -LiteralPath $Destination -PathType Leaf)) {
        $part = "$Destination.part"
        $label = Split-Path $Destination -Leaf
        if ($Expected -le 0) {
            try {
                $head = Invoke-WebRequest -UseBasicParsing -Method Head -Uri $Url -TimeoutSec 12
                $Expected = [long]($head.Headers['Content-Length'] | Select-Object -First 1)
            } catch {}
        }
        $command = @('--fail','--location','--silent','--show-error','--retry','2','--connect-timeout','20',
            '--speed-time','120','--speed-limit','1024','--continue-at','-','--output',('"' + $part + '"'),('"' + $Url + '"'))
        $start = New-Object System.Diagnostics.ProcessStartInfo
        $start.FileName = Join-Path $env:windir 'System32\curl.exe'
        $start.Arguments = $command -join ' '
        $start.UseShellExecute = $false
        $start.CreateNoWindow = $true
        $clock = [Diagnostics.Stopwatch]::StartNew()
        $previous = Get-Item -LiteralPath $part -ErrorAction SilentlyContinue
        $baseline = if ($previous) { [long]$previous.Length } else { 0L }
        $samples = New-Object 'System.Collections.Generic.Queue[object]'
        $samples.Enqueue(@{seconds=0.0;bytes=$baseline})
        $transfer = [Diagnostics.Process]::Start($start)
        try {
            do {
                $file = Get-Item -LiteralPath $part -ErrorAction SilentlyContinue
                $received = if ($file) { [long]$file.Length } else { 0 }
                $elapsed = $clock.Elapsed.TotalSeconds
                while ($samples.Count -gt 1 -and $elapsed - $samples.Peek().seconds -gt 20) { $null = $samples.Dequeue() }
                $first = $samples.Peek()
                $rate = if ($elapsed - $first.seconds -ge 2 -and $received -gt $first.bytes) {
                    ($received-$first.bytes)/($elapsed-$first.seconds)
                } else { 0 }
                $samples.Enqueue(@{seconds=$elapsed;bytes=$received})
                $percent = if ($Expected -gt 0) { [math]::Min(99,[math]::Floor(100*$received/$Expected)) } else { -1 }
                Progress-Line $percent (Transfer-Label $label $received $Expected $rate)
            } until ($transfer.WaitForExit(2000))
        } finally {
            if (-not $transfer.HasExited) { $transfer.Kill(); $transfer.WaitForExit() }
        }
        $exit = $transfer.ExitCode
        $transfer.Dispose()
        if ($exit -ne 0) { throw "Download incompleto: $label (curl $exit). Clique em Baixar e preparar offline novamente para retomar." }
        if ((Get-Item -LiteralPath $part).Length -eq 0) { throw "Download vazio: $label" }
        if ($Hash -and (Get-Hash $part) -ne $Hash) { throw "SHA-256 incorreto: $label. Remova o arquivo .part antes de repetir." }
        if ($label.EndsWith('.zim') -or $label.EndsWith('.pdf') -or $label.EndsWith('.zip') -or $label.EndsWith('.apk') -or $label.EndsWith('.exe') -or $label.EndsWith('.xz')) {
            $stream = [IO.File]::OpenRead($part)
            try {
                $magic = New-Object byte[] 6
                [void]$stream.Read($magic,0,$magic.Length)
                $hex = [BitConverter]::ToString($magic)
                $prefix = switch ([IO.Path]::GetExtension($label)) {
                    '.zim' { '5A-49-4D-04' }
                    '.pdf' { '25-50-44-46-2D' }
                    '.zip' { '50-4B-03-04' }
                    '.apk' { '50-4B-03-04' }
                    '.exe' { '4D-5A' }
                    '.xz'  { 'FD-37-7A-58-5A-00' }
                }
                if (-not $hex.StartsWith($prefix)) { throw "Formato inesperado: $label" }
            } finally { $stream.Dispose() }
        }
        Move-Item -LiteralPath $part -Destination $Destination
        Progress-Line 100 "$label · download concluído"
        Write-Host "100% · $label"
    } elseif ($Hash -and (Get-Hash $Destination) -ne $Hash) {
        throw "SHA-256 incorreto: $Destination"
    }
}
function Ensure-Wsl {
    $feature = Get-CimInstance Win32_OptionalFeature -Filter "Name='VirtualMachinePlatform'"
    if ($feature.InstallState -ne 1) {
        Progress-Line -1 'Ativando a plataforma de virtualização'
        $proc = Start-Process dism.exe -Verb RunAs -ArgumentList '/online /enable-feature /featurename:VirtualMachinePlatform /all /norestart' -Wait -PassThru
        if ($proc.ExitCode -notin @(0,3010)) { throw "DISM retornou $($proc.ExitCode)." }
        throw 'Reinicie o Windows e execute novamente esta mesma ação.'
    }
    & wsl.exe --version *> $null
    if ($LASTEXITCODE -ne 0) {
        if ((Get-Hash $msi) -ne $wslHash) { throw 'O instalador WSL não passou na verificação SHA-256.' }
        Progress-Line -1 'Instalando WSL2'
        $proc = Start-Process msiexec.exe -Verb RunAs -ArgumentList ('/i "'+$msi+'" /passive /norestart') -Wait -PassThru
        if ($proc.ExitCode -eq 3010) { throw 'Reinicie o Windows e execute novamente esta mesma ação.' }
        if ($proc.ExitCode -ne 0) { throw "Instalação WSL retornou $($proc.ExitCode)." }
    }
}
function Api([string]$Path,[string]$Method='Get',$Body=$null,[int]$Timeout=30) {
    $params = @{Uri=$base+$Path; Method=$Method; TimeoutSec=$Timeout}
    if ($null -ne $Body) { $params.ContentType='application/json'; $params.Body=ConvertTo-Json -InputObject $Body -Compress }
    Invoke-RestMethod @params
}
function Install-Service([string]$Distro,[string]$Service) {
    $state = & wsl.exe -d $Distro -u root --exec docker inspect -f '{{.State.Running}}' $Service 2>$null
    if (($state -join '').Trim() -eq 'true') { return }
    $result = Api '/api/system/services/install' 'Post' @{service_name=$Service}
    if (-not $result.success) { throw "Não foi possível iniciar a instalação de $Service." }
    $deadline = [datetime]::Now.AddHours(4)
    do {
        Progress-Line -1 "Instalando $Service"
        Start-Sleep -Seconds 12
        $state = & wsl.exe -d $Distro -u root --exec docker inspect -f '{{.State.Running}}' $Service 2>$null
        if (($state -join '').Trim() -eq 'true') { return }
    } while ([datetime]::Now -lt $deadline)
    throw "Tempo de instalação excedido para $Service."
}
function Prepare-Map {
    $null = Api '/api/maps/setup-world-basemap' 'Post' $null 360
    $jobs = @(Api '/api/downloads/jobs/map')
    $job = $jobs | Where-Object title -eq 'Brasil' | Select-Object -First 1
    if ($job.status -eq 'failed') {
        $null = Api "/api/downloads/jobs/$($job.jobId)/retry" 'Post'
    } elseif (-not $job) {
        $request = @{countries=@('BR');maxzoom=15}
        $preflight = Api '/api/maps/extract-preflight' 'Post' $request 90
        $request.label='Brasil'
        $request.estimatedBytes=$preflight.bytes
        $job = Api '/api/maps/extract' 'Post' $request 90
    }
    $deadline = [datetime]::Now.AddHours(12)
    do {
        $jobs = @(Api '/api/downloads/jobs/map')
        $current = $jobs | Where-Object { $_.jobId -eq $job.jobId -or $_.title -eq 'Brasil' } | Select-Object -First 1
        if ($current.status -eq 'failed') { throw 'A extração do mapa do Brasil falhou. Consulte os downloads do NOMAD.' }
        if (-not $current -or $current.status -eq 'completed') {
            $maps = Wsl-Run -d $build -u root --exec find /opt/project-nomad/storage/maps/pmtiles -maxdepth 1 -type f -name 'br_*_z15.pmtiles'
            if ($maps) { return }
            throw 'A extração terminou sem encontrar o arquivo PMTiles do Brasil.'
        }
        $percent = if ($null -ne $current.progress) { [math]::Min(99,[int]$current.progress) } else { -1 }
        Progress-Line $percent 'Extraindo o mapa detalhado do Brasil · tempo depende da extração e validação'
        Start-Sleep -Seconds 12
    } while ([datetime]::Now -lt $deadline)
    throw 'A extração continua pendente. Clique em Baixar e preparar offline novamente.'
}
function Wait-NomadCatalog {
    if (-not $env:NOMAD_KIT_CONTINUE) { throw 'Abra a interface do instalador para escolher no painel.' }
    Progress-Line -1 'Escolha no painel e clique em Concluir seleção aqui quando terminar'
    Write-Host 'Use Easy Setup, Information Library, Creator Packs e Supply Depot para escolher conteúdo e apps.'
    Write-Host 'Aguarde a conclusão das instalações de aplicativos no painel antes de clicar em Concluir seleção.'
    Start-Process "$base/easy-setup"
    Start-Process "$base/supply-depot"
    Write-Host 'PICK'
    while (-not (Test-Path -LiteralPath $env:NOMAD_KIT_CONTINUE)) {
        Check-Pause
        Start-Sleep -Seconds 2
    }
    $deadline = [datetime]::Now.AddHours(24)
    $previousJobs = @{}
    do {
        $jobs = @(Api '/api/downloads/jobs')
        if ($jobs | Where-Object status -in @('failed','error')) { throw 'Há downloads com erro no painel. Retome o preparo e corrija os itens antes de exportar.' }
        $pending = @($jobs | Where-Object { $_.status -notin @('completed','succeeded','cancelled') })
        if (-not $pending.Count) { return }
        $now = [datetime]::UtcNow
        foreach ($job in $pending) {
            $id = [string]$job.jobId
            $done = [long]$job.downloadedBytes
            $total = [long]$job.totalBytes
            $previous = $previousJobs[$id]
            $rate = if ($job.status -eq 'active' -and $previous -and $done -gt $previous.bytes) {
                ($done-$previous.bytes)/($now-$previous.time).TotalSeconds
            } else { 0 }
            $previousJobs[$id] = @{time=$now;bytes=$done}
            $name = if ($job.title) { $job.title } elseif ($job.filepath) { Split-Path $job.filepath -Leaf } else { $id }
            $percent = if ($null -ne $job.progress) { "$([math]::Min(99,[int]$job.progress))%" } else { '—' }
            Write-Host ("{0} · {1}" -f $percent,(Transfer-Label $name $done $total $rate))
        }
        Progress-Line -1 "Aguardando $($pending.Count) transferências do N.O.M.A.D. · veja os detalhes abaixo"
        Start-Sleep -Seconds 12
    } while ([datetime]::Now -lt $deadline)
    throw 'Há downloads pendentes no painel. Retome o preparo quando quiser continuar.'
}
function Verify-Kit {
    if (-not (Test-Path -LiteralPath $receipt -PathType Leaf)) { throw 'Kit ainda não preparado. Escolha Baixar e preparar offline enquanto estiver conectado.' }
    $manifest = Get-Content -LiteralPath $receipt -Raw | ConvertFrom-Json
    if ($manifest.version -ne 1 -or $manifest.id -notmatch '^[a-f0-9]{32}$') { throw 'Versão ou identificação de kit inválida.' }
    $entries = @($manifest.files)
    if ('NOMAD.tar' -notin $entries.path -or 'WSL-x64.msi' -notin $entries.path) { throw 'Manifesto sem os arquivos essenciais.' }
    for ($i=0; $i -lt $entries.Count; $i++) {
        $item=$entries[$i]
        if ($item.path -notmatch '^(?:[a-zA-Z0-9_.-]+[\\/])*[a-zA-Z0-9_.-]+$' -or $item.path -match '(^|[\\/])\.{1,2}([\\/]|$)') { throw 'Caminho inválido no manifesto.' }
        $path = Join-Path $data $item.path
        Progress-Line ([int](100*$i/$entries.Count)) "Verificando $($item.path)"
        if (-not (Test-Path -LiteralPath $path -PathType Leaf) -or (Get-Item -LiteralPath $path).Length -ne $item.bytes -or (Get-Hash $path) -ne $item.sha256) {
            throw "Arquivo ausente ou alterado: $($item.path)"
        }
    }
    Progress-Line 100 'Kit verificado'
    return $manifest
}

try {
    if ($Mode -eq 'demonstrar') {
        foreach ($value in 0,12,29,50,73,91,100) {
            Progress-Line $value 'Simulação · nenhum arquivo está sendo baixado'
            Start-Sleep -Milliseconds 260
        }
        exit 0
    }
    if ($Mode -eq 'verificar') { $null = Verify-Kit; exit 0 }
    if ($env:PROCESSOR_ARCHITECTURE -ne 'AMD64') { throw 'Este executável requer Windows x64.' }
    $runtime = Join-Path $env:LOCALAPPDATA 'NOMAD-Emergencia'
    $linuxFile = Join-Path $runtime 'nomad-linux.sh'
    $composeFile = Join-Path $runtime 'nomad-compose.yaml'
    New-Item -ItemType Directory -Path $runtime -Force | Out-Null
    [IO.File]::WriteAllBytes($linuxFile,[Convert]::FromBase64String('__LINUX__'))
    [IO.File]::WriteAllBytes($composeFile,[Convert]::FromBase64String('__COMPOSE__'))

    if ($Mode -eq 'instalar') {
        $manifest = Verify-Kit
        Ensure-Wsl
        $name = 'NOMAD-Emergencia'
        $target = Join-Path $runtime 'Ubuntu'
        $installedId = Join-Path $runtime 'Instalado-id.txt'
        if (-not (Has-Distro $name)) {
            if (Get-NetTCPConnection -LocalPort 8080 -State Listen -ErrorAction SilentlyContinue) { throw 'A porta 8080 está em uso. Feche o serviço que a utiliza e tente novamente.' }
            $free = [IO.DriveInfo]::new([IO.Path]::GetPathRoot($target)).AvailableFreeSpace
            if ($free -lt ((Get-Item -LiteralPath $archive).Length + 3GB)) { throw 'Espaço insuficiente na unidade de instalação do Windows.' }
            Progress-Line -1 'Importando o servidor do arquivo offline'
            New-Item -ItemType Directory -Path $target -Force | Out-Null
            Wsl-Run --import $name $target $archive --version 2
            Set-Content -LiteralPath $installedId -Value $manifest.id -Encoding ASCII
        } elseif (-not (Test-Path -LiteralPath $installedId) -or (Get-Content -LiteralPath $installedId -Raw).Trim() -ne $manifest.id) {
            throw 'Já existe uma distribuição NOMAD-Emergencia associada a outro kit.'
        }
        Progress-Line -1 'Iniciando serviços locais'
        Linux-Step $name 'offline' $library
        if ((Api '/api/health').status -ne 'ok') { throw 'O painel local ainda não respondeu.' }
        $keepalive = Get-CimInstance Win32_Process -Filter "Name='wsl.exe'" | Where-Object CommandLine -Match '(?i)-d NOMAD-Emergencia -u root --exec sleep infinity'
        if (-not $keepalive) {
            Start-Process -FilePath (Join-Path $env:windir 'System32\wsl.exe') -ArgumentList "-d $name -u root --exec sleep infinity" -WindowStyle Hidden
        }
        Progress-Line 100 'Servidor ativo · http://localhost:8080'
        try { Start-Process 'http://localhost:8080' } catch { Write-Host 'Abra http://localhost:8080 no navegador.' }
        exit 0
    }

    if (Test-Path -LiteralPath $receipt) { throw 'Esta pasta já contém um kit pronto. Para outra seleção, use uma nova pasta.' }
    if ($kit -notmatch '^[a-zA-Z]:\\') { throw 'Escolha uma pasta em uma unidade local, como D:\NOMAD.' }
    if ([IO.DriveInfo]::new([IO.Path]::GetPathRoot($kit)).DriveFormat -eq 'FAT32') { throw 'Use NTFS ou exFAT para guardar arquivos maiores que 4 GB.' }
    New-Item -ItemType Directory -Path $library -Force | Out-Null
    $extras = Join-Path $data 'Extras'
    New-Item -ItemType Directory -Path $extras -Force | Out-Null
    $selectionFile = Join-Path $data 'Selecao.txt'
    $customFile = Join-Path $data 'Selecao-Kiwix.json'
    $custom = @(Get-Content -LiteralPath $env:NOMAD_KIT_CATALOG -Raw | ConvertFrom-Json)
    $customJson = ConvertTo-Json -InputObject @($custom | Sort-Object file) -Depth 4 -Compress
    $selection = ($selected | Sort-Object) -join ','
    if (Test-Path -LiteralPath $selectionFile) {
        if ((Get-Content -LiteralPath $selectionFile -Raw).Trim() -ne $selection) { throw 'Para retomar, use as mesmas opções da primeira execução ou escolha uma nova pasta.' }
    } else { Set-Content -LiteralPath $selectionFile -Value $selection -Encoding UTF8 }
    if (Test-Path -LiteralPath $customFile) {
        if ((Get-Content -LiteralPath $customFile -Raw).Trim() -ne $customJson) { throw 'Para retomar, preserve a seleção do catálogo ou escolha uma nova pasta.' }
    } else { Set-Content -LiteralPath $customFile -Value $customJson -Encoding UTF8 }
    Download 'https://github.com/microsoft/WSL/releases/download/3.0.5/wsl.3.0.5.0.x64.msi' $msi 367788032 $wslHash
    Ensure-Wsl
    $planFile = Join-Path $data 'Catalogo-PT.json'
    $plan = @()
    if ('wiki' -in $selected -or 'kiwixpt' -in $selected) {
        if (-not (Test-Path -LiteralPath $planFile)) {
            Progress-Line -1 'Consultando a biblioteca em português'
            [xml]$feed = (Invoke-WebRequest -UseBasicParsing 'https://opds.library.kiwix.org/catalog/v2/entries?lang=por&count=1000' -TimeoutSec 60).Content
            if (@($feed.feed.entry).Count -ne [int]$feed.feed.totalResults) { throw 'O catálogo retornou uma página incompleta.' }
            $items = foreach ($entry in $feed.feed.entry) {
                if ($entry.language -ne 'por') { continue }
                if ('kiwixpt' -notin $selected -and $entry.name -ne 'wikipedia_pt_all') { continue }
                $link = $entry.link | Where-Object type -eq 'application/x-zim' | Select-Object -First 1
                if (-not $link) { continue }
                $url = [uri]($link.href -replace '\.meta4$','')
                $file = [IO.Path]::GetFileName($url.AbsolutePath)
                if ($url.Scheme -ne 'https' -or $url.Host -notin @('lb.download.kiwix.org','lbo.download.kiwix.org','download.kiwix.org') -or $file -notmatch '^[A-Za-z0-9._-]+\.zim$') { throw 'URL inesperada no catálogo Kiwix.' }
                [pscustomobject]@{name=[string]$entry.name;file=$file;url=$url.AbsoluteUri;size=[long]$link.length}
            }
            $plan = @($items | Group-Object name | ForEach-Object { $_.Group | Sort-Object size -Descending | Select-Object -First 1 })
            if (-not $plan.Count) { throw 'Nenhum acervo português encontrado.' }
            $plan | ConvertTo-Json | Set-Content -LiteralPath $planFile -Encoding UTF8
        }
        $plan = @(Get-Content -LiteralPath $planFile -Raw | ConvertFrom-Json)
    }
    if ('wikien' -in $selected) {
        $file = 'wikipedia_en_all_maxi_2026-08.zim'
        $plan += [pscustomobject]@{file=$file;url="https://lb.download.kiwix.org/zim/wikipedia/$file";size=127418088448L}
    }
    $plan = @((@($custom) + @($plan)) | Group-Object file | ForEach-Object { $_.Group[0] })
    if ($plan.Count) {
        foreach ($item in $plan) {
            $url = [uri]$item.url
            if ($item.file -notmatch '^[A-Za-z0-9._-]+\.zim$' -or $url.Scheme -ne 'https' -or $url.Host -notin @('lb.download.kiwix.org','lbo.download.kiwix.org','download.kiwix.org') -or [IO.Path]::GetFileName($url.AbsolutePath) -cne $item.file) { throw 'Catálogo local inválido.' }
        }
        $need = ($plan | Where-Object { -not (Test-Path -LiteralPath (Join-Path $library $_.file)) } | Measure-Object size -Sum).Sum
        if ([IO.DriveInfo]::new([IO.Path]::GetPathRoot($kit)).AvailableFreeSpace -lt ($need + 10GB)) { throw 'Espaço livre insuficiente para o acervo e os arquivos básicos do servidor.' }
        foreach ($item in $plan) {
            Download $item.url (Join-Path $library $item.file) $item.size
        }
    }
    if ('guias' -in $selected) {
        Download 'https://www.gov.br/saude/pt-br/centrais-de-conteudo/publicacoes/svsa/enchentes/evite-mais-riscos-em-situacoes-de-desastres-ou-emergencias' (Join-Path $extras 'Agua-e-alimentos.pdf')
        Download 'https://bvsms.saude.gov.br/bvs/publicacoes/nocoes_primeiros_socorros.pdf' (Join-Path $extras 'Primeiros-socorros.pdf')
        Download 'https://www.infoteca.cnptia.embrapa.br/infoteca/bitstream/doc/1168224/1/CartilhaHortas.pdf' (Join-Path $extras 'Hortas-Embrapa.pdf')
    }
    if ('portatil' -in $selected) {
        Download 'https://download.kiwix.org/release/kiwix-desktop/kiwix-desktop_windows_x64_2.5.1.zip' (Join-Path $extras 'Kiwix-Desktop-x64.zip')
    }
    if ('kolibriwin' -in $selected) {
        Download 'https://learningequality.org/r/kolibri-windows-setup-latest' (Join-Path $extras 'Kolibri-Windows.exe')
    }
    if ('android' -in $selected) {
        Download 'https://f-droid.org/repo/net.osmand.plus_541003.apk' (Join-Path $extras 'OsmAnd-Android-arm64.apk')
        [xml]$index = (Invoke-WebRequest -UseBasicParsing 'https://download.osmand.net/get_indexes' -TimeoutSec 60).Content
        $regions = @($index.osmand_regions.region | Where-Object { $_.type -eq 'map' -and $_.name -match '^Brazil_[a-z-]+_southamerica_2\.obf\.zip$' })
        if ($regions.Count -ne 27) { throw "O catálogo OsmAnd retornou $($regions.Count) regiões brasileiras. Esperadas 27." }
        foreach ($region in $regions) {
            $file = [string]$region.name
            Download "https://download.osmand.net/download.php?standard=yes&file=$file" (Join-Path $extras $file) ([long]$region.containerSize)
        }
    }
    if ('iiab' -in $selected) {
        $image = (Invoke-RestMethod 'https://iiab.io/fast.json' -TimeoutSec 45).os_list | Where-Object { $_.name -match 'SMALL.*64-bit' } | Select-Object -First 1
        if (-not $image -or $image.url -notmatch '^https://(?:download\.)?iiab\.io/[^?]+\.img\.xz$') { throw 'Imagem oficial do Internet-in-a-Box indisponível.' }
        Download $image.url (Join-Path $extras 'Internet-in-a-Box-Raspberry-Pi.img.xz') ([long]$image.image_download_size)
        Download 'https://downloads.raspberrypi.com/imager/imager_latest.exe' (Join-Path $extras 'Raspberry-Pi-Imager-Windows.exe')
    }

    $identity = Join-Path $data 'Preparo-id.txt'
    if (-not (Test-Path -LiteralPath $identity)) { Set-Content -LiteralPath $identity -Value ([guid]::NewGuid().ToString('N')) -Encoding ASCII }
    $id = (Get-Content -LiteralPath $identity -Raw).Trim()
    if ($id -notmatch '^[a-f0-9]{32}$') { throw 'Identificador de preparo inválido.' }
    $build = "NOMAD-Preparo-$id"
    if (-not (Test-Path -LiteralPath $archive)) {
        $listeners = @(Get-NetTCPConnection -LocalPort 8080 -State Listen -ErrorAction SilentlyContinue)
        if ($listeners.Count) { throw 'A porta 8080 está em uso. Feche o servidor que a utiliza e escolha Baixar e preparar offline novamente.' }
        $buildDir = Join-Path ([IO.Path]::GetPathRoot($kit)) $build
        if (-not (Has-Distro $build)) {
            $ubuntu = Join-Path $data 'Ubuntu-fonte.wsl'
            Download 'https://releases.ubuntu.com/24.04.5/ubuntu-24.04.5-wsl-amd64.wsl' $ubuntu 388975696 $ubuntuHash
            Progress-Line -1 'Criando ambiente de preparo'
            New-Item -ItemType Directory -Path $buildDir -Force | Out-Null
            Wsl-Run --import $build $buildDir $ubuntu --version 2
        }
        try {
            Progress-Line -1 'Preparando Ubuntu e Docker'
            Linux-Step $build 'setup' $composeFile
            Start-Process -FilePath (Join-Path $env:windir 'System32\wsl.exe') -ArgumentList "-d $build -u root --exec sleep infinity" -WindowStyle Hidden
            Progress-Line -1 'Baixando imagens e iniciando o painel'
            Linux-Step $build 'online' $library
            if ((Api '/api/health').status -ne 'ok') { throw 'O painel de preparo não respondeu.' }
            if ($plan.Count) {
                Install-Service $build 'nomad_kiwix_server'
                $null = Api '/api/zim/rescan-library' 'Post' $null 300
            }
            $mapMarker = Join-Path $data 'Mapa-Brasil.ok'
            if ('brasil' -in $selected -and -not (Test-Path -LiteralPath $mapMarker)) {
                Prepare-Map
                Set-Content -LiteralPath $mapMarker -Value 'Extração concluída' -Encoding UTF8
            }
            if ('cursos' -in $selected) {
                Install-Service $build 'nomad_kolibri_2'
                foreach ($channel in '265e242783c947ccbc1aafbe4bd64194','aa25450559b55bd79bc90c09dfb805d2','c367b7d7cf625b9aa525972cad27c602','2ac071c4672354f2aa78953448f81e50') {
                    Progress-Line -1 "Importando canal Kolibri $channel"
                    Wsl-Run -d $build -u root --exec docker exec nomad_kolibri_2 kolibri manage importchannel network $channel
                    Wsl-Run -d $build -u root --exec docker exec nomad_kolibri_2 kolibri manage importcontent network $channel
                }
            }
            if ('ia' -in $selected) {
                Install-Service $build 'nomad_ollama'
                Progress-Line -1 'Baixando o modelo local Qwen3 4B'
                Wsl-Run -d $build -u root --exec docker exec nomad_ollama ollama pull qwen3:4b
            }
            if ('nomadcatalog' -in $selected) { Wait-NomadCatalog }
            Linux-Step $build 'stop' ''
        } finally { & wsl.exe --terminate $build | Out-Null }
        Progress-Line -1 'Exportando Ubuntu, serviços e conteúdo local'
        $pending = "$archive.part"
        Wsl-Run --export $build $pending
        Move-Item -LiteralPath $pending -Destination $archive
    }
    $files = @(
        Get-Item -LiteralPath $archive,$msi,$selectionFile,$customFile
        Get-ChildItem -LiteralPath $library,$extras -File | Where-Object { $_.Name -notmatch '\.(part|tmp)$' }
    )
    $entries = foreach ($file in $files) {
        Progress-Line -1 "Calculando SHA-256 de $($file.Name)"
        @{path=$file.FullName.Substring($data.Length+1);bytes=$file.Length;sha256=(Get-Hash $file.FullName)}
    }
    @{version=1;id=$id;files=@($entries)} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath "$receipt.part" -Encoding UTF8
    Move-Item -LiteralPath "$receipt.part" -Destination $receipt
    Progress-Line 100 'Kit pronto para guardar'
    exit 0
} catch {
    Write-Host "ERROR|$($_.Exception.Message)"
    Write-Host ("{0}: {1}" -f $_.Exception.GetType().FullName,$_.FullyQualifiedErrorId)
    Write-Host $_.ScriptStackTrace
    exit 1
}

