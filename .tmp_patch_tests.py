import io
import re

root = r'C:\PROJECTS\lan-party-hub\tests\UnitTests'

files = [
    'InstallationOrderTests.cs',
    'InstallationRepairServiceTests.cs',
    'InstallationTrackingPublicationTests.cs',
    'InstallationPhasePublicationRaceTests.cs',
    'InstallationFailurePhasePublicationTests.cs',
]

for name in files:
    p = root + '\\' + name
    t = io.open(p, encoding='utf-8').read()

    # 1) The switcher goes right BEFORE the logger argument of the coordinator.
    old = """            stateStore,
            NullLogger<InstallationCoordinator>.Instance);"""
    new = """            stateStore,
            new InstallRootSwitcher(NullLogger<InstallRootSwitcher>.Instance),
            NullLogger<InstallationCoordinator>.Instance);"""
    if old in t:
        assert t.count(old) == 1, (name, t.count(old))
        t = t.replace(old, new)

    old = """            stateStore,
            NullLogger<InstallationCoordinator>.Instance,
            tracker);"""
    new = """            stateStore,
            new InstallRootSwitcher(NullLogger<InstallRootSwitcher>.Instance),
            NullLogger<InstallationCoordinator>.Instance,
            tracker);"""
    if old in t:
        assert t.count(old) == 1, (name, t.count(old))
        t = t.replace(old, new)

    # 2) The using of the adapter.
    if 'using LANPartyHub.Client.Infrastructure.Filesystem;' not in t:
        marker = 'using LANPartyHub.Client.Application.Ports;'
        assert marker in t, name
        t = t.replace(marker, marker + '\nusing LANPartyHub.Client.Infrastructure.Filesystem;', 1)

    io.open(p, 'w', encoding='utf-8', newline='').write(t)
    print('patched', name)
