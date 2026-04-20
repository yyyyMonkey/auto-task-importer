using System.Globalization;
using System.IO;
using M365TfsSync.Models;

namespace M365TfsSync.Services;

/// <summary>
/// 解析 iCalendar (.ics) 檔案，將 VEVENT 轉換為 CalendarEvent 清單
/// </summary>
public static class IcsParser
{
    public static IReadOnlyList<CalendarEvent> Parse(string filePath)
    {
        var lines = File.ReadAllLines(filePath);
        // 展開折行（RFC 5545：以空白或 Tab 開頭的行是前一行的延續）
        var unfolded = Unfold(lines);
        return ParseEvents(unfolded);
    }

    private static List<string> Unfold(string[] lines)
    {
        var result = new List<string>();
        foreach (var line in lines)
        {
            if ((line.StartsWith(' ') || line.StartsWith('\t')) && result.Count > 0)
                result[^1] += line.TrimStart();
            else
                result.Add(line);
        }
        return result;
    }

    private static List<CalendarEvent> ParseEvents(List<string> lines)
    {
        var events = new List<CalendarEvent>();
        CalendarEvent? current = null;

        foreach (var line in lines)
        {
            if (line.Equals("BEGIN:VEVENT", StringComparison.OrdinalIgnoreCase))
            {
                current = new CalendarEvent { Id = Guid.NewGuid().ToString() };
                continue;
            }

            if (line.Equals("END:VEVENT", StringComparison.OrdinalIgnoreCase))
            {
                if (current != null)
                    events.Add(current);
                current = null;
                continue;
            }

            if (current == null) continue;

            // 取得屬性名稱（冒號前，忽略參數如 DTSTART;TZID=...）
            var colonIdx = line.IndexOf(':');
            if (colonIdx < 0) continue;

            var propFull = line[..colonIdx];
            var value = line[(colonIdx + 1)..];
            var propName = propFull.Split(';')[0].ToUpperInvariant();

            switch (propName)
            {
                case "SUMMARY":
                    current.Subject = DecodeText(value);
                    break;
                case "UID":
                    current.Id = value;
                    break;
                case "DTSTART":
                    current.StartTime = ParseDateTime(propFull, value);
                    break;
                case "DTEND":
                    current.EndTime = ParseDateTime(propFull, value);
                    break;
            }
        }

        return events;
    }

    private static DateTime ParseDateTime(string propFull, string value)
    {
        // 取出 TZID 參數（若有）
        var tzid = string.Empty;
        foreach (var param in propFull.Split(';').Skip(1))
        {
            if (param.StartsWith("TZID=", StringComparison.OrdinalIgnoreCase))
                tzid = param[5..];
        }

        System.Diagnostics.Debug.WriteLine($"[IcsParser] propFull={propFull} | raw value={value} | tzid={tzid}");

        // 移除尾端 Z（UTC 標記）
        var isUtc = value.EndsWith('Z');

        // 只移除 T 分隔符（位於日期與時間之間的固定位置第 9 個字元）
        // 避免 Replace("T","") 誤刪其他位置的 T
        var clean = value.TrimEnd('Z');
        if (clean.Length >= 9 && clean[8] == 'T')
            clean = clean[..8] + clean[9..];

        System.Diagnostics.Debug.WriteLine($"[IcsParser] clean={clean} | isUtc={isUtc}");

        if (!DateTime.TryParseExact(clean,
            new[] { "yyyyMMddHHmmss", "yyyyMMdd" },
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var dt))
        {
            System.Diagnostics.Debug.WriteLine($"[IcsParser] 解析失敗: clean={clean}");
            return DateTime.MinValue;
        }

        if (isUtc)
            return DateTime.SpecifyKind(dt, DateTimeKind.Utc).ToLocalTime();

        // 有 TZID 且是 Windows 時區名稱，嘗試轉換
        if (!string.IsNullOrEmpty(tzid))
        {
            try
            {
                var tz = TimeZoneInfo.FindSystemTimeZoneById(tzid.Trim('"'));
                return TimeZoneInfo.ConvertTimeToUtc(dt, tz).ToLocalTime();
            }
            catch
            {
                // 找不到時區就直接用原始時間
            }
        }

        return dt;
    }

    private static string DecodeText(string value)
    {
        // RFC 5545 跳脫字元還原
        return value
            .Replace("\\n", "\n")
            .Replace("\\N", "\n")
            .Replace("\\,", ",")
            .Replace("\\;", ";")
            .Replace("\\\\", "\\");
    }
}
