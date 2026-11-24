using System.Diagnostics;
using System.IO.Compression;
using Robust.Packaging;
using Robust.Packaging.AssetProcessing;
using Robust.Packaging.AssetProcessing.Passes;
using Robust.Packaging.Utility;
using Robust.Shared.Timing;

namespace Content.Packaging;

public static class ClientPackaging
{
    /// <summary>
    /// Be advised this can be called from server packaging during a HybridACZ build.
    /// </summary>
    public static async Task PackageClient(bool skipBuild, string configuration, IPackageLogger logger)
    {
        logger.Info("Building client...");

        if (!skipBuild)
        {
            await ProcessHelpers.RunCheck(new ProcessStartInfo
            {
                FileName = "dotnet",
                ArgumentList =
                {
                    "build",
                    Path.Combine("Content.Client", "Content.Client.csproj"),
                    "-c", configuration,
                    "--nologo",
                    "/v:m",
                    "/t:Rebuild",
                    "/p:FullRelease=true",
                    "/m"
                }
            });
        }

        logger.Info("Packaging client...");

        var sw = RStopwatch.StartNew();
        {
            await using var zipFile =
                File.Open(Path.Combine("release", "SS14.Client.zip"), FileMode.Create, FileAccess.ReadWrite);
            using var zip = new ZipArchive(zipFile, ZipArchiveMode.Update);
            var writer = new AssetPassZipWriter(zip);

            await WriteResources("", writer, logger, default);
            await writer.FinishedTask;
        }

        logger.Info($"Finished packaging client in {sw.Elapsed}");
    }

    public static async Task WriteResources(
        string contentDir,
        AssetPass pass,
        IPackageLogger logger,
        CancellationToken cancel)
    {
        var graph = new RobustClientAssetGraph();
        pass.Dependencies.Add(new AssetPassDependency(graph.Output.Name));

        var dropSvgPass = new AssetPassFilterDrop(f => f.Path.EndsWith(".svg"))
        {
            Name = "DropSvgPass",
        };
        dropSvgPass.AddDependency(graph.Input).AddBefore(graph.PresetPasses);

        AssetGraph.CalculateGraph([pass, dropSvgPass, ..graph.AllPasses], logger);

        var inputPass = graph.Input;

        await RobustSharedPackaging.WriteContentAssemblies(
            inputPass,
            contentDir,
            "Content.Client",
            new[] { "Content.Client", "Content.Shared", "Content.Shared.Database" },
            cancel: cancel);

        await RobustClientPackaging.WriteClientResources(contentDir, inputPass, cancel);

        // The default client package excludes Maps. Include the vessel layouts used by the preview.
        var resources = Path.Combine(contentDir, "Resources");
        var prototypes = Path.Combine(resources, "Prototypes");
        var includedMaps = new HashSet<string>(StringComparer.Ordinal);
        foreach (var prototype in Directory.EnumerateFiles(prototypes, "*.yml", SearchOption.AllDirectories))
        {
            cancel.ThrowIfCancellationRequested();
            foreach (var line in File.ReadLines(prototype))
            {
                var field = line.TrimStart();
                if (!field.StartsWith("shuttlePath:", StringComparison.Ordinal))
                    continue;

                var path = field["shuttlePath:".Length..].Split('#', 2)[0].Trim().Trim('"', '\'');
                if (!path.StartsWith("/Maps/_NF/Shuttles/", StringComparison.Ordinal) ||
                    !path.EndsWith(".yml", StringComparison.Ordinal) ||
                    path.Contains("..", StringComparison.Ordinal) || path.Contains('\\'))
                    continue;

                var relativePath = path[1..];
                var sourcePath = Path.Combine(resources, relativePath);
                if (File.Exists(sourcePath) && includedMaps.Add(relativePath))
                    inputPass.InjectFileFromDisk(relativePath, sourcePath);
            }
        }

        inputPass.InjectFinished();
    }
}
