import io

p = r'C:\PROJECTS\lan-party-hub\src\Client\Application\Install\InstallationCoordinator.cs'
t = io.open(p, encoding='utf-8').read()

# ---------------------------------------------------------------- 1. Wrap the
# apply + commit in a try that DISCARDS the staging root when the operation is
# CANCELLED, and commit the swap only when the new tree CERTIFIES.

old = """        InstallTreeOutcome tree;

        try
        {
            try
            {
                tree = await _tree
                    .ApplyAsync(manifest, writeRoot, packagePath, sink, cancellationToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                // Esperar la fase antes del resultado terminal, incluso si falla el árbol.
                await sink.FlushAsync().ConfigureAwait(false);
            }
        }
        catch (PrivilegedOperationDeclinedException exception)"""

new = """        InstallTreeOutcome tree;

        try
        {
            try
            {
                tree = await _tree
                    .ApplyAsync(manifest, writeRoot, packagePath, sink, cancellationToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                // Esperar la fase antes del resultado terminal, incluso si falla el árbol.
                await sink.FlushAsync().ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // AC4, FR-11: una CANCELACIÓN deja la instalación anterior intacta: la raíz
            // de PREPARACIÓN se descarta y la activa conserva su contenido.
            DiscardStagingRoot(rootPath, writeRoot);

            throw;
        }
        catch (PrivilegedOperationDeclinedException exception)"""

assert t.count(old) == 1
t = t.replace(old, new)

# ---------------------------------------------------------------- 2. The
# certification of the PREPARED tree decides if the swap happens. A tree that does
# NOT certify is DISCARDED and the previous installation keeps its row.

old = """        var verdict = InstallationCertification.Certify(
            packageValidatedAgainstAnchor: true,
            manifestVerifyPassed: tree.VerifyPassed,
            pathsConfinedToRoot: tree.PathsConfinedToRoot,
            repairablePackageAvailable: true);

        machine.TryTransitionTo(InstallationState.ValidatingInstallation);
        machine.TryTransitionTo(verdict.State);
"""

new = """        var verdict = InstallationCertification.Certify(
            packageValidatedAgainstAnchor: true,
            manifestVerifyPassed: tree.VerifyPassed,
            pathsConfinedToRoot: tree.PathsConfinedToRoot,
            repairablePackageAvailable: true);

        machine.TryTransitionTo(InstallationState.ValidatingInstallation);
        machine.TryTransitionTo(verdict.State);

        // AC4, FR-11: la SUSTITUCIÓN sólo ocurre con un árbol NUEVO que CERTIFICA. Un
        // árbol que no verifica o que escribió fuera de la raíz NO se publica: se
        // descarta y la instalación anterior conserva su contenido y su fila, así que
        // el juego sigue siendo jugable con la versión que ya estaba (AC4, FR-11).
        if (mode == InstallationMode.Update && !verdict.IsInstalled)
        {
            DiscardStagingRoot(rootPath, writeRoot);

            _logger.LogWarning(
                "La actualización del juego {GameId} a la versión {Version} NO certifica el árbol nuevo: {Reason} La instalación anterior conserva su versión y su validez.",
                gameId,
                installedVersion,
                verdict.ReasonText);

            await PublishAsync(
                InstallPhase.Failed,
                sink.LastBytesWritten,
                sink.LastTotalBytes,
                trackedOperation,
                cancellationToken).ConfigureAwait(false);

            return await RejectAsync(
                ErrorCatalog.InstallOperationFailedCode,
                $"La actualización del juego '{gameId}' a la versión '{installedVersion}' no se certificó: {verdict.ReasonText} La instalación anterior conserva su versión y su validez.",
                packageId,
                trackedOperation,
                cancellationToken).ConfigureAwait(false);
        }

        // AC1, FR-11: el árbol nuevo está CERTIFICADO, así que SUSTITUYE al anterior.
        // El cambio de nombre es la operación que no deja la instalación a medias, así
        // que a partir de aquí la versión nueva es la vigente (AD-9, AC4).
        if (mode == InstallationMode.Update)
        {
            _switcher.CommitStagingRoot(rootPath, writeRoot);
        }
"""

assert t.count(old) == 1
t = t.replace(old, new)

# ---------------------------------------------------------------- 3. The helper
# that discards the staging root only when there IS one.

old = """    /// <summary>
    /// Persists the FAILURE of an installation, so the error queda registrado y la
    /// operación es REINTENTABLE sin reinstalar el cliente (AD-8, AD-11, AC5)."""

new = """    /// <summary>
    /// DISCARDS the root of PREPARATION, leaving the active one untouched (AD-9, AC4,
    /// FR-11).
    ///
    /// <para>
    /// El descarte sólo existe en el modo <see cref="InstallationMode.Update"/>: en el
    /// flujo inicial no hay raíz de preparación, y el árbol se escribe directamente en la
    /// raíz activa (AC1, AC4).
    /// </para>
    /// </summary>
    /// <param name="rootPath">Canonical absolute path of the active installation root.</param>
    /// <param name="writeRoot">
    /// Canonical absolute path of the root the tree was written into.
    /// </param>
    private void DiscardStagingRoot(string rootPath, string writeRoot)
    {
        if (string.Equals(rootPath, writeRoot, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        try
        {
            _switcher.DiscardStagingRoot(rootPath, writeRoot);
        }
        catch (Exception exception)
        {
            // El descarte NUNCA puede impedir que la operación siga: lo que importa es
            // que la raíz activa NO se toca (AC4, AD-1).
            _logger.LogWarning(
                exception,
                "No se pudo descartar la raíz de preparación '{StagingRoot}': la raíz activa '{RootPath}' conserva su contenido.",
                writeRoot,
                rootPath);
        }
    }

    /// <summary>
    /// Persists the FAILURE of an installation, so the error queda registrado y la
    /// operación es REINTENTABLE sin reinstalar el cliente (AD-8, AD-11, AC5)."""

assert t.count(old) == 1
t = t.replace(old, new)

io.open(p, 'w', encoding='utf-8', newline='').write(t)
print('ok')
