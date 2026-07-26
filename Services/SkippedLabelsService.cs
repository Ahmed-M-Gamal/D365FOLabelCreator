using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using D365LabelCreator.Models;

namespace D365LabelCreator.Services;

/// <summary>
/// Manages persisted skipped labels per model. Stores them in a .skippedlabels.json file
/// inside the model directory so they are tied to the specific model location.
/// </summary>
public static class SkippedLabelsService
{
    private const string SkippedLabelsFileName = ".skippedlabels.json";

    /// <summary>Gets the path to the skipped labels file for a given model.</summary>
    private static string GetSkippedLabelsPath(string modelDir) =>
        Path.Combine(modelDir, SkippedLabelsFileName);

    /// <summary>Loads the list of skipped label keys for a model from disk.</summary>
    public static List<string> LoadSkippedLabels(string modelDir)
    {
        var path = GetSkippedLabelsPath(modelDir);
        if (!File.Exists(path))
            return new List<string>();

        try
        {
            var json = File.ReadAllText(path);
            var data = JsonSerializer.Deserialize<SkippedLabelsData>(json);
            return data?.SkippedKeys ?? new List<string>();
        }
        catch
        {
            // If the file is corrupted or unreadable, start fresh
            return new List<string>();
        }
    }

    /// <summary>Saves the list of skipped label keys for a model to disk.</summary>
    public static void SaveSkippedLabels(string modelDir, List<string> skippedKeys)
    {
        var path = GetSkippedLabelsPath(modelDir);
        try
        {
            var data = new SkippedLabelsData
            {
                SkippedKeys = skippedKeys,
                LastUpdated = DateTime.UtcNow
            };
            var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);
        }
        catch (Exception ex)
        {
            // Log or silently fail — don't block the UI
            System.Diagnostics.Debug.WriteLine($"Failed to save skipped labels: {ex.Message}");
        }
    }

    /// <summary>Adds a label key to the skipped list and saves to disk.</summary>
    public static void AddSkippedLabel(string modelDir, string labelKey)
    {
        var skippedKeys = LoadSkippedLabels(modelDir);
        if (!skippedKeys.Contains(labelKey, StringComparer.OrdinalIgnoreCase))
        {
            skippedKeys.Add(labelKey);
            SaveSkippedLabels(modelDir, skippedKeys);
        }
    }

    /// <summary>Adds multiple label keys to the skipped list and saves to disk.</summary>
    public static int AddSkippedLabels(string modelDir, IEnumerable<string> labelKeys)
    {
        var skippedKeys = LoadSkippedLabels(modelDir);
        int added = 0;
        foreach (var key in labelKeys)
        {
            if (!skippedKeys.Contains(key, StringComparer.OrdinalIgnoreCase))
            {
                skippedKeys.Add(key);
                added++;
            }
        }

        if (added > 0)
            SaveSkippedLabels(modelDir, skippedKeys);

        return added;
    }

    /// <summary>Removes a label key from the skipped list and saves to disk.</summary>
    public static bool RemoveSkippedLabel(string modelDir, string labelKey)
    {
        var skippedKeys = LoadSkippedLabels(modelDir);
        var removed = skippedKeys.RemoveAll(k => string.Equals(k, labelKey, StringComparison.OrdinalIgnoreCase));
        if (removed > 0)
            SaveSkippedLabels(modelDir, skippedKeys);
        return removed > 0;
    }

    /// <summary>Clears all skipped labels for a model.</summary>
    public static void ClearSkippedLabels(string modelDir)
    {
        var path = GetSkippedLabelsPath(modelDir);
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to clear skipped labels: {ex.Message}");
        }
    }

    /// <summary>Internal data structure for JSON serialization.</summary>
    private sealed class SkippedLabelsData
    {
        public List<string> SkippedKeys { get; set; } = new();
        public DateTime LastUpdated { get; set; }
    }
}
