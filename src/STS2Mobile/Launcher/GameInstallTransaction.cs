using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace STS2Mobile.Launcher;

internal enum GameInstallFaultPoint
{
    AfterPrepared,
    AfterActiveRetired,
    AfterStagedActivated,
}

internal sealed class GameInstallInterruptionException : Exception
{
    public GameInstallInterruptionException(string message)
        : base(message) { }
}

internal sealed class GameInstallTuple
{
    public int Schema { get; set; } = 1;
    public string TransactionId { get; set; }
    public string Branch { get; set; }
    public string BuildId { get; set; }
    public long PckLength { get; set; }
    public long PckWriteTicks { get; set; }
    public long AssemblyLength { get; set; }
    public long AssemblyWriteTicks { get; set; }
    public Dictionary<string, GameInstallFileIdentity> Assemblies { get; set; } = new();
    public long AtlasSourcePckWriteTicks { get; set; }
    public Dictionary<uint, ulong> Depots { get; set; } = new();

    public static GameInstallTuple Capture(
        string gameDirectory,
        string branch,
        string buildId,
        IReadOnlyDictionary<uint, ulong> depots
    )
    {
        var pck = new FileInfo(Path.Combine(gameDirectory, "SlayTheSpire2.pck"));
        var assembly = new FileInfo(FindGameAssembly(gameDirectory));
        if (!pck.Exists || !HasPckMagic(pck.FullName) || !assembly.Exists)
            throw new InvalidDataException("A complete install requires both PCK and sts2.dll");

        return new GameInstallTuple
        {
            TransactionId = Guid.NewGuid().ToString("N"),
            Branch = string.IsNullOrWhiteSpace(branch) ? "public" : branch,
            BuildId = buildId ?? "",
            PckLength = pck.Length,
            PckWriteTicks = pck.LastWriteTimeUtc.Ticks,
            AssemblyLength = assembly.Length,
            AssemblyWriteTicks = assembly.LastWriteTimeUtc.Ticks,
            Assemblies = CaptureAssemblySet(gameDirectory),
            AtlasSourcePckWriteTicks = pck.LastWriteTimeUtc.Ticks,
            Depots = depots?.ToDictionary(pair => pair.Key, pair => pair.Value) ?? new(),
        };
    }

    public bool MatchesFiles(string gameDirectory)
    {
        try
        {
            var pck = new FileInfo(Path.Combine(gameDirectory, "SlayTheSpire2.pck"));
            var assembly = new FileInfo(FindGameAssembly(gameDirectory));
            return Schema == 1
                && !string.IsNullOrEmpty(TransactionId)
                && pck.Exists
                && HasPckMagic(pck.FullName)
                && assembly.Exists
                && pck.Length == PckLength
                && pck.LastWriteTimeUtc.Ticks == PckWriteTicks
                && assembly.Length == AssemblyLength
                && assembly.LastWriteTimeUtc.Ticks == AssemblyWriteTicks
                && AtlasSourcePckWriteTicks == PckWriteTicks
                && AssemblySetMatches(gameDirectory);
        }
        catch
        {
            return false;
        }
    }

    private static string FindGameAssembly(string gameDirectory)
    {
        foreach (var dataDirectory in Directory.EnumerateDirectories(gameDirectory, "data_*"))
        {
            var candidate = Path.Combine(dataDirectory, "sts2.dll");
            if (File.Exists(candidate))
                return candidate;
        }
        return Path.Combine(gameDirectory, "data_sts2_windows_x86_64", "sts2.dll");
    }

    private static bool HasPckMagic(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var reader = new BinaryReader(stream);
            return stream.Length >= 4 && reader.ReadUInt32() == 0x43504447;
        }
        catch
        {
            return false;
        }
    }

    private static Dictionary<string, GameInstallFileIdentity> CaptureAssemblySet(
        string gameDirectory
    )
    {
        var assemblyDirectory = Path.GetDirectoryName(FindGameAssembly(gameDirectory));
        if (assemblyDirectory == null || !Directory.Exists(assemblyDirectory))
            return new Dictionary<string, GameInstallFileIdentity>();

        return Directory
            .EnumerateFiles(assemblyDirectory, "*.dll", SearchOption.TopDirectoryOnly)
            .Select(path => new FileInfo(path))
            .ToDictionary(
                file => file.Name,
                file => new GameInstallFileIdentity
                {
                    Length = file.Length,
                    WriteTicks = file.LastWriteTimeUtc.Ticks,
                },
                StringComparer.Ordinal
            );
    }

    private bool AssemblySetMatches(string gameDirectory)
    {
        if (Assemblies == null || Assemblies.Count == 0)
            return false;
        var actual = CaptureAssemblySet(gameDirectory);
        return actual.Count == Assemblies.Count
            && Assemblies.All(pair =>
                actual.TryGetValue(pair.Key, out var identity)
                && identity.Length == pair.Value.Length
                && identity.WriteTicks == pair.Value.WriteTicks
            );
    }
}

