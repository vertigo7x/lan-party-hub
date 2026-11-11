import io

p = r'C:\PROJECTS\lan-party-hub\src\Client\Host\InstallationComposition.cs'
t = io.open(p, encoding='utf-8').read()

pairs = [
    ('if (!TryResolve(createDirectory.Path, out var directoryPath, out var directoryRejection))',
     'if (!TryResolve(rootPath, createDirectory.Path, out var directoryPath, out var directoryRejection))'),
    ('if (!TryResolve(writeConfig.Path, out var configPath, out var configRejection))',
     'if (!TryResolve(rootPath, writeConfig.Path, out var configPath, out var configRejection))'),
    ('if (!TryResolve(verify.Path, out var verifiedPath, out var verifyRejection))',
     'if (!TryResolve(rootPath, verify.Path, out var verifiedPath, out var verifyRejection))'),
    ('if (!TryResolve(ExtractDestination(operation), out _, out var copyRejection))',
     'if (!TryResolve(rootPath, ExtractDestination(operation), out _, out var copyRejection))'),
]

for a, b in pairs:
    assert t.count(a) == 1, (a, t.count(a))
    t = t.replace(a, b)

old = """    private bool TryResolve(string relativePath, out string resolved, out string? rejection)
    {
        resolved = string.Empty;
        rejection = null;

        try
        {
            resolved = _paths.ResolveInsideRoot(relativePath);

            return true;
        }"""

new = """    private bool TryResolve(
        string rootPath,
        string relativePath,
        out string resolved,
        out string? rejection)
    {
        resolved = string.Empty;
        rejection = null;

        try
        {
            // AD-9: el confine se comprueba contra la raíz que esta llamada está
            // ESCRIBIENDO, que durante una actualización es la raíz de PREPARACIÓN y no
            // la que hay en el disco (AD-9, AC4, FR-11).
            resolved = _paths.ResolveInside(rootPath, relativePath);

            return true;
        }"""

assert t.count(old) == 1
t = t.replace(old, new)

io.open(p, 'w', encoding='utf-8', newline='').write(t)
print('ok')
