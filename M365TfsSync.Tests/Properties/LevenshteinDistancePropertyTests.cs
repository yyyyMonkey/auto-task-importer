using FsCheck;
using FsCheck.Xunit;
using M365TfsSync.Services;

namespace M365TfsSync.Tests.Properties;

// Feature: m365-tfs-calendar-sync, Property 9: Levenshtein 相似度計算正確性
public class LevenshteinDistancePropertyTests
{
    [Property]
    public Property SimilarityIsInRange(string s1, string s2)
    {
        var sim = LevenshteinDistance.ComputeSimilarity(s1 ?? "", s2 ?? "");
        return (sim >= 0.0 && sim <= 100.0).ToProperty();
    }

    [Property]
    public Property SimilarityIsSymmetric(string s1, string s2)
    {
        var sim1 = LevenshteinDistance.ComputeSimilarity(s1 ?? "", s2 ?? "");
        var sim2 = LevenshteinDistance.ComputeSimilarity(s2 ?? "", s1 ?? "");
        return (Math.Abs(sim1 - sim2) < 0.001).ToProperty();
    }

    [Property]
    public Property IdenticalStringsHaveFullSimilarity(string s)
    {
        var sim = LevenshteinDistance.ComputeSimilarity(s ?? "", s ?? "");
        return (Math.Abs(sim - 100.0) < 0.001).ToProperty();
    }
}
