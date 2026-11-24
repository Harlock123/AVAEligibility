using System.Globalization;
using ClosedXML.Excel;

namespace AVAEligibility.Core.Inquiries;

/// <summary>One visit on the schedule: who, when, with whom, for what, and the insurance on file.</summary>
public sealed record Appointment
{
    /// <summary>The row in the list, from 1 (the header is row 0), so problems can say where.</summary>
    public int Row { get; init; }
    public DateOnly? Date { get; init; }
    public string Time { get; init; } = "";
    public string Last { get; init; } = "";
    public string First { get; init; } = "";
    public string Middle { get; init; } = "";
    public DateOnly? BirthDate { get; init; }
    public string Sex { get; init; } = "";
    public string PayerId { get; init; } = "";
    public string Payer { get; init; } = "";
    public string MemberId { get; init; } = "";

    /// <summary>"self", or the subscriber as "LAST, FIRST" when the patient is a dependent.</summary>
    public string Subscriber { get; init; } = "";
    public string Provider { get; init; } = "";
    public string ProviderNpi { get; init; } = "";
    public string Visit { get; init; } = "";
    public string Procedure { get; init; } = "";
    public decimal? Charge { get; init; }

    /// <summary>What the list had that didn't read (a date that isn't one), by column, so checks can say so.</summary>
    public IReadOnlyList<string> Unread { get; init; } = [];

    public bool SelfPay => PayerId.Length == 0 && MemberId.Length == 0;
    public bool IsDependent => Subscriber.Length > 0 && !Subscriber.Equals("self", StringComparison.OrdinalIgnoreCase);
    public string SortName => First.Length > 0 ? $"{Last}, {First}" : Last;

    /// <summary>The subscriber's last and first names, from "LAST, FIRST".</summary>
    public (string Last, string First) SubscriberName
    {
        get
        {
            var parts = Subscriber.Split(',', 2);
            return (parts[0].Trim(), parts.Length > 1 ? parts[1].Trim() : "");
        }
    }
}

/// <summary>
/// Reads an appointment list: a CSV or an Excel workbook (its first sheet) with a header row. Columns are found by
/// their names, in any order and case ("Birth date", "DOB" and "Date of birth" all work); unknown columns are ignored.
/// </summary>
public static class AppointmentList
{
    private static readonly Dictionary<string, string[]> Names = new()
    {
        ["date"] = ["date", "visit date", "appointment date", "date of service", "dos"],
        ["time"] = ["time", "appointment time"],
        ["last"] = ["last name", "last", "surname", "family name", "patient last name"],
        ["first"] = ["first name", "first", "given name", "patient first name"],
        ["middle"] = ["middle", "middle name", "middle initial", "mi"],
        ["dob"] = ["birth date", "dob", "date of birth", "birthdate"],
        ["sex"] = ["sex", "gender"],
        ["payerid"] = ["payer id", "payerid", "payer code"],
        ["payer"] = ["payer", "payer name", "insurance", "plan"],
        ["member"] = ["member id", "member", "subscriber id", "insurance id", "policy number"],
        ["subscriber"] = ["subscriber", "insured", "relationship"],
        ["provider"] = ["provider", "rendering provider", "doctor"],
        ["npi"] = ["provider npi", "npi", "rendering npi"],
        ["visit"] = ["visit", "visit type", "reason", "appointment type"],
        ["procedure"] = ["procedure", "cpt", "procedure code", "code"],
        ["charge"] = ["charge", "fee", "amount"],
    };

    private static readonly string[] DateFormats = ["yyyy-MM-dd", "M/d/yyyy", "MM/dd/yyyy", "yyyyMMdd", "M/d/yy"];

