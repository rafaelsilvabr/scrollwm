using System.Text.Json;
using ScrollWM.Core.Recovery;
using ScrollWM.Core.Windows;
using ScrollWM.Windows.Recovery;
using Xunit;

namespace ScrollWM.Windows.Tests;

public sealed class JsonRecoveryStoreTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "ScrollWM.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void RoundTripPreservesPlacementAndCanBeReadByANewStore()
    {
        var store = new JsonRecoveryStore(directory);
        var snapshot = new WindowSnapshot(new(1, 2, 1234, 3, "Fixture"),
            new(0, 1, new(-1, -1), new(-1, -1), new(10, 20, 600, 400)), new(10, 20, 600, 400), 144, 123);
        var journal = new RecoveryJournal(1, DateTimeOffset.UtcNow, [snapshot]);
        store.Save(journal);

        var loaded = new JsonRecoveryStore(directory).Load();
        Assert.NotNull(loaded);
        Assert.Equal(journal.CreatedUtc, loaded.CreatedUtc);
        Assert.Equal(snapshot, Assert.Single(loaded.Windows));
        Assert.False(File.Exists(store.JournalPath + ".tmp"));

        store.Save(journal with { Windows = [] });
        Assert.Empty(new JsonRecoveryStore(directory).Load()!.Windows);
    }

    [Theory]
    [InlineData("{")]
    [InlineData("null")]
    [InlineData("{\"Version\":99,\"Windows\":[]}")]
    [InlineData("{\"Version\":1,\"Windows\":null}")]
    [InlineData("{\"Version\":1,\"Windows\":[null]}")]
    public void InvalidJournalIsNotSilentlyOverwritten(string content)
    {
        Directory.CreateDirectory(directory);
        var store = new JsonRecoveryStore(directory);
        File.WriteAllText(store.JournalPath, content);

        var exception = Record.Exception(() => store.Load());
        Assert.True(exception is InvalidDataException or JsonException);
        Assert.Equal(content, File.ReadAllText(store.JournalPath));
    }

    [Fact]
    public void VersionTwoPreservesMonitorRegionAndOuterBounds()
    {
        var region = new byte[32]; region[0] = 32; region[4] = 1;
        var display = new DisplayInfo("monitor-interface", new(-1600, -200, 1600, 1000), new(-1600, -200, 1600, 960), 144, false);
        var snapshot = new WindowSnapshot(new(1, 2, 1234, 3, "Fixture"),
            new(0, 1, new(-1, -1), new(-1, -1), new(-1500, -100, 600, 400)), new(-1493, -100, 586, 393), 144, 123,
            region, display, new(-1500, -100, 600, 400));
        var store = new JsonRecoveryStore(directory);
        store.Save(new(RecoveryJournal.CurrentVersion, DateTimeOffset.UtcNow, [snapshot]));
        var loaded = Assert.Single(new JsonRecoveryStore(directory).Load()!.Windows);
        Assert.Equal(region, loaded.OriginalRegion);
        Assert.Equal(display, loaded.OriginalDisplay);
        Assert.Equal(snapshot.OriginalOuterFrame, loaded.OriginalOuterFrame);
    }

    [Fact]
    public void BriefReplacementLockIsRetriedWithoutDiscardingTheJournal()
    {
        var original = new RecoveryJournal(1, DateTimeOffset.UtcNow, []);
        var initialStore = new JsonRecoveryStore(directory);
        initialStore.Save(original);
        using var reader = new FileStream(initialStore.JournalPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var retries = 0;
        var retryingStore = new JsonRecoveryStore(directory)
        {
            RetryDelay = _ =>
            {
                retries++;
                reader.Dispose();
            },
        };
        var replacement = original with { CreatedUtc = original.CreatedUtc.AddSeconds(1) };

        retryingStore.Save(replacement);

        Assert.True(retries > 0);
        Assert.Equal(replacement.CreatedUtc, new JsonRecoveryStore(directory).Load()!.CreatedUtc);
        Assert.False(File.Exists(initialStore.JournalPath + ".tmp"));
    }

    [Fact]
    public void PersistentReplacementLockFailsWithOriginalJournalPreserved()
    {
        var store = new JsonRecoveryStore(directory) { RetryDelay = _ => { } };
        var original = new RecoveryJournal(1, DateTimeOffset.UtcNow, []);
        store.Save(original);
        var originalBytes = File.ReadAllBytes(store.JournalPath);
        using var reader = new FileStream(store.JournalPath, FileMode.Open, FileAccess.Read, FileShare.Read);

        Assert.Throws<IOException>(() => store.Save(original with { CreatedUtc = original.CreatedUtc.AddSeconds(1) }));

        Assert.Equal(originalBytes, File.ReadAllBytes(store.JournalPath));
        Assert.True(File.Exists(store.JournalPath + ".tmp"));
    }

    public void Dispose()
    {
        File.Delete(Path.Combine(directory, "recovery.json"));
        File.Delete(Path.Combine(directory, "recovery.json.tmp"));
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory);
        }
    }
}
