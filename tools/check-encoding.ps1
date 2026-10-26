<#
.SYNOPSIS
    Verifica que los ficheros Markdown del repositorio no contengan caracteres
    fuera del juego esperado: codepoints CJK (cirílico/coreano/japonés/chino) ni
    el caracter de reemplazo U+FFFD.

.DESCRIPTION
    Los editores han inyectado repetidamente codepoints CJK dentro de texto en
    español y han dejado pasar revisiones que declaraban "CJK: PASS - 0" sobre
    ficheros corruptos. Este check convierte ese fallo en algo detectable.

    Solo lectura: no modifica ficheros. Devuelve 1 si encuentra offenders.

.EXAMPLE
    pwsh -File tools/check-encoding.ps1
    pwsh -File tools/check-encoding.ps1 -Path docs,_bmad-output
#>
[CmdletBinding()]
param(
    [string[]] $Path = @('docs', '_bmad-output', '_bmad', 'AGENTS.md', 'tools'),
    [switch] $Quiet
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
Set-Location $repoRoot

# Rangos de codepoints que no deben aparecer en los artefactos del proyecto.
$forbidden = @(
    @{ Name = 'CJK punctuation'; Start = 0x3000; End = 0x303F },
    @{ Name = 'Hiragana';        Start = 0x3040; End = 0x309F },
    @{ Name = 'Katakana';        Start = 0x30A0; End = 0x30FF },
    @{ Name = 'CJK ext A';       Start = 0x3400; End = 0x4DBF },
    @{ Name = 'CJK unified';     Start = 0x4E00; End = 0x9FFF },
    @{ Name = 'Hangul';          Start = 0xAC00; End = 0xD7AF },
    @{ Name = 'CJK compat';      Start = 0xF900; End = 0xFAFF },
    @{ Name = 'Fullwidth forms'; Start = 0xFF00; End = 0xFFEF }
)

function Test-Forbidden {
    param([int] $CodePoint)

    if ($CodePoint -eq 0xFFFD) { return 'U+FFFD replacement char' }
    foreach ($range in $forbidden) {
        if ($CodePoint -ge $range.Start -and $CodePoint -le $range.End) {
            return $range.Name
        }
    }
    return $null
}

$files = @()
foreach ($target in $Path) {
    if (-not (Test-Path -LiteralPath $target)) { continue }
    $item = Get-Item -LiteralPath $target
    if ($item.PSIsContainer) {
        $files += Get-ChildItem -LiteralPath $target -Recurse -File -Filter '*.md'
    }
    else {
        $files += $item
    }
}
$files = $files | Sort-Object FullName -Unique

$offenders = @()
foreach ($file in $files) {
    $bytes = [System.IO.File]::ReadAllBytes($file.FullName)
    if ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF) {
        $offenders += [pscustomobject]@{ File = $file.FullName; Line = 0; Detail = 'UTF-8 BOM' }
        continue
    }
    $text = [System.Text.Encoding]::UTF8.GetString($bytes)
    $lines = $text -split "`n"
    for ($i = 0; $i -lt $lines.Count; $i++) {
        foreach ($ch in $lines[$i].ToCharArray()) {
            $detail = Test-Forbidden -CodePoint ([int][char]$ch)
            if ($detail) {
                $offenders += [pscustomobject]@{
                    File   = $file.FullName
                    Line   = $i + 1
                    Detail = "$detail (U+{0:X4})" -f [int][char]$ch
                }
            }
        }
    }
}

if (-not $Quiet) {
    Write-Host "Ficheros Markdown analizados: $($files.Count)"
    if ($offenders.Count -eq 0) {
        Write-Host 'OK: sin codepoints CJK ni U+FFFD.' -ForegroundColor Green
    }
    else {
        Write-Host "Offenders: $($offenders.Count)" -ForegroundColor Red
        foreach ($o in $offenders) {
            $relative = Resolve-Path -Relative $o.File
            Write-Host ("  {0}:{1}  {2}" -f $relative, $o.Line, $o.Detail)
        }
    }
}

if ($offenders.Count -gt 0) { exit 1 }
exit 0
