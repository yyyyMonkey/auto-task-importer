using System.Globalization;
using System.IO;
using System.Text;
using M365TfsSync.Models;

namespace M365TfsSync.Services;

/// <summary>
/// 解析由 Outlook（繁體中文環境）匯出的 CSV 行事曆檔案。
///
/// Outlook 繁體中文版匯出的 CSV 標頭欄位（固定順序）：
///   主旨, 開始日期, 開始時間, 結束日期, 結束時間, 全天事件, 提醒日期, 提醒時間, 提醒, 會議召集人, 必要出席者, 選擇性出席者, 會議資源, 計費資訊, 說明, 優先順序, 私用, 敏感度, 顯示為
///
/// 本解析器只需要「主旨」、「開始日期」、「開始時間」、「結束日期」、「結束時間」這五個欄位。
/// 欄位順序以標頭列為準，位置不固定。
/// </summary>
public static class CsvParser
{
    // Outlook 繁體中文標頭對應
    private static readonly string[] SubjectHeaders   = ["主旨", "Subject"];
    private static readonly string[] StartDateHeaders = ["開始日期", "Start Date"];
    private static readonly string[] StartTimeHeaders = ["開始時間", "Start Time"];
    private static readonly string[] EndDateHeaders   = ["結束日期", "End Date"];
    private static readonly string[] EndTimeHeaders   = ["結束時間", "End Time"];

    // Outlook 匯出的日期格式（繁體中文 Windows 地區設定）
    private static readonly string[] DateFormats =
    [
        "yyyy/M/d",
        "yyyy/MM/dd",
        "yyyy-MM-dd",
        "M/d/yyyy",
        "MM/dd/yyyy",
    ];

    // Outlook 匯出的時間格式
    private static readonly string[] TimeFormats =
    [
        "H:mm:ss",
        "HH:mm:ss",
        "H:mm",
        "HH:mm",
        "h:mm tt",    // 12 小時制（英文 AM/PM）
        "hh:mm tt",
    ];

    public static IReadOnlyList<CalendarEvent> Parse(string filePath)
    {
        // Outlook 繁體中文 CSV 以 UTF-8 with BOM 或 Big5 儲存；先嘗試 UTF-8，再 fallback
        var lines = ReadLines(filePath);
        if (lines.Length == 0)
            return Array.Empty<CalendarEvent>();

        // 第一列必須是標頭列，找出各欄位索引
        var header = SplitCsvLine(lines[0]);
        var colSubject   = FindColumn(header, SubjectHeaders);
        var colStartDate = FindColumn(header, StartDateHeaders);
        var colStartTime = FindColumn(header, StartTimeHeaders);
        var colEndDate   = FindColumn(header, EndDateHeaders);
        var colEndTime   = FindColumn(header, EndTimeHeaders);

        if (colSubject < 0 || colStartDate < 0 || colStartTime < 0 ||
            colEndDate < 0 || colEndTime < 0)
            throw new FormatException(
                "CSV 檔案格式不符，請確認是否為 Outlook 繁體中文版匯出的行事曆 CSV。\n" +
                "必要欄位：主旨、開始日期、開始時間、結束日期、結束時間");

        var events = new List<CalendarEvent>();

        for (var i = 1; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (string.IsNullOrWhiteSpace(line)) continue;

            var fields = SplitCsvLine(line);
            var maxIdx = new[] { colSubject, colStartDate, colStartTime, colEndDate, colEndTime }.Max();
            if (fields.Count <= maxIdx) continue;

            var subject = fields[colSubject].Trim().Trim('"');
            if (string.IsNullOrWhiteSpace(subject)) continue;

            if (!TryParseDate(fields[colStartDate], out var startDate)) continue;
            if (!TryParseDate(fields[colEndDate],   out var endDate))   continue;

            TryParseTime(fields[colStartTime], out var startTime);
            TryParseTime(fields[colEndTime],   out var endTime);

            var startDateTime = startDate.Add(startTime);
            var endDateTime   = endDate.Add(endTime);

            // 防呆：若結束時間不晚於開始時間，補 1 小時
            if (endDateTime <= startDateTime)
                endDateTime = startDateTime.AddHours(1);

            events.Add(new CalendarEvent
            {
                Id        = Guid.NewGuid().ToString(),
                Subject   = subject,
                StartTime = startDateTime,
                EndTime   = endDateTime,
            });
        }

        return events;
    }

    // ── 私有輔助 ────────────────────────────────────────────────────────────

    private static string[] ReadLines(string filePath)
    {
        // 嘗試 UTF-8（含 BOM），若失敗改用 Big5（950）
        try
        {
            return File.ReadAllLines(filePath, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        }
        catch
        {
            return File.ReadAllLines(filePath, Encoding.GetEncoding(950));
        }
    }

    private static int FindColumn(List<string> header, string[] candidates)
    {
        for (var i = 0; i < header.Count; i++)
        {
            var cell = header[i].Trim().Trim('"');
            foreach (var candidate in candidates)
                if (string.Equals(cell, candidate, StringComparison.OrdinalIgnoreCase))
                    return i;
        }
        return -1;
    }

    /// <summary>支援欄位內含逗號（以雙引號包圍）的 RFC 4180 CSV 分割</summary>
    private static List<string> SplitCsvLine(string line)
    {
        var fields  = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            if (ch == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (ch == ',' && !inQuotes)
            {
                fields.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(ch);
            }
        }

        fields.Add(current.ToString());
        return fields;
    }

    private static bool TryParseDate(string value, out DateTime result)
    {
        var clean = value.Trim().Trim('"');
        return DateTime.TryParseExact(
            clean, DateFormats,
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out result);
    }

    private static bool TryParseTime(string value, out TimeSpan result)
    {
        var clean = value.Trim().Trim('"');

        // 嘗試 AM/PM 格式（需要 en-US 文化）
        if (DateTime.TryParseExact(clean, TimeFormats,
            CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
        {
            result = dt.TimeOfDay;
            return true;
        }

        // 全天事件：時間欄位可能為空
        result = TimeSpan.Zero;
        return false;
    }
}
