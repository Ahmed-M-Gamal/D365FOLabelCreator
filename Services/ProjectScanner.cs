using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using D365LabelCreator.Models;

namespace D365LabelCreator.Services;

/// <summary>Discovers VS solutions (.sln) and their D365 projects (.rnrproj) under a directory.</summary>
public static class ProjectScanner
{
    // Project("{type-guid}") = "Display name", "Rel\Path.rnrproj", "{project-guid}"
    private static readonly Regex ProjectLine = new(
        @"^Project\(""\{[^}]+\}""\)\s*=\s*""[^""]*"",\s*""([^""]+)"",\s*""\{[^}]+\}""",
        RegexOptions.Compiled);

    /// <summary>Finds every .sln under <paramref name="projectsDir"/> (any depth) and parses its projects.</summary>
    public static List<SolutionInfo> GetSolutions(string projectsDir)
    {
        var solutions = new List<SolutionInfo>();
        if (string.IsNullOrWhiteSpace(projectsDir) || !Directory.Exists(projectsDir))
            return solutions;

        IEnumerable<string> slnFiles;
        try
        {
            slnFiles = Directory.EnumerateFiles(projectsDir, "*.sln", SearchOption.AllDirectories);
        }
        catch
        {
            return solutions;
        }

        foreach (var sln in slnFiles)
        {
            try
            {
                var info = ParseSolution(sln);
                if (info.Projects.Count > 0)
                    solutions.Add(info);
            }
            catch
            {
                // Skip malformed solutions.
            }
        }

        solutions.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        return solutions;
    }

    private static SolutionInfo ParseSolution(string slnPath)
    {
        var solution = new SolutionInfo
        {
            Name = Path.GetFileNameWithoutExtension(slnPath),
            SolutionPath = slnPath,
        };
        string slnDir = Path.GetDirectoryName(slnPath)!;

        foreach (var line in File.ReadLines(slnPath))
        {
            var m = ProjectLine.Match(line);
            if (!m.Success)
                continue;

            string relPath = m.Groups[1].Value;
            if (!relPath.EndsWith(".rnrproj", StringComparison.OrdinalIgnoreCase))
                continue; // only D365 projects

            string projPath = Path.GetFullPath(Path.Combine(slnDir, relPath));
            if (!File.Exists(projPath))
                continue;

            var project = ParseProject(projPath);
            if (project != null)
                solution.Projects.Add(project);
        }

        solution.Projects.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        return solution;
    }

    private static ProjectInfo? ParseProject(string projPath)
    {
        XDocument doc;
        try
        {
            doc = XDocument.Load(projPath);
        }
        catch
        {
            return null;
        }

        string model = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "Model")?.Value?.Trim() ?? "";
        string name = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "Name")?.Value?.Trim()
                      ?? Path.GetFileNameWithoutExtension(projPath);

        var project = new ProjectInfo
        {
            Name = name,
            ProjectPath = projPath,
            Model = model,
        };

        foreach (var content in doc.Descendants().Where(e => e.Name.LocalName == "Content"))
        {
            string include = content.Attribute("Include")?.Value ?? "";
            // Metadata items look like "AxClass\ItemName"; skip label .txt entries (no backslash).
            int slash = include.IndexOf('\\');
            if (slash <= 0)
                continue;
            string type = include[..slash];
            string itemName = include[(slash + 1)..];
            if (!type.StartsWith("Ax", StringComparison.Ordinal) || itemName.Length == 0)
                continue;
            project.ItemKeys.Add(ProjectInfo.Key(type, itemName));
        }

        return project;
    }
}
