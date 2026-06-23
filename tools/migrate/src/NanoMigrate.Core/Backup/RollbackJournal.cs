// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace nanoFramework.Migrate.Core.Backup;

/// <summary>
/// One recorded action in a rollback journal: either a file the migration CREATED
/// (which a rollback deletes) or a file it MODIFIED/DELETED whose original content
/// was backed up (which a rollback restores).
/// </summary>
public sealed class RollbackEntry
{
    /// <summary>The kind of action that was recorded.</summary>
    public RollbackAction Action { get; set; }

    /// <summary>
    /// The live (original) path the action concerns: the file that was created (for
    /// <see cref="RollbackAction.Created"/>) or the file that was modified/deleted and
    /// must be restored (for <see cref="RollbackAction.Restore"/>).
    /// </summary>
    public string OriginalPath { get; set; } = "";

    /// <summary>
    /// For <see cref="RollbackAction.Restore"/>: the path of the backup copy that
    /// holds the original bytes. Null for <see cref="RollbackAction.Created"/>.
    /// </summary>
    public string? BackupPath { get; set; }
}

/// <summary>What a recorded entry does on rollback.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum RollbackAction
{
    /// <summary>The migration created this file; rolling back deletes it.</summary>
    Created,

    /// <summary>The migration modified or deleted this file; rolling back restores it from the backup.</summary>
    Restore,
}

/// <summary>
/// The on-disk manifest of a single migration run: an id, a timestamp, and the
/// ordered list of recorded actions. Serialized as JSON inside the run's backup
/// folder so a later <c>rollback</c> can reverse the run without git.
/// </summary>
public sealed class RollbackManifest
{
    /// <summary>The run id (also the backup folder name suffix).</summary>
    public string Id { get; set; } = "";

    /// <summary>When the run was started (UTC, round-trip format).</summary>
    public string CreatedUtc { get; set; } = "";

    /// <summary>The recorded actions, in the order they were registered.</summary>
    public List<RollbackEntry> Entries { get; set; } = new();
}

/// <summary>
/// Records — and reverses — the file-level effects of a migration run so a real
/// run is reversible without git. Before anything is changed the command asks the
/// journal to back up every file that will be modified or deleted and to remember
/// every file that will be created; the backups + a JSON manifest live under
/// <c>.nanomigrate/rollback-&lt;id&gt;/</c> beneath a chosen root. A later rollback
/// reads the manifest, restores the backups, and deletes the created files.
///
/// Pure file logic — no console. Idempotent and safe: restoring an already-restored
/// tree, or rolling back when there is no journal, is a clean no-op.
/// </summary>
public sealed class RollbackJournal
{
    /// <summary>The folder that holds all migration rollback sets (<c>.nanomigrate</c>).</summary>
    public const string FolderName = ".nanomigrate";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly RollbackManifest _manifest;
    private int _backupSeq;

    /// <summary>The root the <c>.nanomigrate</c> folder is created under.</summary>
    public string Root { get; }

    /// <summary>This run's id.</summary>
    public string Id => _manifest.Id;

    /// <summary>The per-run set folder: <c>&lt;root&gt;/.nanomigrate/rollback-&lt;id&gt;</c>.</summary>
    public string SetDirectory => Path.Combine(Root, FolderName, "rollback-" + _manifest.Id);

    /// <summary>The manifest file path inside <see cref="SetDirectory"/>.</summary>
    public string ManifestPath => Path.Combine(SetDirectory, "manifest.json");

    /// <summary>The recorded entries so far.</summary>
    public IReadOnlyList<RollbackEntry> Entries => _manifest.Entries;

    private RollbackJournal(string root, RollbackManifest manifest)
    {
        Root = root;
        _manifest = manifest;
    }

