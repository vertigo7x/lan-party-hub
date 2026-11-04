# Build Lite paralelo

- Ventana deslizante de hasta cuatro implementaciones independientes.
- Cada worker usa una rama y un worktree efímero bajo `.build-lite/worktrees/`.
- El plan genera `reads`, `writes`, `shared_surfaces` y `depends_on`; sin prueba estructurada de independencia, la historia se ejecuta en exclusiva.
- Contratos compartidos, persistencia, red, instalación y seguridad son exclusivos.
- Planificación puede correr en paralelo; rebase, verificación final, PR y squash merge se serializan contra `main`.
- Tras cada merge, las ramas pendientes se rebasa y revalidan antes de fusionarse.
