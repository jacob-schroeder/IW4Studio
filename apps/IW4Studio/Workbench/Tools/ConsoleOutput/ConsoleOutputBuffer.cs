using System.Collections.ObjectModel;
using IW4.Studio.Desktop.ViewModels;

namespace IW4.Studio.Desktop.Workbench.Tools.ConsoleOutput;

/// <summary>
/// Bounded, observable activity history for the Studio workbench console.
/// Producers write explicitly; this type never redirects process output.
/// </summary>
public sealed class ConsoleOutputBuffer : ObservableObject
{
    public const int DefaultCapacity = 1_000;

    private readonly ObservableCollection<ConsoleOutputEntry> _entries = new();
    private readonly TimeProvider _timeProvider;

    public ConsoleOutputBuffer(
        int capacity = DefaultCapacity,
        TimeProvider? timeProvider = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);

        Capacity = capacity;
        _timeProvider = timeProvider ?? TimeProvider.System;
        Entries = new ReadOnlyObservableCollection<ConsoleOutputEntry>(_entries);
    }

    public int Capacity { get; }

    public ReadOnlyObservableCollection<ConsoleOutputEntry> Entries { get; }

    public int Count => _entries.Count;

    public bool HasEntries => Count != 0;

    public ConsoleOutputEntry Append(
        ConsoleOutputLevel level,
        string source,
        string message)
    {
        var entry = new ConsoleOutputEntry(
            _timeProvider.GetUtcNow(),
            level,
            source,
            message);

        Append(entry);
        return entry;
    }

    public void Append(ConsoleOutputEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        int previousCount = Count;
        if (_entries.Count == Capacity)
            _entries.RemoveAt(0);

        _entries.Add(entry);
        RaiseSummaryChanges(previousCount);
    }

    public void AppendRange(IEnumerable<ConsoleOutputEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        ConsoleOutputEntry[] incoming = entries.ToArray();
        if (incoming.Any(entry => entry is null))
            throw new ArgumentException("Console output cannot contain a null entry.", nameof(entries));

        if (incoming.Length == 0)
            return;

        int previousCount = Count;
        if (incoming.Length >= Capacity)
        {
            _entries.Clear();
            foreach (ConsoleOutputEntry entry in incoming[^Capacity..])
                _entries.Add(entry);
        }
        else
        {
            int entriesToRemove = Math.Max(0, _entries.Count + incoming.Length - Capacity);
            for (int index = 0; index < entriesToRemove; index++)
                _entries.RemoveAt(0);

            foreach (ConsoleOutputEntry entry in incoming)
                _entries.Add(entry);
        }

        RaiseSummaryChanges(previousCount);
    }

    public void Clear()
    {
        if (_entries.Count == 0)
            return;

        int previousCount = Count;
        _entries.Clear();
        RaiseSummaryChanges(previousCount);
    }

    private void RaiseSummaryChanges(int previousCount)
    {
        if (previousCount != Count)
            OnPropertyChanged(nameof(Count));

        if ((previousCount != 0) != HasEntries)
            OnPropertyChanged(nameof(HasEntries));
    }
}
