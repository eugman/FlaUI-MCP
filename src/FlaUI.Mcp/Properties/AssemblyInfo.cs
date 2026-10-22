using System.Runtime.CompilerServices;

// The offline test assemblies install a fake InputDesktop, which is internal.
[assembly: InternalsVisibleTo("FlaUI.Mcp.Tests")]
[assembly: InternalsVisibleTo("FlaUI.Mcp.PowerBi.Tests")]
[assembly: InternalsVisibleTo("FlaUI.Mcp.Camtasia.Tests")]
[assembly: InternalsVisibleTo("FlaUI.Mcp.Te3.Tests")]

// The desktop tests reach the raw Win32 mutators to arrange windows; product code goes through the guards.
[assembly: InternalsVisibleTo("FlaUI.Mcp.IntegrationTests")]
