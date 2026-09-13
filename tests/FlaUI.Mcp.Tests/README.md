# Generic no-focus tests

Run `dotnet test tests/FlaUI.Mcp.Tests/FlaUI.Mcp.Tests.csproj -c Foundation`.

Tests use provider doubles, temporary files/images and service construction, not
desktop input. This project no longer references the TE3 runner; its tests live
in tests/FlaUI.Automation.Tests.

IntegrationTests launches desktop apps, including legacy Paint DPI tests. Build
it freely; execute only after a desktop handoff. Unit passes do not establish
live selector correctness or screenshot fidelity.
