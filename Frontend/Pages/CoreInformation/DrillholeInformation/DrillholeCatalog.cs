using Frontend.Api.CoreInformation.Drillholes;
using Frontend.Pages.Measurement;

namespace Frontend.Pages.CoreInformation.DrillholeInformation;

/// <summary>Filtered dropdown entries and the closest match to highlight (null when nothing is being searched).</summary>
public sealed record DrillholeOptions(IReadOnlyList<DrillholeOption> Entries, DrillholeOption? Closest);

/// <summary>
/// Immutable, indexed drillhole list (safe to filter on any thread). Option objects are reused from the previous
/// catalog for unchanged drillholes, so a refresh with the same data changes nothing in the dropdown.
/// </summary>
public sealed class DrillholeCatalog
{
    private readonly Dictionary<string, DrillholeResponse> byId;
    private readonly Dictionary<string, DrillholeOption> options;
    private readonly List<DrillholeResponse> sorted;

    private DrillholeCatalog(List<DrillholeResponse> remote, List<DrillholeResponse> all,
        Dictionary<string, DrillholeResponse> byId, Dictionary<string, DrillholeOption> options, List<DrillholeResponse> sorted)
    {
        Remote = remote;
        All = all;
        this.byId = byId;
        this.options = options;
        this.sorted = sorted;
    }

    public static DrillholeCatalog Empty { get; } = Build([], null);

    /// <summary>Every drillhole: the backend (or cached) list, then the ones created on this computer.</summary>
    public IReadOnlyList<DrillholeResponse> All { get; }

    /// <summary>The backend (or cached) list only, without the drillholes created on this computer.</summary>
    public IReadOnlyList<DrillholeResponse> Remote { get; }

    public int Count => All.Count;

    /// <summary>
    /// Builds the catalog from the backend (or cached) <paramref name="list"/> and the drillholes created on this
    /// computer (<paramref name="local"/>). A local drillhole whose Hole ID is also in the backend list is listed once,
    /// as the backend entry; the others are marked <see cref="DrillholeOption.IsLocal"/>.
    /// </summary>
    public static DrillholeCatalog Build(IEnumerable<DrillholeResponse> list, DrillholeCatalog? previous,
        IEnumerable<DrillholeResponse>? local = null)
    {
        // Entries without a Hole ID cannot be selected or saved, so they are left out; duplicates are listed once.
        static List<DrillholeResponse> Clean(IEnumerable<DrillholeResponse> source) => source
            .Where(d => !string.IsNullOrWhiteSpace(d.HoleId))
            .Select(d => d with { HoleId = d.HoleId!.Trim() })
            .DistinctBy(d => d.HoleId!, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var remote = Clean(list);
        var remoteIds = new HashSet<string>(remote.Select(d => d.HoleId!), StringComparer.OrdinalIgnoreCase);
        var all = remote.Concat(Clean(local ?? []).Where(d => !remoteIds.Contains(d.HoleId!))).ToList();
        var byId = all.ToDictionary(d => d.HoleId!, StringComparer.OrdinalIgnoreCase);
        var options = new Dictionary<string, DrillholeOption>(StringComparer.OrdinalIgnoreCase);

        foreach (var drillhole in all)
        {
            var isLocal = !remoteIds.Contains(drillhole.HoleId!);
            options[drillhole.HoleId!] = previous?.options.GetValueOrDefault(drillhole.HoleId!) is { } same &&
                                         same.Drillhole == drillhole && same.IsLocal == isLocal
                ? same
                : DrillholeOption.Item(drillhole, isLocal);
        }

        var sorted = all.OrderBy(d => d.HoleId, StringComparer.OrdinalIgnoreCase).ToList();
        return new DrillholeCatalog(remote, all, byId, options, sorted);
    }

    public DrillholeResponse? Find(string holeId) =>
        holeId.Length > 0 && byId.TryGetValue(holeId.Trim(), out var found) ? found : null;

    public DrillholeOption? Option(string holeId) =>
        holeId.Length > 0 && options.TryGetValue(holeId.Trim(), out var found) ? found : null;

    /// <summary>
    /// Entries whose Hole ID contains <paramref name="text"/>: measured drillholes first, then the rest A–Z. While
    /// searching, each group is ranked exact, prefix, partial.
    /// </summary>
    public DrillholeOptions Options(IReadOnlyList<MeasuredDrillhole> history, string text, string picked)
    {
        var showAll = text.Length == 0 || string.Equals(text, picked, StringComparison.OrdinalIgnoreCase);
        bool Matches(DrillholeResponse d) => showAll || d.HoleId!.Contains(text, StringComparison.OrdinalIgnoreCase);

        // 0 exact, 1 prefix, 2 partial (only called for matches).
        int Rank(DrillholeResponse d) =>
            string.Equals(d.HoleId, text, StringComparison.OrdinalIgnoreCase) ? 0 :
            d.HoleId!.StartsWith(text, StringComparison.OrdinalIgnoreCase) ? 1 : 2;

        IEnumerable<DrillholeResponse> Ordered(IEnumerable<DrillholeResponse> matches) => showAll ? matches : matches.OrderBy(Rank);

        var recent = new List<DrillholeResponse>();
        var recentIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in history)
        {
            if (Find(entry.DrillholeId) is { } drillhole && recentIds.Add(drillhole.HoleId!))
            {
                recent.Add(drillhole);
            }
        }

        var result = new List<DrillholeOption>();
        var measuredMatches = Ordered(recent.Where(Matches)).ToList();

        if (measuredMatches.Count > 0)
        {
            result.Add(DrillholeOption.MeasuredHeader);
            result.AddRange(measuredMatches.Select(d => options[d.HoleId!]));
        }

        var otherMatches = Ordered(sorted.Where(d => !recentIds.Contains(d.HoleId!) && Matches(d))).ToList();

        if (otherMatches.Count > 0)
        {
            result.Add(result.Count > 0 ? DrillholeOption.AllHeaderAfterMeasured : DrillholeOption.AllHeader);
            result.AddRange(otherMatches.Select(d => options[d.HoleId!]));
        }

        // Closest: the best-ranked match; on a tie the one listed first (Measured drillholes before All drillholes).
        DrillholeOption? closest = null;

        if (!showAll)
        {
            var best = int.MaxValue;

            foreach (var drillhole in measuredMatches.Concat(otherMatches))
            {
                var rank = Rank(drillhole);

                if (rank < best)
                {
                    best = rank;
                    closest = options[drillhole.HoleId!];
                }
            }
        }

        return new DrillholeOptions(result, closest);
    }
}
