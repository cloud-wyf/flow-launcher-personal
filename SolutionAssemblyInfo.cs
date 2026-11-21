using System.Reflection;
using System.Runtime.InteropServices;

#if DEBUG

[assembly: AssemblyConfiguration("Debug")]
[assembly: AssemblyDescription("Debug build, https://github.com/Flow-Launcher/Flow.Launcher")]
#else
[assembly: AssemblyConfiguration("Release")]
[assembly: AssemblyDescription("Release build, https://github.com/Flow-Launcher/Flow.Launcher")]
#endif

[assembly: AssemblyTitle("Flow Launcher")]
[assembly: AssemblyCompany("Flow Launcher")]
[assembly: AssemblyProduct("Flow Launcher")]
[assembly: AssemblyCopyright("The MIT License (MIT)")]
[assembly: AssemblyTrademark("")]
[assembly: AssemblyCulture("")]
[assembly: ComVisible(false)]
[assembly: AssemblyVersion("2.1.4")]
[assembly: AssemblyFileVersion("2.1.4.1")]
[assembly: AssemblyInformationalVersion("2.1.4.1")]
