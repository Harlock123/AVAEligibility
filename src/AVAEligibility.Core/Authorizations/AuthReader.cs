using System.Globalization;
using AVAEligibility.Core.Eligibility;
using AVAEligibility.Core.X12;

namespace AVAEligibility.Core.Authorizations;

/// <summary>
/// Reads 278s (005010X217), request or answer, following the HL levels: 20 the UMO (who reviews: the payer or its
/// utilization review), 21 the requester, 22 the subscriber, 23 a dependent, EV the patient event (the review) and SS
/// its services. An AAA at the UMO's, requester's or subscriber's level applies to every review under it; a transaction
/// refused before any event still gives one review, so the refusal is seen.
/// </summary>
public static class AuthReader
{
    /// <summary>Whether a file holds a 278.</summary>
    public static bool LooksLikeOne(string path)
    {
        try
        {
            return X12File.LooksLikeX12(path) && X12File.Load(path).Transactions.Any(t => t.Code == "278");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or FormatException)
        {
            return false;
        }
    }

    public static AuthFile Load(string path) => Parse(File.ReadAllText(path), System.IO.Path.GetFileName(path), path);

    public static AuthFile Parse(string text, string name = "pasted", string? path = null)
    {
        var x12 = X12File.Parse(text);
        var file = new AuthFile { Name = name, Path = path, X12 = x12 };
        foreach (var t in x12.Transactions)
        {
            if (t.Code != "278")
            {
                file.Problems.Add($"Transaction {t.Control} is a {t.Code}, not a 278: it's left out.");
                continue;
            }
            Read(t, x12.Delimiters, file);
        }
        if (x12.Transactions.Count == 0) file.Problems.Add("There's no ST segment: this isn't an X12 file, or it's empty.");
        return file;
    }

