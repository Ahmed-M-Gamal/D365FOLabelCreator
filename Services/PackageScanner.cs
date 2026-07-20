using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Linq;
using D365LabelCreator.Models;

namespace D365LabelCreator.Services;

/// <summary>Locates PackagesLocalDirectory and enumerates customizable models.</summary>
public static class PackageScanner
{
    /// <summary>Folders under a model that are build output / resources, not metadata.</summary>
    public static readonly HashSet<string> IgnoredModelFolders =
        new(StringComparer.OrdinalIgnoreCase) { "bin", "Reports", "Resources", "XppMetadata" };

    /// <summary>
    /// Attempts to locate PackagesLocalDirectory automatically. Checks a couple of well-known
    /// locations, then scans drive roots for &lt;drive&gt;:\AOSService\PackagesLocalDirectory.
    /// Returns null if nothing is found (the user can then Browse).
    /// </summary>
    public static string? AutoDetect()
    {
        var candidates = new List<string>
        {
            @"C:\AOSService\PackagesLocalDirectory",
            @"K:\AosService\PackagesLocalDirectory",
            @"J:\AosService\PackagesLocalDirectory",
            @"I:\AosService\PackagesLocalDirectory",
        };

        foreach (var c in candidates)
            if (Directory.Exists(c))
                return c;

        try
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                if (!drive.IsReady)
                    continue;
                var p = Path.Combine(drive.RootDirectory.FullName, "AOSService", "PackagesLocalDirectory");
                if (Directory.Exists(p))
                    return p;
            }
        }
        catch
        {
            // Ignore drive enumeration failures.
        }

        return null;
    }

    /// <summary>Basic sanity check that a folder looks like a PackagesLocalDirectory.</summary>
    public static bool LooksValid(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            return false;
        // A PackagesLocalDirectory contains package folders, each with a Descriptor subfolder.
        try
        {
            foreach (var dir in Directory.EnumerateDirectories(path))
                if (Directory.Exists(Path.Combine(dir, "Descriptor")))
                    return true;
        }
        catch
        {
            return false;
        }
        return false;
    }

    /// <summary>
    /// Returns all models whose descriptor has &lt;Customization&gt;Allow&lt;/Customization&gt;.
    /// A package folder can hold several models (one descriptor + one sibling folder each).
    /// </summary>
    public static List<ModelInfo> GetCustomizableModels(string packagesLocalDirectory)
    {
        var models = new List<ModelInfo>();
        if (!Directory.Exists(packagesLocalDirectory))
            return models;

        foreach (var packageDir in Directory.EnumerateDirectories(packagesLocalDirectory))
        {
            var descriptorDir = Path.Combine(packageDir, "Descriptor");
            if (!Directory.Exists(descriptorDir))
                continue;

            foreach (var descriptorPath in Directory.EnumerateFiles(descriptorDir, "*.xml"))
            {
                ModelInfo? model = TryReadModel(packageDir, descriptorPath);
                if (model != null)
                    models.Add(model);
            }
        }

        models.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        return models;
    }

    private static ModelInfo? TryReadModel(string packageDir, string descriptorPath)
    {
        try
        {
            var doc = XDocument.Load(descriptorPath);
            var root = doc.Root;
            if (root == null)
                return null;

            string customization = (string?)root.Element("Customization") ?? string.Empty;
            if (!string.Equals(customization.Trim(), "Allow", StringComparison.OrdinalIgnoreCase))
                return null;

            string name = (string?)root.Element("Name")
                          ?? Path.GetFileNameWithoutExtension(descriptorPath);
            string displayName = (string?)root.Element("DisplayName") ?? name;

            string modelDir = Path.Combine(packageDir, name);
            if (!Directory.Exists(modelDir))
                return null; // descriptor without a matching model folder — skip

            return new ModelInfo
            {
                Name = name,
                DisplayName = displayName,
                PackageDir = packageDir,
                ModelDir = modelDir,
                DescriptorPath = descriptorPath,
            };
        }
        catch
        {
            return null; // unreadable/invalid descriptor — skip
        }
    }
}
