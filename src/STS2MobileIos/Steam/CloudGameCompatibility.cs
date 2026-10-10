using System.Text.Json;
using System.Text.RegularExpressions;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Validation;

namespace STS2MobileIos.Steam;

internal static class CloudGameCompatibility
{
    public static void RequireMainMenu(bool sourceModded)
    {
        string mismatch = CloudProfileImport.ModeMismatch(sourceModded, UserDataPathProvider.IsRunningModded);
        if (mismatch != null) throw new InvalidOperationException(mismatch);
        if (NGame.Instance?.MainMenu == null || NRun.Instance != null
            || RunManager.Instance.IsInProgress || Lan.LanSession.Busy || !SaveManager.Instance.IsProfileInitialized
            || SaveManager.Instance.CurrentRunSaveTask is { IsCompleted: false })
            throw new InvalidOperationException("Return to the main menu and finish saving before importing a profile.");
    }

    public static string Validate(CloudProfileCopy copy)
    {
        RequireMainMenu(copy.IsModded);
        var progress = Read<SerializableProgress>(copy.Data["progress.save"]);
        var context = new DeserializationContext();
        try { _ = ProgressState.FromSerializable(progress, context); }
        catch { throw new InvalidOperationException("This progress save cannot be loaded by the installed game."); }
        var blocking = context.Errors.Where(error =>
            !ProgressImportWarnings.IsRetainedHistory(error.IsFatal, error.Path, error.Message)).ToList();
        if (blocking.Count != 0)
        {
            string reason = string.Join("; ", blocking.Take(3).Select(error => error.Path + ": " + error.Message));
            reason = new string(reason.Where(character => !char.IsControl(character)).Take(500).ToArray());
            throw new InvalidOperationException("This progress save needs a repair that import cannot apply: " + reason);
        }
        if (copy.Data.TryGetValue("prefs.save", out var prefs)) _ = Read<PrefsSave>(prefs);
        string details = $"{progress.NumberOfRuns} completed runs | {progress.TotalPlaytime / 3600.0:F1} hours played";
        if (copy.Data.TryGetValue("current_run.save", out var bytes))
        {
            var run = Read<SerializableRun>(bytes);
            if (run.Players?.Count != 1 || run.Players[0].NetId != 1 || run.DailyTime != null)
                throw new InvalidOperationException("Only a regular single-player run is supported by this import.");
            if (run.Acts == null || run.CurrentActIndex < 0 || run.CurrentActIndex >= run.Acts.Count
                || run.Players[0].CurrentHp <= 0 || run.Players[0].CurrentHp > run.Players[0].MaxHp)
                throw new InvalidOperationException("This current run is incomplete or cannot be continued.");
            using var document = JsonDocument.Parse(bytes);
            CheckModels(document.RootElement);
            try { _ = RunState.FromSerializable(run); }
            catch { throw new InvalidOperationException("This current run references content the installed game cannot load. Import progress without the run."); }
            details += $"\nCurrent run: {run.Players[0].CharacterId.Entry}, act {run.CurrentActIndex + 1}, ascension {run.Ascension}";
        }
        else details += "\nProgress and preferences only; no current run or run history.";
        if (copy.IsModded)
            details += "\nModded profile: checked against the currently loaded mods. Keep the same mod versions as on desktop.";
        return details + ProgressImportWarnings.Summary(context.Errors.Count);
    }

    private static T Read<T>(byte[] bytes) where T : ISaveSchema, new()
    {
        try
        {
            using var document = JsonDocument.Parse(bytes);
            var root = document.RootElement;
            int latest = SaveManager.Instance.GetLatestSchemaVersion<T>();
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("schema_version", out var schema)
                || !schema.TryGetInt32(out int version) || version != latest)
                throw new InvalidOperationException("The cloud save schema does not match the installed game. Update to the same game version before importing.");
            var typeInfo = JsonSerializationUtility.GetTypeInfo<T>();
            var known = typeInfo.Properties.Select(property => property.Name).ToHashSet(StringComparer.Ordinal);
            if (root.EnumerateObject().Any(property => !known.Contains(property.Name)))
                throw new InvalidOperationException("The cloud save has fields the installed game and loaded mods do not recognize. Install matching game/mod versions before importing.");
            return JsonSerializer.Deserialize(bytes, typeInfo)
                ?? throw new InvalidOperationException("The cloud save is empty.");
        }
        catch (JsonException) { throw new InvalidOperationException("The cloud save format is not compatible with the installed game."); }
    }

    private static void CheckModels(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in value.EnumerateObject()) CheckModels(property.Value);
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in value.EnumerateArray()) CheckModels(child);
        }
        else if (value.ValueKind == JsonValueKind.String)
        {
            string text = value.GetString();
            if (text != "NONE.NONE" && Regex.IsMatch(text, @"^[A-Z][A-Z0-9_]*\.[A-Z][A-Z0-9_]*$"))
            {
                var id = ModelId.Deserialize(text);
                if (ModelDb.GetByIdOrNull<AbstractModel>(id) == null)
                    throw new InvalidOperationException("This run contains unavailable game content. Import progress without the run.");
            }
        }
    }
}