    /// <summary>
    /// Starts a fresh journal rooted at <paramref name="root"/>. The backup folder is
    /// created lazily on the first <see cref="BackupBeforeChange"/>; an empty run that
    /// records nothing leaves no folder behind.
    /// </summary>
    public static RollbackJournal Start(string root)
    {
        var id = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..8];
        return new RollbackJournal(Path.GetFullPath(root), new RollbackManifest
        {
            Id = id,
            CreatedUtc = DateTime.UtcNow.ToString("O"),
        });
    }

    /// <summary>
    /// Backs up the original bytes of a file that the migration is about to MODIFY or
    /// DELETE, recording a restore entry. A missing source is ignored (nothing to
    /// restore). Re-recording the same original path is a no-op so the FIRST backup —
    /// the true original — always wins.
    /// </summary>
    public void BackupBeforeChange(string path)
    {
        var full = Path.GetFullPath(path);
        if (!File.Exists(full)) return;
        if (_manifest.Entries.Any(e => e.Action == RollbackAction.Restore
                                    && PathEquals(e.OriginalPath, full)))
            return;

        Directory.CreateDirectory(SetDirectory);
        var backupName = $"{_backupSeq++:D4}-{Path.GetFileName(full)}.bak";
        var backupPath = Path.Combine(SetDirectory, backupName);
        File.Copy(full, backupPath, overwrite: true);

        _manifest.Entries.Add(new RollbackEntry
        {
            Action = RollbackAction.Restore,
            OriginalPath = full,
            BackupPath = backupPath,
        });
    }

    /// <summary>
    /// Records that the migration CREATED <paramref name="path"/> (so a rollback
    /// deletes it). Recording the same path twice is a no-op.
    /// </summary>
    public void RecordCreated(string path)
    {
        var full = Path.GetFullPath(path);
        if (_manifest.Entries.Any(e => e.Action == RollbackAction.Created
                                    && PathEquals(e.OriginalPath, full)))
            return;
        _manifest.Entries.Add(new RollbackEntry
        {
            Action = RollbackAction.Created,
            OriginalPath = full,
        });
    }

    /// <summary>True when nothing has been recorded yet.</summary>
    public bool IsEmpty => _manifest.Entries.Count == 0;

    /// <summary>
    /// Writes the manifest to disk so the run can later be rolled back. A journal that
    /// recorded nothing writes nothing (no empty folder is left behind).
    /// </summary>
    public void Save()
    {
        if (IsEmpty) return;
        Directory.CreateDirectory(SetDirectory);
        File.WriteAllText(ManifestPath, JsonSerializer.Serialize(_manifest, JsonOpts), new UTF8Encoding(false));
    }

    /// <summary>
    /// The newest saved manifest under <paramref name="root"/>'s <c>.nanomigrate</c>
    /// folder, or null when none exists. "Newest" is by the manifest's recorded UTC
    /// timestamp (folder names sort the same way, so this is stable).
    /// </summary>
    public static RollbackManifest? FindLatest(string root)
    {
        var sets = ManifestPaths(root);
        RollbackManifest? newest = null;
        foreach (var path in sets)
        {
            var m = TryLoadManifest(path);
            if (m is null) continue;
            if (newest is null
                || string.Compare(m.CreatedUtc, newest.CreatedUtc, StringComparison.Ordinal) > 0)
                newest = m;
        }
        return newest;
    }

    /// <summary>Every saved manifest under <paramref name="root"/>, newest first.</summary>
    public static IReadOnlyList<string> ManifestPaths(string root)
    {
        var dir = Path.Combine(Path.GetFullPath(root), FolderName);
        if (!Directory.Exists(dir)) return Array.Empty<string>();
        return Directory.EnumerateFiles(dir, "manifest.json", SearchOption.AllDirectories)
            .OrderByDescending(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static RollbackManifest? TryLoadManifest(string manifestPath)
    {
        try { return JsonSerializer.Deserialize<RollbackManifest>(File.ReadAllText(manifestPath), JsonOpts); }
        catch { return null; }
    }

    /// <summary>
    /// Reverses the run described by <paramref name="manifest"/>: created files are
    /// deleted and modified/deleted files are restored from their backups. Restores
    /// run before deletes so a restore can never be clobbered. Idempotent: a created
    /// file already gone, or a restore target already matching its backup, is a no-op.
    /// Returns a tally of what was reverted (and any per-entry problems).
    /// </summary>
    public static RollbackResult Apply(RollbackManifest manifest, CancellationToken cancellationToken = default)
    {
        var result = new RollbackResult();

        // Restore originals first (modified/deleted files), then remove created files.
        foreach (var e in manifest.Entries.Where(e => e.Action == RollbackAction.Restore))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (e.BackupPath is null || !File.Exists(e.BackupPath))
                {
                    result.Problems.Add($"missing backup for {e.OriginalPath}");
                    continue;
                }
                var dir = Path.GetDirectoryName(e.OriginalPath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.Copy(e.BackupPath, e.OriginalPath, overwrite: true);
                result.Restored.Add(e.OriginalPath);
            }
            catch (Exception ex)
            {
                result.Problems.Add($"restore failed for {e.OriginalPath}: {ex.Message}");
            }
        }

        foreach (var e in manifest.Entries.Where(e => e.Action == RollbackAction.Created))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (File.Exists(e.OriginalPath))
                {
                    File.Delete(e.OriginalPath);
                    result.Deleted.Add(e.OriginalPath);
                }
            }
            catch (Exception ex)
            {
                result.Problems.Add($"delete failed for {e.OriginalPath}: {ex.Message}");
            }
        }

        return result;
    }

    /// <summary>
    /// Loads the manifest at <paramref name="manifestPath"/>, reverses it, and on
    /// success removes its backup set folder. Returns the result, or null when the
    /// manifest could not be read.
    /// </summary>
    public static RollbackResult? ApplyAndCleanup(string manifestPath, CancellationToken cancellationToken = default)
    {
        var manifest = TryLoadManifest(manifestPath);
        if (manifest is null) return null;
        var result = Apply(manifest, cancellationToken);

        // Remove the run's backup set once reverted (best-effort).
        try
        {
            var setDir = Path.GetDirectoryName(manifestPath);
            if (setDir is not null && Directory.Exists(setDir)) Directory.Delete(setDir, recursive: true);
        }
        catch { /* leave the set if it cannot be removed; clean can mop up later */ }

        return result;
    }

    private static bool PathEquals(string a, string b) =>
        string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
}

/// <summary>The tally of a rollback: files restored, files deleted, and any problems.</summary>
public sealed class RollbackResult
{
    /// <summary>Original files restored from backups.</summary>
    public List<string> Restored { get; } = new();

    /// <summary>Created files removed.</summary>
    public List<string> Deleted { get; } = new();

    /// <summary>Non-fatal problems encountered per entry (a missing backup, etc.).</summary>
    public List<string> Problems { get; } = new();

    /// <summary>Total files touched (restored + deleted).</summary>
    public int Total => Restored.Count + Deleted.Count;
}
