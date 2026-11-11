import io

p = r'C:\PROJECTS\lan-party-hub\src\Client\Application\Install\InstallOperationExecutor.cs'
t = io.open(p, encoding='utf-8').read()

pairs = [
    (""".ExtractAsync(packagePath, extract.ArchivePath, extract.DestinationPath, destinationIsDirectory: true, cancellationToken)""",
     """.ExtractAsync(rootPath, packagePath, extract.ArchivePath, extract.DestinationPath, destinationIsDirectory: true, cancellationToken)"""),
    (""".ExtractAsync(packagePath, copyFile.SourcePath, copyFile.DestinationPath, destinationIsDirectory: false, cancellationToken)""",
     """.ExtractAsync(rootPath, packagePath, copyFile.SourcePath, copyFile.DestinationPath, destinationIsDirectory: false, cancellationToken)"""),
]

for a, b in pairs:
    assert t.count(a) == 1, (a, t.count(a))
    t = t.replace(a, b)

io.open(p, 'w', encoding='utf-8', newline='').write(t)
print('ok')