internal sealed class GameInstallFileIdentity
{
    public long Length { get; set; }
    public long WriteTicks { get; set; }
}

// Stages an entire install beside game/, then switches directories on one
// filesystem. Individual files may use hard links while unchanged; every write
// path replaces rather than mutates those links. The rollback remains until the
// new process reaches game-ready.
internal sealed class GameInstallTransaction
{
    internal const string CompletionMarkerName = ".launcher_install_complete";
    internal const string ValidationAttemptMarkerName = ".launcher_install_validation_attempt";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    [DllImport("libc", EntryPoint = "link", SetLastError = true)]
    private static extern int Link(string existingPath, string newPath);

    private readonly string _dataDirectory;

    private GameInstallTransaction(string dataDirectory)
    {
        _dataDirectory = dataDirectory;
        StagingGameDirectory = GetStagingPath(dataDirectory);
    }

    public string StagingGameDirectory { get; }
    public string StagingStateDirectory => Path.Combine(StagingGameDirectory, ".download_state");

    public static GameInstallTransaction Begin(string dataDirectory, bool forceFresh)
    {
        Recover(dataDirectory);
        if (Directory.Exists(GetRollbackPath(dataDirectory)))
        {
            throw new InvalidOperationException(
                "The previous game update has not reached a healthy startup yet"
            );
        }

        DiscardStaging(dataDirectory);
        var transaction = new GameInstallTransaction(dataDirectory);
        Directory.CreateDirectory(transaction.StagingGameDirectory);
        if (!forceFresh && Directory.Exists(GetActivePath(dataDirectory)))
            CloneDirectory(GetActivePath(dataDirectory), transaction.StagingGameDirectory);

        DeleteFileIfPresent(Path.Combine(transaction.StagingGameDirectory, CompletionMarkerName));
        return transaction;
    }

    public void SeedLegacyStateDirectory(string legacyStateDirectory)
    {
        if (
            Directory.Exists(StagingStateDirectory)
            || string.IsNullOrEmpty(legacyStateDirectory)
            || !Directory.Exists(legacyStateDirectory)
        )
        {
            return;
        }
        CloneDirectory(legacyStateDirectory, StagingStateDirectory);
    }

    // The staged tree normally shares unchanged files with active through hard
    // links. Call this before an operation that edits a file in place (the PCK
    // patcher is the only current caller), otherwise the old rollback would be
    // modified through the shared inode as well.
    public void DetachFileForWrite(string relativePath)
    {
        var path = Path.Combine(StagingGameDirectory, relativePath);
        if (!File.Exists(path))
            return;

        var temporary = path + ".detaching";
        File.Copy(path, temporary, overwrite: true);
        File.SetLastWriteTimeUtc(temporary, File.GetLastWriteTimeUtc(path));
        File.Move(temporary, path, overwrite: true);
    }

    public void Prepare(GameInstallTuple tuple)
    {
        if (tuple == null || !tuple.MatchesFiles(StagingGameDirectory))
            throw new InvalidDataException("Staged game tuple does not match its files");
        WriteTupleAtomically(StagingGameDirectory, tuple);
    }

    public void Commit(Action<GameInstallFaultPoint> fault = null)
    {
        var active = GetActivePath(_dataDirectory);
        var staging = StagingGameDirectory;
        var rollback = GetRollbackPath(_dataDirectory);
        var tuple = ReadTuple(staging);
        if (tuple == null || !tuple.MatchesFiles(staging))
            throw new InvalidDataException("Refusing to activate an incomplete staged game");

        try
        {
            fault?.Invoke(GameInstallFaultPoint.AfterPrepared);
            if (Directory.Exists(active))
                Directory.Move(active, rollback);
            fault?.Invoke(GameInstallFaultPoint.AfterActiveRetired);
            Directory.Move(staging, active);
            fault?.Invoke(GameInstallFaultPoint.AfterStagedActivated);
        }
        catch (GameInstallInterruptionException)
        {
            // Tests model a process disappearing at this exact point. Real debug
            // injection uses FailFast and cannot reach managed recovery here.
            throw;
        }
        catch
        {
            Recover(_dataDirectory);
            throw;
        }
    }

