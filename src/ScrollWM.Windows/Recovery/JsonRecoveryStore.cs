using System.Text.Json;
using ScrollWM.Core.Recovery;

namespace ScrollWM.Windows.Recovery;

public sealed class JsonRecoveryStore(string directory) : IRecoveryStore
{
    internal Action<TimeSpan> RetryDelay { get; init; } = Thread.Sleep;

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        IgnoreReadOnlyProperties = true,
    };

    public string JournalPath { get; } = Path.Combine(Path.GetFullPath(directory), "recovery.json");

    public RecoveryJournal? Load()
    {
        if (!File.Exists(JournalPath))
        {
            return null;
        }

        var journal = JsonSerializer.Deserialize<RecoveryJournal>(File.ReadAllBytes(JournalPath), Options)
            ?? throw new InvalidDataException("The recovery journal is empty or invalid. Keep it for diagnosis.");

        if (journal.Version is not (1 or RecoveryJournal.CurrentVersion) || journal.Windows is null ||
            journal.Windows.Any(item => item is null || item.Identity is null || item.Placement is null ||
                item.Identity.Handle == 0 || item.Identity.ProcessId <= 0 ||
                item.Identity.ProcessStartUtcTicks <= 0 || item.RecoveryToken <= 0 ||
                string.IsNullOrEmpty(item.Identity.ClassName) || item.Identity.ThreadId == 0 ||
                item.Placement.NormalBounds.Width <= 0 || item.Placement.NormalBounds.Height <= 0 ||
                item.OriginalRegion is { Length: > 1048576 } ||
                item.OriginalOuterFrame is { } outer && (outer.Width <= 0 || outer.Height <= 0) ||
                item.OriginalDisplay is { } display && (string.IsNullOrWhiteSpace(display.Id) ||
                    display.Dpi is < 48 or > 768 || display.WorkArea.Width <= 0 || display.WorkArea.Height <= 0 ||
                    display.Bounds.Width <= 0 || display.Bounds.Height <= 0)) ||
            journal.Windows.Select(item => item.Identity.Handle).Distinct().Count() != journal.Windows.Length)
        {
            throw new InvalidDataException("The recovery journal has an unsupported version or invalid entries. It was preserved.");
        }

        return journal;
    }

    public void Save(RecoveryJournal journal)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(JournalPath)!);
        var temporaryPath = JournalPath + ".tmp";
        var bytes = JsonSerializer.SerializeToUtf8Bytes(journal, Options);
        using (var stream = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None,
            4096, FileOptions.WriteThrough))
        {
            stream.Write(bytes);
            stream.Flush(flushToDisk: true);
        }

        // A reader without delete sharing can briefly block replacement. Retry
        // only documented errors that retain both file names; never delete the
        // old journal as a workaround or retry a partially completed replace.
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                if (File.Exists(JournalPath))
                {
                    File.Replace(temporaryPath, JournalPath, null);
                }
                else
                {
                    File.Move(temporaryPath, JournalPath);
                }

                return;
            }
            catch (IOException exception) when (attempt < 4 && IsRetryableReplacement(exception) &&
                File.Exists(temporaryPath) && File.Exists(JournalPath))
            {
                RetryDelay(TimeSpan.FromMilliseconds(50 * (attempt + 1)));
            }
        }
    }

    private static bool IsRetryableReplacement(IOException exception) =>
        (exception.HResult & 0xFFFF) is 32 or 33 or 1175;
}
