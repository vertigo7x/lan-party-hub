<#
.SYNOPSIS
    Verifica que la personalización de bmad-build-auto llega al workflow renderizado.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
Set-Location $repoRoot

$output = & uv run --no-cache _bmad/scripts/render_skill.py `
    --project-root $repoRoot `
    --skill (Join-Path $repoRoot '.agents/skills/bmad-build-auto') 2>&1
if ($LASTEXITCODE -ne 0) {
    throw "No se pudo renderizar bmad-build-auto: $output"
}

$match = [regex]::Match(($output -join "`n"), 'read and follow\s+(.+workflow\.md)')
if (-not $match.Success -or -not (Test-Path -LiteralPath $match.Groups[1].Value.Trim())) {
    throw 'El renderizado no devolvio una ruta valida a workflow.md.'
}

$workflow = Get-Content -LiteralPath $match.Groups[1].Value.Trim() -Raw -Encoding utf8
$required = @(
    'La puerta de VCS protege trabajo ajeno',
    'bmad-loop/<run_id>/<story_key>',
    '_bmad-output/implementation-artifacts/epic-<N>-context.md',
    'spec-<story_key>(-<numero>)?.md',
    'permite solamente las rutas de artefactos propios',
    'Publica en el chat el hash del commit'
)
foreach ($text in $required) {
    if (-not $workflow.Contains($text)) {
        throw "El workflow renderizado no contiene la regla requerida: $text"
    }
}

Write-Host 'OK: la personalizacion de bmad-build-auto se conserva en el workflow renderizado.' -ForegroundColor Green
