using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sync.Sharp.Models;
using Sync.Sharp.Models.Options;

namespace Sync.Sharp.Services;

public interface IStateStore
{
    Task InitializeAsync(CancellationToken ct);
    bool IsExternalIdProcessed(string externalId);
    bool IsHashProcessed(string infoHash);
    bool IsHashSynced(string infoHash);
    Task RecordGrabbedAsync(string? externalId, string? infoHash, string title, MediaCategory category, CancellationToken ct);
    Task MarkSyncedAsync(string infoHash, string title, MediaCategory category, CancellationToken ct);
}

public sealed class JsonStateStore(
    IOptions<StorageConfigOpts> storageOptions,
    JsonSerializerOptions jsonOptions,
    ILogger<JsonStateStore> logger) : IStateStore
{
    private readonly StorageConfigOpts _storageConfig = storageOptions?.Value ?? throw new ArgumentNullException(nameof(storageOptions));
    private readonly JsonSerializerOptions _jsonOptions = jsonOptions ?? throw new ArgumentNullException(nameof(jsonOptions));

    private const int InitialRequestCount = 1;
    private const int MaxRequestCount = 1;
    private readonly SemaphoreSlim _fileLock = new(InitialRequestCount, MaxRequestCount);
    private readonly Lock _stateLock = new();
    private SyncStateDocument _state = new([]);
    private readonly HashSet<string> _externalIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _hashIndex = new(StringComparer.OrdinalIgnoreCase);

    public async Task InitializeAsync(CancellationToken ct)
    {
        await _fileLock.WaitAsync(ct);
        try
        {
            var path = _storageConfig.StateFilePath;
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(dir))
            {
                Directory.CreateDirectory(dir);
            }

            SyncStateDocument loaded;
            if (File.Exists(path))
            {
                await using var stream = File.OpenRead(path);
                loaded = await JsonSerializer.DeserializeAsync<SyncStateDocument>(
                    stream,
                    _jsonOptions,
                    ct) ?? new([]);
            }
            else
            {
                loaded = new([]);
                await SaveInternalAsync(loaded, ct);
            }

            lock (_stateLock)
            {
                _state = loaded;
                RebuildIndicesLocked();
            }

            logger.LogDebug(
                "Initialized JSON state store at {StateFilePath} ({Count} entries).",
                path,
                loaded.Items.Count);
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public bool IsExternalIdProcessed(string externalId)
    {
        if (string.IsNullOrWhiteSpace(externalId))
        {
            return false;
        }

        lock (_stateLock)
        {
            return _externalIds.Contains(externalId);
        }
    }

    public bool IsHashProcessed(string infoHash)
    {
        if (string.IsNullOrWhiteSpace(infoHash))
        {
            return false;
        }

        lock (_stateLock)
        {
            return _hashIndex.ContainsKey(infoHash);
        }
    }

    public bool IsHashSynced(string infoHash)
    {
        if (string.IsNullOrWhiteSpace(infoHash))
        {
            return false;
        }

        lock (_stateLock)
        {
            return _hashIndex.TryGetValue(infoHash, out var idx) &&
                   _state.Items[idx].Status == MediaSyncStatus.Synced;
        }
    }

    public async Task RecordGrabbedAsync(
        string? externalId,
        string? infoHash,
        string title,
        MediaCategory category,
        CancellationToken ct)
    {
        await _fileLock.WaitAsync(ct);
        try
        {
            SyncStateDocument snapshot;
            lock (_stateLock)
            {
                if (!string.IsNullOrWhiteSpace(infoHash) && _hashIndex.ContainsKey(infoHash))
                {
                    return;
                }

                if (string.IsNullOrWhiteSpace(infoHash) &&
                    !string.IsNullOrWhiteSpace(externalId) &&
                    _externalIds.Contains(externalId))
                {
                    return;
                }

                var entry = new MediaHistoryEntry(
                    ExternalId: externalId,
                    InfoHash: infoHash?.ToLowerInvariant() ?? string.Empty,
                    Title: title,
                    Category: category,
                    Status: MediaSyncStatus.Grabbed,
                    CreatedAt: DateTimeOffset.UtcNow,
                    SyncedAt: null);

                AddEntryLocked(entry);
                snapshot = new SyncStateDocument([.. _state.Items]);
            }

            await SaveInternalAsync(snapshot, ct);
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public async Task MarkSyncedAsync(
        string infoHash,
        string title,
        MediaCategory category,
        CancellationToken ct)
    {
        await _fileLock.WaitAsync(ct);
        try
        {
            var now = DateTimeOffset.UtcNow;
            SyncStateDocument snapshot;
            lock (_stateLock)
            {
                if (_hashIndex.TryGetValue(infoHash, out var idx))
                {
                    var existing = _state.Items[idx];
                    _state.Items[idx] = existing with
                    {
                        Status = MediaSyncStatus.Synced,
                        SyncedAt = now
                    };
                }
                else
                {
                    AddEntryLocked(new MediaHistoryEntry(
                        ExternalId: null,
                        InfoHash: infoHash.ToLowerInvariant(),
                        Title: title,
                        Category: category,
                        Status: MediaSyncStatus.Synced,
                        CreatedAt: now,
                        SyncedAt: now));
                }

                snapshot = new SyncStateDocument([.. _state.Items]);
            }

            await SaveInternalAsync(snapshot, ct);
        }
        finally
        {
            _fileLock.Release();
        }
    }

    private void RebuildIndicesLocked()
    {
        _externalIds.Clear();
        _hashIndex.Clear();

        for (var i = 0; i < _state.Items.Count; i++)
        {
            IndexEntryLocked(_state.Items[i], i);
        }
    }

    private void AddEntryLocked(MediaHistoryEntry entry)
    {
        var idx = _state.Items.Count;
        _state.Items.Add(entry);
        IndexEntryLocked(entry, idx);
    }

    private void IndexEntryLocked(MediaHistoryEntry entry, int index)
    {
        if (!string.IsNullOrWhiteSpace(entry.ExternalId))
        {
            _externalIds.Add(entry.ExternalId);
        }

        if (!string.IsNullOrWhiteSpace(entry.InfoHash))
        {
            _hashIndex.TryAdd(entry.InfoHash, index);
        }
    }

    private async Task SaveInternalAsync(SyncStateDocument state, CancellationToken ct)
    {
        var path = _storageConfig.StateFilePath;
        var tempPath = $"{path}.tmp";

        await using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(
                stream,
                state,
                _jsonOptions,
                ct);
        }

        File.Move(tempPath, path, overwrite: true);
    }
}
