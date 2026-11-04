# Modelo de ejecución

## Elegibilidad

El plan de cada historia debe incluir un bloque JSON con estas claves:

```json
{
  "reads": ["..."],
  "writes": ["..."],
  "shared_surfaces": ["..."],
  "depends_on": ["N.M"]
}
```

Una historia es exclusiva cuando el bloque falta o es inválido, declara una superficie sensible, tiene dependencia pendiente o intersecta las escrituras/superficies de un worker activo.

## Ciclo

1. Planificar historias candidatas y validar sus metadatos.
2. Mantener hasta cuatro workers elegibles, cada uno en su worktree y rama.
3. Cada worker conserva el presupuesto y las verificaciones actuales.
4. Un coordinador consume historias terminadas una a una: refresca `origin/main`, rebasa, vuelve a verificar, abre PR y fusiona.
5. Ante fallo, conserva worktree, rama y logs para recuperación; no bloquea workers independientes ya activos.

## Invariantes

- Un solo coordinador modifica `sprint-status.yaml`, crea PRs y fusiona.
- Los workers no ejecutan el merge ni comparten el worktree canónico.
- La limpieza de worktrees solo sucede después de merge confirmado; los fallidos permanecen para inspección.
