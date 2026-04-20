using System.Globalization;
using System.IO;
using System.Text;
using M365TfsSync.Models;

namespace M365TfsSync.Services;

/// <summary>
/// 解析 Outlook 中文版匯出的 CSV 行事曆檔案
/// 編碼：Big5（繁體中文 Windows 預設）
/// 時間格式：上午/下午 hh:mm:ss（12 小時制）
/// </summary>
public static class CsvParser
{
    public static IReadOnlyList<CalendarEvent> Parse(string filePath)
    {
        // 先嘗試讀取 BOM，判斷是 UTF-8 還是 Big5
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var encoding = DetectEncoding(filePath);

        var lines = File.ReadAllLines(filePath, encoding);
        if (lines.Length < 2)
            return Array.Empty<CalendarEvent>();

        // 解析標題列，找出各欄位索引
        var headers = SplitCsvLine(lines[0]);
        int idxSubject   = FindIndex(headers, "主旨");
        int idxStartDate = FindIndex(headers, "開始日期");
        int idxStartTime = FindIndex(headers, "開始時間");
        int idxEndDate   = FindIndex(headers, "結束日期");
        int idxEndTime   = FindIndex(headers, "結束時間");

        if (idxSubject < 0 || idxStartDate < 0 || idxStartTime < 0 ||
            idxEndDate < 0 || idxEndTime < 0)
            throw new InvalidOperationException(
                "CSV 格式不符：找不到必要欄位（主旨、開始日期、開始時間、結束日期、結束時間）");

        var events = new List<CalendarEvent>();

        for (int i = 1; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (string.IsNullOrEmpty(line)) continue;

            var cols = SplitCsvLine(line);
            if (cols.Count <= Math.Max(idxEndDate, idxEndTime)) continue;

            var subject   = cols[idxSubject].Trim().Trim('"');
            var startDate = cols[idxStartDate].Trim().Trim('"');
            var startTime = cols[idxStartTime].Trim().Trim('"');
            var endDate   = cols[idxEndDate].Trim().Trim('"');
            var endTime   = cols[idxEndTime].Trim().Trim('"');

            var start = ParseOutlookDateTime(startDate, startTime);
            var end   = ParseOutlookDateTime(endDate, endTime);

            System.Diagnostics.Debug.WriteLine(
                $"[CsvParser] {subject} | startDate={startDate} startTime={startTime} → {start:yyyy/MM/dd HH:mm}");
            System.Diagnostics.Debug.WriteLine(
                $"[CsvParser] {subject} | endDate={endDate} endTime={endTime} → {end:yyyy/MM/dd HH:mm}");

            if (start == DateTime.MinValue) continue;
            // 若結束時間解析失敗，預設為開始時間 + 1 小時
            if (end == DateTime.MinValue || end <= start)
                end = start.AddHours(1);

            events.Add(new CalendarEvent
            {
                Id = Guid.NewGuid().ToString(),
                Subject = subject,
                StartTime = start,
                EndTime = end
            });
        }

        return events;
    }

    /// <summary>
    /// 解析 Outlook 中文版的日期 + 時間欄位
    /// 日期格式：yyyy/M/d 或 yyyy-M-d
    /// 時間格式：上午 hh:mm:ss 或 下午 hh:mm:ss（12 小時制）
    /// 策略：直接 split 空格取最後一段，忽略前面的上午/下午文字
    /// </summary>
    private static DateTime ParseOutlookDateTime(string datePart, string timePart)
    {
        // 解析日期
        if (!DateTime.TryParse(datePart, CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var date))
        {
            if (!DateTime.TryParse(datePart, new CultureInfo("zh-TW"),
                DateTimeStyles.None, out date))
                return DateTime.MinValue;
        }

        // 直接 split 空格，取最後一段（hh:mm:ss），忽略前面的上午/下午
        var parts = timePart.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var timeStr = parts[^1]; // 最後一段一定是時間數字

        if (!TimeSpan.TryParse(timeStr, out var time))
            return DateTime.MinValue;

        return date.Date + time;
    }

    /// <summary>簡易 CSV 行解析，支援引號包覆的欄位</summary>
    private static List<string> SplitCsvLine(string line)
    {
        var result = new List<string>();
        var current = new StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '"')
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
            else if (c == ',' && !inQuotes)
            {
                result.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }
        result.Add(current.ToString());
        return result;
    }

    private static int FindIndex(List<string> headers, string name)
    {
        for (int i = 0; i < headers.Count; i++)
            if (headers[i].Trim().Trim('"').Equals(name, StringComparison.OrdinalIgnoreCase))
                return i;
        return -1;
    }

    /// <summary>
    /// 偵測檔案編碼：有 UTF-8 BOM 用 UTF-8，否則用 Big5
    /// </summary>
    private static Encoding DetectEncoding(string filePath)
    {
        using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read);
        var bom = new byte[3];
        fs.Read(bom, 0, 3);

        // UTF-8 BOM: EF BB BF
        if (bom[0] == 0xEF && bom[1] == 0xBB && bom[2] == 0xBF)
            return new UTF8Encoding(true);

        // 預設 Big5
        return Encoding.GetEncoding("big5");
    }
}
