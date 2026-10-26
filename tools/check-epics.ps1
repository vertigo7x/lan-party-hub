<#
.SYNOPSIS
    Verifica la integridad estructural del backlog epic/AC de LAN Party Hub.

.DESCRIPTION
    Comprueba sobre el epics.md de la pasada vigente:

      1. Conteo de `## Epic N:`, `### Story N.M` y de los bloques
         `**Given**` / `**When**` / `**Then**`, y paridad de los tres
         contadores de bloques GWT.
      2. Continuidad de los IDs de historia (1.1 .. ultima), sin huecos ni
         duplicados.
      3. Que toda cita de `FR-n`, `NFR-n`, `UX-DR-n`, `AR-n`, `AD-n`, `SM-n` y
         `SM-Cn` cae dentro de rango.
      4. Bidireccionalidad de la tabla de trazabilidad de AR: cada AC citada
         en la fila de `AR-n` existe en su historia y cita ese AR, y cada AC
         que cita `AR-n` aparece listada en la fila de `AR-n`.
      5. Que no quedan marcadores de plantilla (`{{...}}`, `<PLACEHOLDER>`,
         `TODO`, `TBD`).

    Solo lectura: no modifica ficheros. Devuelve 1 si alguna comprobacion
    falla, impresando el detalle de cada incumplimiento.

.EXAMPLE
    powershell -NoProfile -File tools/check-epics.ps1
    powershell -NoProfile -File tools/check-epics.ps1 -Quiet
