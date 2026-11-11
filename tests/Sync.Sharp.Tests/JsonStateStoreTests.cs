using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sync.Sharp.Models.Options;
using Sync.Sharp.Services;

namespace Sync.Sharp.Tests;

public class JsonStateStoreTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _stateFilePath;

    public JsonStateStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"sync-sharp-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _stateFilePath = Path.Combine(_tempDir, "state.json");
    }

    [Fact]
    public async Task InitializeRecordAndMarkSynced_ShouldPersistStateCorrectly()
    {
        var options = Options.Create(new SchedulingConfigOpts
        {
            StateFilePath = _stateFilePath
        });
        var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var store = new JsonStateStore(options, jsonOptions, NullLogger<JsonStateStore>.Instance);

        await store.InitializeAsync(CancellationToken.None);
        File.Exists(_stateFilePath).Should().BeTrue();

        await store.RecordGrabbedAsync("tt1234567", "ABCDEF123456", "Test Movie 2026", "movies", CancellationToken.None);

        store.IsExternalIdProcessed("tt1234567").Should().BeTrue();
        store.IsHashProcessed("abcdef123456").Should().BeTrue();
        store.IsHashSynced("abcdef123456").Should().BeFalse();

        await store.MarkSyncedAsync("abcdef123456", "Test Movie 2026", "movies", CancellationToken.None);
        store.IsHashSynced("ABCDEF123456").Should().BeTrue();

        // Reload in a second instance to verify disk persistence
        var reloadedStore = new JsonStateStore(options, jsonOptions, NullLogger<JsonStateStore>.Instance);
        await reloadedStore.InitializeAsync(CancellationToken.None);
        reloadedStore.IsExternalIdProcessed("tt1234567").Should().BeTrue();
        reloadedStore.IsHashSynced("abcdef123456").Should().BeTrue();
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }
}
