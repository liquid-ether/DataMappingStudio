using App.Application.Provisioning;
using App.Domain.Data;

namespace App.Application.Tests.Provisioning;

public class SampleDataTests
{
    private static readonly SampleDataSet Data = SampleData.Build();

    [Fact]
    public void Produces_the_requested_row_counts()
    {
        Assert.Equal(10, Data.Applications.Count);
        Assert.Equal(100, Data.DataSources.Count);
        Assert.Equal(5000, Data.DictionaryEntries.Count);
        Assert.Equal(30, Data.MappingTargets.Count);

        Assert.Equal(5, Data.Classifications.Count);
        Assert.NotEmpty(Data.LookupValues);
        Assert.NotEmpty(Data.Rules);
        Assert.NotEmpty(Data.MappingSources);
        Assert.NotEmpty(Data.Mappings);
    }

    [Fact]
    public void Dictionary_entries_are_distributed_5_to_150_per_source()
    {
        List<int> perSource = Data.DictionaryEntries
            .GroupBy(e => e["source_id"])
            .Select(g => g.Count())
            .ToList();

        Assert.Equal(100, perSource.Count);                  // every source has entries
        Assert.All(perSource, c => Assert.InRange(c, 5, 150));
        Assert.Equal(5000, perSource.Sum());
    }

    [Fact]
    public void Lineage_is_at_most_five_targets_deep_and_acyclic()
    {
        HashSet<string> targets = Data.MappingTargets.Select(t => t["name"]!).ToHashSet();
        Dictionary<string, List<string>> upstream = Data.MappingSources
            .Where(s => s["is_target"] == "true")
            .GroupBy(s => s["target"]!)
            .ToDictionary(g => g.Key, g => g.Select(s => s["source_name"]!).ToList());

        Dictionary<string, int> memo = [];

        int Depth(string node, HashSet<string> stack)
        {
            if (memo.TryGetValue(node, out int cached))
            {
                return cached;
            }

            Assert.True(stack.Add(node), $"lineage cycle through {node}");
            int best = 1;
            if (upstream.TryGetValue(node, out List<string>? ups))
            {
                foreach (string u in ups.Where(targets.Contains))
                {
                    best = Math.Max(best, 1 + Depth(u, stack));
                }
            }

            stack.Remove(node);
            memo[node] = best;
            return best;
        }

        int maxDepth = targets.Max(t => Depth(t, []));
        Assert.Equal(5, maxDepth);
    }

    [Fact]
    public void References_point_at_seeded_rows()
    {
        HashSet<string?> appIds = Data.Applications.Select(a => (string?)a.Id.ToString()).ToHashSet();
        HashSet<string?> sourceIds = Data.DataSources.Select(s => (string?)s.Id.ToString()).ToHashSet();
        HashSet<string?> classIds = Data.Classifications.Select(c => (string?)c.Id.ToString()).ToHashSet();

        Assert.All(Data.DataSources, s => Assert.Contains(s["application_id"], appIds));
        Assert.All(Data.DictionaryEntries, e =>
        {
            Assert.Contains(e["source_id"], sourceIds);
            Assert.Contains(e["classification_id"], classIds);
        });
    }

    [Fact]
    public void Is_deterministic_for_a_given_seed()
    {
        SampleDataSet a = SampleData.Build(99);
        SampleDataSet b = SampleData.Build(99);

        // Random choices (distribution + data types) reproduce; identities differ per build.
        Assert.Equal(
            a.DictionaryEntries.GroupBy(e => e["source_id"]).Select(g => g.Count()).OrderBy(c => c),
            b.DictionaryEntries.GroupBy(e => e["source_id"]).Select(g => g.Count()).OrderBy(c => c));
        Assert.Equal(
            a.DictionaryEntries.Select(e => e["data_type"]),
            b.DictionaryEntries.Select(e => e["data_type"]));
    }
}
