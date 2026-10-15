using Frontend.Pages.Measurement;

namespace Frontend.Calculation.Measurement;

/// <summary>
/// Display (and CSV) order of measurement rows: ascending numeric From; equal From values by capture Start Time, then
/// by stored record ID (rows not stored yet come after stored ones), then by Capture No. Rows stored before depths
/// were recorded (no From) come last. Ordering never changes a row's values, IDs or interval, and it is not used to
/// work out the next depth (that comes from the capture state).
/// </summary>
public sealed class MeasurementOrder : IComparer<MeasurementResult>
{
    public static MeasurementOrder Instance { get; } = new();

    public int Compare(MeasurementResult? x, MeasurementResult? y)
    {
        if (ReferenceEquals(x, y)) return 0;
        if (x is null) return -1;
        if (y is null) return 1;

        var byFrom = (x.FromMm, y.FromMm) switch
        {
            ({ } a, { } b) => a.CompareTo(b),
            (null, null) => 0,
            (null, _) => 1,
            (_, null) => -1
        };

        if (byFrom != 0) return byFrom;

        var byTime = x.StartTime.CompareTo(y.StartTime);

        if (byTime != 0) return byTime;

        var byRecord = (x.RecordId, y.RecordId) switch
        {
            ({ } a, { } b) => a.CompareTo(b),
            (null, null) => 0,
            (null, _) => 1,
            (_, null) => -1
        };

        return byRecord != 0 ? byRecord : x.CaptureNumber.CompareTo(y.CaptureNumber);
    }

    /// <summary>Rows in display order (a stable sort of a copy).</summary>
    public static List<MeasurementResult> Sort(IEnumerable<MeasurementResult> rows) => rows.Order(Instance).ToList();

    /// <summary>Position at which <paramref name="row"/> belongs in an already sorted list (after equal rows).</summary>
    public static int InsertIndex(IList<MeasurementResult> sorted, MeasurementResult row)
    {
        int low = 0, high = sorted.Count;

        while (low < high)
        {
            var middle = (low + high) / 2;

            if (Instance.Compare(sorted[middle], row) <= 0)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        return low;
    }
}