    private static void Read(Transaction t, Delimiters d, AuthFile file)
    {
        var bht = t.Body.FirstOrDefault(s => s.Id == "BHT");
        var response = bht?[2] is "11" or "18" or "AT";
        var created = bht == null ? null : Date(bht[4]) is { } day ? (DateTime?)day.ToDateTime(Time(bht[5])) : null;
        AuthReview New() => new() { Source = t, IsResponse = response, Created = created, Reference = bht?[3] ?? "" };

        var level = "";
        string umoName = "", umoId = "", reqName = "", reqId = "", last = "", first = "", member = "", pLast = "", pFirst = "";
        DateOnly? born = null, pBorn = null;
        var above = new List<Rejection>();      // refusals at the UMO, requester or subscriber level
        var context = new List<Segment>();      // the subscriber's and dependent's segments
        AuthReview? review = null;
        var serviceSegments = new List<Segment>();
        string sAction = "", sNumber = "";
        DateOnly? sDate = null;
        Segment? sLine = null;

        void EndService()
        {
            if (review != null && sLine != null)
            {
                var proc = sLine[1].Split(d.Component);
                review.Services.Add(new AuthService(proc.Length > 1 ? proc[1] : proc[0], proc.Length > 2 ? [.. proc.Skip(2).Where(m => m.Length > 0)] : [],
                    Money(sLine[2]), Money(sLine.Id == "SV1" ? sLine[4] : sLine.Id == "SV2" ? sLine[5] : sLine[6]), sDate, sAction, sNumber, [.. serviceSegments]));
            }
            sLine = null;
            sAction = sNumber = "";
            sDate = null;
            serviceSegments.Clear();
        }
        void Finish()
        {
            EndService();
            if (review == null) return;
            review.Rejections.InsertRange(0, above);
            file.Reviews.Add(review);
            review = null;
        }
        AuthReview Start()
        {
            var r = New();
            r.UmoName = umoName; r.UmoId = umoId; r.RequesterName = reqName; r.RequesterId = reqId;
            r.Last = last; r.First = first; r.MemberId = member; r.BirthDate = born;
            r.PatientLast = pLast; r.PatientFirst = pFirst; r.PatientBirthDate = pBorn;
            r.Segments.AddRange(context);
            return r;
        }

        foreach (var s in t.Body)
        {
            if (s.Id == "HL")
            {
                var next = s[3].Trim();
                if (next == "SS")
                {
                    EndService();
                    level = next;
                    serviceSegments.Add(s);
                    review?.Segments.Add(s);
                    continue;
                }
                if (next == "EV") Finish();
                else if (level is "EV" or "SS") Finish();
                level = next;
                if (level is "22") { last = first = member = pLast = pFirst = ""; born = pBorn = null; context.Clear(); }
                if (level is "23") { pLast = pFirst = ""; pBorn = null; }
                if (level == "EV") review = Start();
                (level == "EV" ? review!.Segments : context).Add(s);
                continue;
            }
            if (s.Id == "BHT") continue;
            switch (level)
            {
                case "20":
                    if (s.Id == "NM1") { umoName = Name(s); umoId = s[9]; }
                    else if (s.Id == "AAA") above.Add(Aaa(s, "the reviewer (UMO)"));
                    break;
                case "21":
                    if (s.Id == "NM1") { reqName = Name(s); reqId = s[9]; }
                    else if (s.Id == "AAA") above.Add(Aaa(s, "the requester"));
                    break;
                case "22":
                    context.Add(s);
                    if (s.Id == "NM1") { last = s[3]; first = s[4]; member = s[9]; }
                    else if (s.Id == "DMG") born = Date(s[2]);
                    else if (s.Id == "AAA") above.Add(Aaa(s, "the subscriber"));
                    break;
                case "23":
                    context.Add(s);
                    if (s.Id == "NM1") { pLast = s[3]; pFirst = s[4]; }
                    else if (s.Id == "DMG") pBorn = Date(s[2]);
                    else if (s.Id == "AAA") above.Add(Aaa(s, "the patient"));
                    break;
                case "EV" when review != null:
                    review.Segments.Add(s);
                    switch (s.Id)
                    {
                        case "TRN": review.Trace = s[2]; review.TraceOriginator = s[3]; break;
                        case "UM":
                            review.Category = s[1]; review.CertificationType = s[2]; review.ServiceType = s[3];
                            review.Facility = s[4].Split(d.Component)[0];
                            break;
                        case "HCR": review.Action = s[1]; review.Number = s[2]; review.Reason = s[3]; break;
                        case "REF" when s[1] == "BB" && review.Number.Length == 0: review.Number = s[2]; break;
                        case "DTP": Dates(review, s, event_: true); break;
                        case "HI":
                            for (var i = 1; i < s.Elements.Length; i++)
                                if (s[i].Split(d.Component) is [_, var code, ..] && code.Length > 0) review.Diagnoses.Add(code);
                            break;
                        case "NM1" when s[1] is "SJ" or "FA" or "72": review.ProviderName = Name(s); review.ProviderNpi = s[9]; break;
                        case "AAA": review.Rejections.Add(Aaa(s, "the request")); break;
                    }
                    break;
                case "SS" when review != null:
                    review.Segments.Add(s);
                    serviceSegments.Add(s);
                    switch (s.Id)
                    {
                        case "SV1" or "SV2" or "SV3": sLine = s; break;
                        case "HCR":
                            sAction = s[1]; sNumber = s[2];
                            if (review.Action.Length == 0) { review.Action = s[1]; review.Number = s[2]; review.Reason = s[3]; }
                            break;
                        case "DTP" when s[1] == "472": sDate = Date(s[3].Split('-')[0]); break;
                        case "DTP": Dates(review, s, event_: false); break;
                        case "AAA": review.Rejections.Add(Aaa(s, "a service")); break;
                    }
                    break;
            }
        }
        Finish();
        if (!file.Reviews.Any(r => r.Source == t) && (above.Count > 0 || member.Length > 0))
        {
            var r = Start();
            r.Rejections.AddRange(above);
            file.Reviews.Add(r);
        }
    }

    private static void Dates(AuthReview r, Segment s, bool event_)
    {
        var parts = s[3].Split('-');
        var from = Date(parts[0]);
        var to = parts.Length > 1 ? Date(parts[1]) : null;
        switch (s[1])
        {
            case "AAH" or "472" when event_ && r.EventDate == null: r.EventDate = from; r.EventEnd = to; break;
            case "102": r.Issued ??= from; break;
            case "007": r.Effective ??= from; if (to != null) r.Expires ??= to; break;
            case "036": r.Expires ??= from; break;
        }
    }

    private static Rejection Aaa(Segment s, string where) => new(s[1] == "Y", s[3], s[4], where, s);

    private static string Name(Segment s) => s[2] == "1" && s[4].Length > 0 ? $"{s[4]} {s[3]}" : s[3];

    private static decimal? Money(string v) => decimal.TryParse(v, NumberStyles.Number, CultureInfo.InvariantCulture, out var m) ? m : null;

    private static DateOnly? Date(string v) =>
        DateOnly.TryParseExact(v.Trim(), "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;

    private static TimeOnly Time(string v) =>
        TimeOnly.TryParseExact(v.Trim().PadRight(4, '0')[..4], "HHmm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var t) ? t : TimeOnly.MinValue;
}
