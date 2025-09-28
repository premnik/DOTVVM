using Converters;
using System.Text;
using System.Text.RegularExpressions;

namespace ASP_dotnet_MigrationTool;

public static class Program
{
    public static int Main(string[] args)
    {
        var (input, output, ns) = ParseArgs(args);
        if (input is null || output is null)
        {
            Console.WriteLine("Usage: --in <LegacyRoot> --out <OutputRoot> [--ns <Namespace>]");
            return 2;
        }
        ns ??= "SampleApp.ViewModels";

        MigrateFolder(input, output, ns);
        Console.WriteLine($"Done. Open: {Path.Combine(output, "MigratedSolution.sln")}");
        return 0;
    }

    public static void MigrateFolder(string input, string output, string ns)
    {
        Directory.CreateDirectory(output);
        var viewsRoot = Path.Combine(output, "Views");
        var vmsRoot = Path.Combine(output, "ViewModels");
        Directory.CreateDirectory(viewsRoot);
        Directory.CreateDirectory(vmsRoot);

        var files = Directory.EnumerateFiles(input, "*.aspx", SearchOption.AllDirectories).ToList();
        foreach (var aspx in files)
        {
            var name = Path.GetFileNameWithoutExtension(aspx);
            var vmClass = MakeValidIdentifier(name) + "ViewModel";
            var viewOut = Path.Combine(viewsRoot, name + ".dothtml");
            var vmOut = Path.Combine(vmsRoot, vmClass + ".cs");

            var aspxContent = File.ReadAllText(aspx);
            var conv = DotvvmGenerator.Generate(aspxContent, ns, vmClass);
            File.WriteAllText(viewOut, conv.View);
            File.WriteAllText(vmOut, conv.ViewModel);
        }

        var webAppPath = Path.Combine(output, "MigratedDotvvmApp");
        EnsureWebProject(webAppPath);
        CopyIntoWebApp(viewsRoot, vmsRoot, webAppPath);
        GenerateRoutes(webAppPath);
        CreateOutputSolution(output);
    }

    private static (string? input, string? output, string? ns) ParseArgs(string[] args)
    {
        string? input = null, output = null, ns = null;
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--in": input = args[++i]; break;
                case "--out": output = args[++i]; break;
                case "--ns": ns = args[++i]; break;
            }
        }
        return (input, output, ns);
    }

    private static string MakeValidIdentifier(string name)
    {
        var s = Regex.Replace(name, @"[^A-Za-z0-9_]", "_");
        if (string.IsNullOrEmpty(s) || char.IsDigit(s[0])) s = "_" + s;
        return s;
    }

    public static void EnsureWebProject(string webAppPath)
    {
        Directory.CreateDirectory(webAppPath);
        Directory.CreateDirectory(Path.Combine(webAppPath, "Views"));
        Directory.CreateDirectory(Path.Combine(webAppPath, "ViewModels"));

        var csproj = Path.Combine(webAppPath, "MigratedDotvvmApp.csproj");
        if (!File.Exists(csproj))
        {
            File.WriteAllText(csproj, @"<Project Sdk=""Microsoft.NET.Sdk.Web"">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include=""DotVVM.AspNetCore"" Version=""4.*"" />
    <PackageReference Include=""DotVVM.Core"" Version=""4.*"" />
  </ItemGroup>
</Project>");
        }

        var program = Path.Combine(webAppPath, "Program.cs");
        if (!File.Exists(program))
        {
            File.WriteAllText(program,
@"using DotVVM.Framework.Hosting;
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddDotVVM<DotvvmStartup>();
var app = builder.Build();
app.UseDotVVM<DotvvmStartup>();
app.Run();");
        }

        var dotvvmJson = Path.Combine(webAppPath, "dotvvm.json");
        if (!File.Exists(dotvvmJson))
        {
            File.WriteAllText(dotvvmJson, @"{ ""frameworkVersion"": ""4.2.0"", ""resources"": { ""default"": { ""styles"": [], ""scripts"": [] } } }");
        }
    }

    public static void CopyIntoWebApp(string viewsRoot, string vmsRoot, string webAppPath)
    {
        foreach (var file in Directory.EnumerateFiles(viewsRoot, "*.dothtml"))
            File.Copy(file, Path.Combine(webAppPath, "Views", Path.GetFileName(file)), true);
        foreach (var file in Directory.EnumerateFiles(vmsRoot, "*.cs"))
            File.Copy(file, Path.Combine(webAppPath, "ViewModels", Path.GetFileName(file)), true);
    }

    public static void GenerateRoutes(string webAppPath)
    {
        var pages = Directory.EnumerateFiles(Path.Combine(webAppPath, "Views"), "*.dothtml").OrderBy(p => p).ToList();
        var sb = new StringBuilder();
        sb.AppendLine("using DotVVM.Framework.Configuration;");
        sb.AppendLine();
        sb.AppendLine("public class DotvvmStartup : IDotvvmStartup");
        sb.AppendLine("{");
        sb.AppendLine("    public void Configure(DotvvmConfiguration config, string applicationPath)");
        sb.AppendLine("    {");
        sb.AppendLine("        config.AddDefaultTempStorages(\"temp\");");
        foreach (var p in pages)
        {
            var name = Path.GetFileNameWithoutExtension(p);
            var url = name.Equals("Default", StringComparison.OrdinalIgnoreCase) ? "" : name.ToLowerInvariant();
            sb.AppendLine($"        config.RouteTable.Add(\"{name}\", \"{url}\", \"Views/{Path.GetFileName(p)}\");");
        }
        sb.AppendLine("    }");
        sb.AppendLine("}");
        File.WriteAllText(Path.Combine(webAppPath, "DotvvmStartup.cs"), sb.ToString());
    }

    public static void CreateOutputSolution(string output)
    {
        var sln = $@"Microsoft Visual Studio Solution File, Format Version 12.00
# Visual Studio Version 17
VisualStudioVersion = 17.14.36414.22
MinimumVisualStudioVersion = 10.0.40219.1
Project(""{{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}}"") = ""MigratedDotvvmApp"", ""MigratedDotvvmApp\\MigratedDotvvmApp.csproj"", ""{{BEECA7E1-53F6-4F2F-9E2D-40A810ACD2F7}}""
EndProject
Global
    GlobalSection(SolutionConfigurationPlatforms) = preSolution
        Debug|Any CPU = Debug|Any CPU
        Release|Any CPU = Release|Any CPU
    EndGlobalSection
    GlobalSection(ProjectConfigurationPlatforms) = postSolution
        {{BEECA7E1-53F6-4F2F-9E2D-40A810ACD2F7}}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
        {{BEECA7E1-53F6-4F2F-9E2D-40A810ACD2F7}}.Debug|Any CPU.Build.0 = Debug|Any CPU
        {{BEECA7E1-53F6-4F2F-9E2D-40A810ACD2F7}}.Release|Any CPU.ActiveCfg = Release|Any CPU
        {{BEECA7E1-53F6-4F2F-9E2D-40A810ACD2F7}}.Release|Any CPU.Build.0 = Release|Any CPU
    EndGlobalSection
EndGlobal";
        File.WriteAllText(Path.Combine(output, "MigratedSolution.sln"), sln);
    }
}