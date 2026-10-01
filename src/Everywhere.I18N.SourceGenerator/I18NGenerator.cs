using System.Collections.Immutable;
using System.Security;
using System.Text;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace Everywhere.I18N.SourceGenerator;

[Generator]
public sealed class I18NSourceGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Register the additional file provider for RESX files
        var resxFiles = context.AdditionalTextsProvider
            .Where(file =>
            {
                var fileName = Path.GetFileName(file.Path);
                return fileName.Equals("Strings.resx", StringComparison.OrdinalIgnoreCase);
            })
            .Collect();

        var options = context.AnalyzerConfigOptionsProvider.Select(static (provider, _) => BuildOptions(provider));

        context.RegisterSourceOutput(
            resxFiles.Combine(options),
            static (context, tuple) =>
                GenerateI18NCode(context, tuple.Left, tuple.Right));
    }

    private static GeneratorOptions BuildOptions(AnalyzerConfigOptionsProvider provider)
    {
        var globalOptions = provider.GlobalOptions;
        globalOptions.TryGetValue("build_property.EverywhereI18NNamespace", out var configuredNamespace);
        globalOptions.TryGetValue("build_property.AssemblyName", out var assemblyName);
        globalOptions.TryGetValue("build_property.RootNamespace", out var rootNamespace);

        var baseNamespace =
            !string.IsNullOrWhiteSpace(configuredNamespace) ? configuredNamespace! :
            !string.IsNullOrWhiteSpace(assemblyName) ? assemblyName! + ".I18N" :
            !string.IsNullOrWhiteSpace(rootNamespace) ? rootNamespace! + ".I18N" :
            "Everywhere.I18N.Generated";

        return new GeneratorOptions(baseNamespace);
    }

    private static void GenerateI18NCode(SourceProductionContext context, ImmutableArray<AdditionalText> resxFiles, GeneratorOptions options)
    {
        if (resxFiles.Length == 0)
        {
            return;
        }

        try
        {
            // Only neutral resources define the strongly typed key API.
            var defaultResxFile = resxFiles.FirstOrDefault(f => Path.GetFileName(f.Path).Equals("Strings.resx", StringComparison.OrdinalIgnoreCase));
            if (defaultResxFile == null)
            {
                context.ReportDiagnostic(
                    Diagnostic.Create(
                        new DiagnosticDescriptor(
                            "I18N001",
                            "Missing Default RESX File",
                            "Could not find the default Strings.resx file",
                            "I18N",
                            DiagnosticSeverity.Error,
                            isEnabledByDefault: true),
                        Location.None));
                return;
            }

            // Parse the default RESX to get all keys
            var defaultContent = defaultResxFile.GetText(context.CancellationToken)?.ToString();
            if (defaultContent is not { Length: > 0 })
            {
                return;
            }

            // Parse default RESX for keys and values
            var defaultEntries = ParseResxEntries(defaultContent);
            if (defaultEntries.Count == 0)
            {
                return;
            }

            context.AddSource(
                "LocaleKey.g.cs",
                SourceText.From(GenerateLocaleKeyClass(options.Namespace, defaultResxFile.Path, defaultEntries), Encoding.UTF8));
        }
        catch (Exception ex)
        {
            context.ReportDiagnostic(
                Diagnostic.Create(
                    new DiagnosticDescriptor(
                        "I18N002",
                        "I18N Generation Error",
                        $"Error generating I18N code: {ex.Message}",
                        "I18N",
                        DiagnosticSeverity.Error,
                        isEnabledByDefault: true),
                    Location.None));
        }
    }

    private static Dictionary<string, string> ParseResxEntries(string resxContent)
    {
        var entries = new Dictionary<string, string>();

        try
        {
            var doc = XDocument.Parse(resxContent);
            var dataNodes = doc.Root?.Elements("data");

            if (dataNodes == null) return entries;

            foreach (var dataNode in dataNodes)
            {
                var nameAttr = dataNode.Attribute("name");
                var valueNode = dataNode.Element("value");

                if (nameAttr != null && valueNode != null)
                {
                    entries[nameAttr.Value] = valueNode.Value;
                }
            }
        }
        catch
        {
            // Silently fail and return an empty dictionary
            return new Dictionary<string, string>();
        }

        return entries;
    }

    private static string GenerateLocaleKeyClass(string ns, string resxPath, Dictionary<string, string> entries)
    {
        var sb = new StringBuilder();

        sb.AppendLine(
            $$"""
              // Generated by Everywhere.I18N.SourceGenerator, do not edit manually
              // Edit {{Path.GetFileName(resxPath)}} instead, run the generator or build project to update this file

              #nullable enable

              namespace {{ns}};

              /// <summary>
              ///     Provides strongly-typed keys for localized strings.
              /// </summary>
              public static partial class LocaleKey
              {
                  /// <summary>
                  /// An empty string constant for special use.
                  /// </summary>
                  public const string Empty = "";
              """);

        foreach (var entry in entries)
        {
            AppendSummary(sb, entry.Value);
            var escapedKey = EscapeVariableName(entry.Key);
            sb.AppendLine($"    public const string {escapedKey} = {ToCSharpString(entry.Key)};");
        }

        sb.AppendLine("}");

        return sb.ToString();
    }

    private static void AppendSummary(StringBuilder sb, string value)
    {
        var escapedSummary = SecurityElement.Escape(value);
        if (escapedSummary is not null && escapedSummary.Contains('\n'))
        {
            sb.AppendLine("    /// <summary>");
            foreach (var summaryLine in escapedSummary.Split('\n'))
            {
                sb.AppendLine($"    /// {summaryLine}");
            }
            sb.AppendLine("    /// </summary>");
        }
        else
        {
            sb.AppendLine($"    /// <summary>{escapedSummary}</summary>");
        }
    }

    private static string EscapeVariableName(string s)
    {
        var escaped = new string(s.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray());
        if (escaped.Length > 0 && char.IsDigit(escaped[0]))
        {
            escaped = "_" + escaped;
        }
        return escaped;
    }

    private static string ToCSharpString(string value)
    {
        return "\"" + value
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\r\n", "\\n")
            .Replace("\r", "\\n")
            .Replace("\n", "\\n") + "\"";
    }

    private sealed class GeneratorOptions
    {
        public GeneratorOptions(string ns)
        {
            Namespace = ns;
        }

        public string Namespace { get; }
    }
}