    public static void Recover(string dataDirectory)
    {
        var active = GetActivePath(dataDirectory);
        var staging = GetStagingPath(dataDirectory);
        var rollback = GetRollbackPath(dataDirectory);

        if (!Directory.Exists(active))
        {
            if (ReadTuple(staging)?.MatchesFiles(staging) == true)
                Directory.Move(staging, active);
            else if (Directory.Exists(rollback))
                Directory.Move(rollback, active);
            return;
        }

        if (!Directory.Exists(rollback))
            return;
        if (ReadTuple(active)?.MatchesFiles(active) == true)
            return;

        var failed = Path.Combine(
            dataDirectory,
            $"game.failed.{DateTime.UtcNow.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture)}"
        );
        Directory.Move(active, failed);
        Directory.Move(rollback, active);
    }

    public static GameInstallTuple ReadActiveTuple(string dataDirectory) =>
        ReadTuple(GetActivePath(dataDirectory));

    public static bool ActiveTupleMatchesFiles(string dataDirectory)
    {
        var active = GetActivePath(dataDirectory);
        return ReadTuple(active)?.MatchesFiles(active) == true;
    }

    public static bool ActiveHasCompletionMarker(string dataDirectory) =>
        File.Exists(Path.Combine(GetActivePath(dataDirectory), CompletionMarkerName));

    public static void CompleteValidation(string dataDirectory)
    {
        if (!ActiveTupleMatchesFiles(dataDirectory))
            return;
        DeleteFileIfPresent(
            Path.Combine(GetActivePath(dataDirectory), ValidationAttemptMarkerName)
        );
        DeleteDirectoryIfPresent(GetRollbackPath(dataDirectory));
        DeleteDirectoryIfPresent(GetStagingPath(dataDirectory));
        foreach (var failed in Directory.EnumerateDirectories(dataDirectory, "game.failed.*"))
            DeleteDirectoryIfPresent(failed);
    }

    public static void DiscardStaging(string dataDirectory) =>
        DeleteDirectoryIfPresent(GetStagingPath(dataDirectory));

    public static string GetActivePath(string dataDirectory) => Path.Combine(dataDirectory, "game");

    public static string GetStagingPath(string dataDirectory) =>
        Path.Combine(dataDirectory, "game.staging");

    public static string GetRollbackPath(string dataDirectory) =>
        Path.Combine(dataDirectory, "game.rollback");

    private static GameInstallTuple ReadTuple(string gameDirectory)
    {
        try
        {
            var marker = Path.Combine(gameDirectory, CompletionMarkerName);
            if (!File.Exists(marker))
                return null;
            return JsonSerializer.Deserialize<GameInstallTuple>(
                File.ReadAllText(marker),
                JsonOptions
            );
        }
        catch
        {
            return null;
        }
    }

    private static void WriteTupleAtomically(string gameDirectory, GameInstallTuple tuple)
    {
        var marker = Path.Combine(gameDirectory, CompletionMarkerName);
        var temporary = marker + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(tuple, JsonOptions));
        File.Move(temporary, marker, overwrite: true);
    }

    private static void CloneDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (
            var directory in Directory.EnumerateDirectories(
                source,
                "*",
                SearchOption.AllDirectories
            )
        )
        {
            Directory.CreateDirectory(
                Path.Combine(destination, Path.GetRelativePath(source, directory))
            );
        }
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            if (file.EndsWith(".downloading", StringComparison.Ordinal))
                continue;
            var target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            try
            {
                if (OperatingSystem.IsWindows() || Link(file, target) != 0)
                    throw new IOException(
                        new Win32Exception(Marshal.GetLastPInvokeError()).Message
                    );
            }
            catch
            {
                File.Copy(file, target, overwrite: false);
                File.SetLastWriteTimeUtc(target, File.GetLastWriteTimeUtc(file));
            }
        }
    }

    private static void DeleteFileIfPresent(string path)
    {
        if (File.Exists(path))
            File.Delete(path);
    }

    private static void DeleteDirectoryIfPresent(string path)
    {
        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
    }
}
