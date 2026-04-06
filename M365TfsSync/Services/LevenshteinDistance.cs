namespace M365TfsSync.Services;

public static class LevenshteinDistance
{
    /// <summary>
    /// 計算兩個字串的 Levenshtein 編輯距離（忽略大小寫）
    /// </summary>
    public static int Compute(string s1, string s2)
    {
        s1 = (s1 ?? string.Empty).ToLowerInvariant();
        s2 = (s2 ?? string.Empty).ToLowerInvariant();

        int m = s1.Length;
        int n = s2.Length;

        if (m == 0) return n;
        if (n == 0) return m;

        var dp = new int[m + 1, n + 1];

        for (int i = 0; i <= m; i++) dp[i, 0] = i;
        for (int j = 0; j <= n; j++) dp[0, j] = j;

        for (int i = 1; i <= m; i++)
        {
            for (int j = 1; j <= n; j++)
            {
                int cost = s1[i - 1] == s2[j - 1] ? 0 : 1;
                dp[i, j] = Math.Min(
                    Math.Min(dp[i - 1, j] + 1,      // 刪除
                             dp[i, j - 1] + 1),      // 插入
                    dp[i - 1, j - 1] + cost           // 替換
                );
            }
        }

        return dp[m, n];
    }

    /// <summary>
    /// 計算兩個字串的相似度百分比（0.0 ~ 100.0）
    /// 公式：(1 - editDistance / max(len(s1), len(s2))) * 100
    /// </summary>
    public static double ComputeSimilarity(string s1, string s2)
    {
        s1 = s1 ?? string.Empty;
        s2 = s2 ?? string.Empty;

        if (s1.Length == 0 && s2.Length == 0)
            return 100.0;

        int maxLen = Math.Max(s1.Length, s2.Length);
        int distance = Compute(s1, s2);
        return (1.0 - (double)distance / maxLen) * 100.0;
    }
}