#>
[CmdletBinding()]
param(
    [string] $Path = '_bmad-output/planning-artifacts/epics/epics-lan-party-hub-2026-10-26/epics.md',
    [switch] $Quiet
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
Set-Location $repoRoot

# Rangos validos de cada familia de identificadores.
$ranges = @(
    @{ Label = 'FR';    Min = 1;  Max = 11 }
    @{ Label = 'NFR';   Min = 1;  Max = 10 }
    @{ Label = 'UX-DR'; Min = 1;  Max = 45 }
    @{ Label = 'AR';    Min = 1;  Max = 25 }
    @{ Label = 'AD';    Min = 1;  Max = 34 }
    @{ Label = 'SM';    Min = 1;  Max = 5 }
    @{ Label = 'SM-C';  Min = 1;  Max = 2 }
)

# Marcadores de plantilla. TODO y TBD se buscan en mayusculas: el documento
# esta en espanol y "todo" es una palabra corriente ("todo error"), asi que
# una busqueda sin distincion de mayusculas produciria falsos positivos.
$templateMarkers = @(
    @{ Name = '{{...}}';        Pattern = '\{\{[^}]*\}\}'; CaseSensitive = $false }
    @{ Name = '<PLACEHOLDER>';  Pattern = '<PLACEHOLDER>';   CaseSensitive = $false }
    @{ Name = 'TODO';           Pattern = '\bTODO\b';        CaseSensitive = $true  }
    @{ Name = 'TBD';            Pattern = '\bTBD\b';         CaseSensitive = $true  }
)

$failures = New-Object System.Collections.Generic.List[string]
$checks = New-Object System.Collections.Generic.List[string]

function Add-Failure {
    param([string] $Message)
    $failures.Add($Message)
}

if (-not (Test-Path -LiteralPath $Path)) {
    Write-Host "ERROR: no existe el fichero de epics: $Path" -ForegroundColor Red
    exit 1
}

$bytes = [System.IO.File]::ReadAllBytes((Resolve-Path -LiteralPath $Path).Path)
$text = [System.Text.Encoding]::UTF8.GetString($bytes)
$lines = $text -split "`r?`n"

# ---------------------------------------------------------------------------
# Indice de historias y de bloques AC.
#
# Modelo de AC: cada bloque `**Given**` abre un criterio de aceptacion
# (AC1, AC2, ...) dentro de su historia. El cuerpo del AC se extiende hasta
# el siguiente `**Given**` o hasta el siguiente encabezado de historia.
# ---------------------------------------------------------------------------
$stories = New-Object System.Collections.Generic.List[object]
$current = $null
$acIndex = 0

for ($i = 0; $i -lt $lines.Count; $i++) {
    $line = $lines[$i]
    $lineNo = $i + 1

    if ($line -match '^### Story (\d+)\.(\d+)\b') {
        $current = [pscustomobject]@{
            Key  = "$($Matches[1]).$($Matches[2])"
            Epic = [int]$Matches[1]
            Num  = [int]$Matches[2]
            Line = $lineNo
            Acs  = (New-Object System.Collections.Generic.List[object])
        }
        $stories.Add($current)
        $acIndex = 0
        continue
    }

    if ($null -eq $current) { continue }

    if ($line -match '^\*\*Given\*\*') {
        $acIndex++
        $current.Acs.Add([pscustomobject]@{
            Index = $acIndex
            Line  = $lineNo
            Body  = New-Object System.Collections.Generic.List[string]
        })
        continue
    }

    if ($acIndex -gt 0) {
        $current.Acs[$acIndex - 1].Body.Add($line)
    }
}

# ---------------------------------------------------------------------------
# 1. Conteo de epicas, historias y bloques GWT + paridad.
# ---------------------------------------------------------------------------
$epicNumbers = New-Object System.Collections.Generic.List[int]
$storyHeaders = 0
$gwt = @{ Given = 0; When = 0; Then = 0 }

for ($i = 0; $i -lt $lines.Count; $i++) {
    $line = $lines[$i]
    if ($line -match '^## Epic (\d+):') {
        $epicNumbers.Add([int]$Matches[1])
    }
    if ($line -match '^### Story (\d+)\.(\d+)\b') {
        $storyHeaders++
    }
    if ($line -match '^\*\*Given\*\*') { $gwt.Given++ }
    if ($line -match '^\*\*When\*\*')  { $gwt.When++ }
    if ($line -match '^\*\*Then\*\*')  { $gwt.Then++ }
}

$epicCount = $epicNumbers.Count
$storyCount = $storyHeaders
$acTotal = $gwt.Given

$checks.Add("Epicas: $epicCount")
$checks.Add("Historias: $storyCount")
$checks.Add("AC (bloques Given): $acTotal")
$checks.Add("Bloques GWT: Given=$($gwt.Given) When=$($gwt.When) Then=$($gwt.Then)")

if ($epicCount -eq 0) {
    Add-Failure 'No se encontro ningun encabezado "## Epic N:".'
}

$epicDup = $epicNumbers | Group-Object | Where-Object { $_.Count -gt 1 }
foreach ($d in $epicDup) {
    Add-Failure "Epic duplicada: ## Epic $($d.Name): aparece $($d.Count) veces."
}

$expectedEpic = 1
foreach ($n in ($epicNumbers | Sort-Object)) {
    if ($n -ne $expectedEpic) {
        Add-Failure "Hueco en la numeracion de epicas: se esperaba ## Epic ${expectedEpic}: y se encontro ## Epic ${n}:."
    }
    $expectedEpic = $n + 1
}

if ($storyCount -ne $stories.Count) {
    Add-Failure "Discrepancia en el conteo de historias: $storyHeaders encabezados '### Story' frente a $($stories.Count) historias indexadas."
}

if ($gwt.Given -ne $gwt.When) {
    Add-Failure "Paridad GWT rota: hay $($gwt.Given) bloques '**Given**' y $($gwt.When) bloques '**When**'."
}
if ($gwt.Given -ne $gwt.Then) {
    Add-Failure "Paridad GWT rota: hay $($gwt.Given) bloques '**Given**' y $($gwt.Then) bloques '**Then**'."
}

# ---------------------------------------------------------------------------
# 2. Continuidad de los IDs de historia (1.1 .. ultima, sin huecos ni dups).
# ---------------------------------------------------------------------------
$seen = @{}
foreach ($s in $stories) {
    if ($seen.ContainsKey($s.Key)) {
        Add-Failure "Historia duplicada: Story $($s.Key) aparece mas de una vez (lineas $($seen[$s.Key]) y $($s.Line))."
    }
    else {
        $seen[$s.Key] = $s.Line
    }
}

$byEpic = $stories | Group-Object Epic | Sort-Object { [int]$_.Name }
foreach ($g in $byEpic) {
    $nums = @($g.Group | ForEach-Object { $_.Num } | Sort-Object)
    for ($k = 0; $k -lt $nums.Count; $k++) {
        if ($nums[$k] -ne ($k + 1)) {
            Add-Failure "Hueco en la numeracion de historias de la Epic $($g.Name): se esperaba Story $($g.Name).$($k + 1) y se encontro Story $($g.Name).$($nums[$k])."
        }
    }
}

if ($stories.Count -gt 0) {
    $first = $stories | Sort-Object { $_.Line } | Select-Object -First 1
    $last = $stories | Sort-Object { $_.Line } | Select-Object -Last 1
    if ($first.Epic -ne 1 -or $first.Num -ne 1) {
        Add-Failure "La primera historia no es 1.1 sino Story $($first.Key) (linea $($first.Line))."
    }
    $checks.Add("Rango de historias: $($first.Key) .. $($last.Key)")
}

# ---------------------------------------------------------------------------
# 3. Rangos de los identificadores citados.
# ---------------------------------------------------------------------------
foreach ($r in $ranges) {
    $label = $r.Label
    $pattern = "(?<![\w-])$([regex]::Escape($label))-(\d+)(?!\w)"
    for ($i = 0; $i -lt $lines.Count; $i++) {
        foreach ($m in [regex]::Matches($lines[$i], $pattern)) {
            $value = [int]$m.Groups[1].Value
            if ($value -lt $r.Min -or $value -gt $r.Max) {
                Add-Failure ("$label-$value fuera de rango (permitido $label-$($r.Min)..$($r.Max)) en linea {0}." -f ($i + 1))
            }
        }
    }
}

# ---------------------------------------------------------------------------
# 4. Bidireccionalidad de la tabla de trazabilidad de AR.
#
# Filas: | AR-n | <lista de AC> | <verificabilidad> |
# Las AC se referencian como "<epica>.<historia> AC<x>" y pueden usar rangos
# "<epica>.<historia> AC<x>..AC<y>".
# ---------------------------------------------------------------------------
$arTable = @{}
$tableStart = -1
$tableEnd = -1
for ($i = 0; $i -lt $lines.Count; $i++) {
    if ($tableStart -lt 0 -and $lines[$i] -match '^###\s+Trazabilidad de las Additional Requirements') {
        $tableStart = $i
        continue
    }
    if ($tableStart -ge 0 -and $lines[$i] -match '^##\s+') {
        $tableEnd = $i
        break
    }
}

if ($tableStart -lt 0) {
    Add-Failure 'No se encontro la seccion "### Trazabilidad de las Additional Requirements (AR)".'
}
else {
    $checks.Add("Tabla de trazabilidad AR: linea $($tableStart + 1)")

    for ($i = $tableStart; $i -lt $lines.Count; $i++) {
        if ($tableEnd -ge 0 -and $i -ge $tableEnd) { break }
        if ($lines[$i] -notmatch '^\|\s*AR-(\d+)\s*\|') { continue }

        $arId = [int]$Matches[1]
        $cells = $lines[$i].Split('|')
        # cells[0] es vacio (precedido por '|'); las columnas utiles son 1..n-1.
        $acCell = if ($cells.Count -gt 2) { $cells[2] } else { '' }

        $listed = New-Object System.Collections.Generic.List[string]
        $rangePattern = '(\d+)\.(\d+)\s+AC(\d+)(?:\.\.AC(\d+))?'
        foreach ($m in [regex]::Matches($acCell, $rangePattern)) {
            $storyKey = "$($m.Groups[1].Value).$($m.Groups[2].Value)"
            $lo = [int]$m.Groups[3].Value
            if ($m.Groups[4].Success) {
                $hi = [int]$m.Groups[4].Value
                if ($hi -lt $lo) {
                    Add-Failure "AR-${arId}: rango de AC invertido en '$storyKey AC$lo..AC$hi'."
                    continue
                }
                for ($x = $lo; $x -le $hi; $x++) { $listed.Add("$storyKey AC$x") }
            }
            else {
                $listed.Add("$storyKey AC$lo")
            }
        }
        $arTable[$arId] = $listed
    }

    # Índice de AC del cuerpo: clave "historia ACn" -> linea del AC.
    $acIndexByKey = @{}
    foreach ($s in $stories) {
        foreach ($ac in $s.Acs) {
            $acIndexByKey["$($s.Key) AC$($ac.Index)"] = $ac.Line
        }
    }

    # Citas de AR por AC en el cuerpo del documento.
    $citations = @{}   # AR -> lista de "historia ACn"
    foreach ($s in $stories) {
        foreach ($ac in $s.Acs) {
            $bodyText = $ac.Body -join "`n"
            foreach ($m in [regex]::Matches($bodyText, '(?<![\w-])AR-(\d+)(?!\w)')) {
                $arId = [int]$m.Groups[1].Value
                if (-not $citations.ContainsKey($arId)) {
                    $citations[$arId] = New-Object System.Collections.Generic.List[string]
                }
                $citations[$arId].Add("$($s.Key) AC$($ac.Index)")
            }
        }
    }

    # 4a. Tabla -> cuerpo: cada AC listada existe y cita ese AR.
    foreach ($arId in ($arTable.Keys | Sort-Object)) {
        foreach ($key in $arTable[$arId]) {
            if (-not $acIndexByKey.ContainsKey($key)) {
                Add-Failure "AR-${arId}: la tabla de trazabilidad lista '$key', pero no existe ningun AC con ese identificador en el documento."
                continue
            }
            $storyKey = $key -replace ' AC\d+$', ''
            $acNum = [int]($key -replace '^.* AC', '')
            $s = $stories | Where-Object { $_.Key -eq $storyKey } | Select-Object -First 1
            $ac = $s.Acs | Where-Object { $_.Index -eq $acNum } | Select-Object -First 1
            $acText = $ac.Body -join "`n"
            if ($acText -notmatch "(?<![\w-])AR-$arId(?!\w)") {
                Add-Failure "AR-${arId}: la tabla de trazabilidad lista '$key' (linea $($acIndexByKey[$key])), pero ese AC no cita AR-$arId."
            }
        }
    }

    # 4b. Cuerpo -> tabla: cada AC que cita AR-n aparece listada en la fila de AR-n.
    foreach ($arId in ($citations.Keys | Sort-Object)) {
        if (-not $arTable.ContainsKey($arId)) {
            Add-Failure "AR-$arId se cita en el cuerpo del documento pero no tiene fila en la tabla de trazabilidad de AR."
            continue
        }
        foreach ($key in $citations[$arId]) {
            if ($arTable[$arId] -notcontains $key) {
                Add-Failure "AR-${arId}: el AC '$key' cita AR-$arId pero no aparece listado en la fila de AR-$arId de la tabla de trazabilidad."
            }
        }
    }

    # 4c. Filas de AR sin ninguna cita en el cuerpo.
    foreach ($arId in ($arTable.Keys | Sort-Object)) {
        if (-not $citations.ContainsKey($arId)) {
            Add-Failure "AR-$arId tiene fila en la tabla de trazabilidad pero ningun AC del documento lo cita."
        }
    }
}

# ---------------------------------------------------------------------------
# 5. Marcadores de plantilla.
# ---------------------------------------------------------------------------
foreach ($marker in $templateMarkers) {
    for ($i = 0; $i -lt $lines.Count; $i++) {
        # -match es insensible a mayusculas en PowerShell; los marcadores que
        # deben buscarse solo en mayusculas (TODO, TBD) usan -cmatch.
        $hit = if ($marker.CaseSensitive) {
            $lines[$i] -cmatch $marker.Pattern
        } else {
            $lines[$i] -match $marker.Pattern
        }
        if ($hit) {
            Add-Failure ("Marcador de plantilla {0} en linea {1}: {2}" -f $marker.Name, ($i + 1), $lines[$i].Trim())
        }
    }
}

# ---------------------------------------------------------------------------
# Resumen.
# ---------------------------------------------------------------------------
if (-not $Quiet) {
    Write-Host "Fichero analizado: $Path"
    foreach ($c in $checks) { Write-Host "  $c" }

    if ($failures.Count -eq 0) {
        Write-Host 'OK: todas las comprobaciones de epics pasan.' -ForegroundColor Green
    }
    else {
        Write-Host "Fallos: $($failures.Count)" -ForegroundColor Red
        foreach ($f in $failures) { Write-Host "  - $f" }
    }
}

if ($failures.Count -gt 0) { exit 1 }
exit 0