    public static bool LooksLikeOne(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext is not (".csv" or ".xlsx")) return false;
        try
        {
            var header = ext == ".csv" ? File.ReadLines(path).FirstOrDefault() ?? "" : string.Join(",", ReadExcel(path).FirstOrDefault() ?? []);
            var cells = SplitCsv(header).Select(Key).ToList();
            return cells.Contains("last") && (cells.Contains("member") || cells.Contains("payer"));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or FormatException)
        {
            return false;
        }
    }

    public static IReadOnlyList<Appointment> Load(string path) =>
        Read(Path.GetExtension(path).Equals(".xlsx", StringComparison.OrdinalIgnoreCase) ? ReadExcel(path) : File.ReadLines(path).Where(l => l.Trim().Length > 0).Select(SplitCsv).ToList());

    /// <summary>Reads rows whose first is the header.</summary>
    public static IReadOnlyList<Appointment> Read(IReadOnlyList<IReadOnlyList<string>> rows)
    {
        if (rows.Count == 0) return [];
        var columns = new Dictionary<string, int>();
        for (var i = 0; i < rows[0].Count; i++)
            if (Key(rows[0][i]) is { } k && !columns.ContainsKey(k)) columns[k] = i;
        var list = new List<Appointment>();
        for (var r = 1; r < rows.Count; r++)
        {
            var row = rows[r];
            var unread = new List<string>();
            string Get(string key) => columns.TryGetValue(key, out var i) && i < row.Count ? row[i].Trim() : "";
            DateOnly? Date(string key, string label)
            {
                var v = Get(key);
                if (v.Length == 0) return null;
                if (DateOnly.TryParseExact(v, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)) return d;
                if (DateTime.TryParse(v, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt)) return DateOnly.FromDateTime(dt);
                unread.Add($"{label} \"{v}\" isn't a date");
                return null;
            }
            var charge = Get("charge");
            list.Add(new Appointment
            {
                Row = r,
                Date = Date("date", "the visit date"),
                Time = Get("time"),
                Last = Get("last").ToUpperInvariant(),
                First = Get("first").ToUpperInvariant(),
                Middle = Get("middle").ToUpperInvariant(),
                BirthDate = Date("dob", "the birth date"),
                Sex = Get("sex").ToUpperInvariant(),
                PayerId = Get("payerid"),
                Payer = Get("payer").Equals("self-pay", StringComparison.OrdinalIgnoreCase) ? "" : Get("payer"),
                MemberId = Get("member"),
                Subscriber = Get("subscriber").ToUpperInvariant(),
                Provider = Get("provider"),
                ProviderNpi = Get("npi"),
                Visit = Get("visit"),
                Procedure = Get("procedure").ToUpperInvariant(),
                Charge = decimal.TryParse(charge.TrimStart('$'), NumberStyles.Number, CultureInfo.InvariantCulture, out var c) ? c : null,
                Unread = unread,
            });
        }
        return list;
    }

    private static string? Key(string header)
    {
        var h = header.Trim().ToLowerInvariant().Replace('_', ' ');
        return Names.FirstOrDefault(kv => kv.Value.Contains(h)).Key;
    }

    private static List<IReadOnlyList<string>> ReadExcel(string path)
    {
        using var book = new XLWorkbook(path);
        var sheet = book.Worksheets.First();
        var used = sheet.RangeUsed();
        if (used == null) return [];
        return used.Rows().Select(r => (IReadOnlyList<string>)r.Cells().Select(c => c.DataType == XLDataType.DateTime ? c.GetDateTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : c.GetFormattedString()).ToList()).ToList();
    }

    /// <summary>One CSV line into cells: quotes around cells with commas, doubled quotes inside them.</summary>
    public static IReadOnlyList<string> SplitCsv(string line)
    {
        var cells = new List<string>();
        var cell = new System.Text.StringBuilder();
        var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < line.Length && line[i + 1] == '"') { cell.Append('"'); i++; }
                else if (c == '"') quoted = false;
                else cell.Append(c);
            }
            else if (c == '"') quoted = true;
            else if (c == ',') { cells.Add(cell.ToString()); cell.Clear(); }
            else cell.Append(c);
        }
        cells.Add(cell.ToString());
        return cells;
    }
}
