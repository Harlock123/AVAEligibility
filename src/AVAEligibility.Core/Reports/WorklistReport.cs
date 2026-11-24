using System.Net;
using System.Text;
using AVAEligibility.Core.Eligibility;
using AVAEligibility.Core.Inquiries;
using ClosedXML.Excel;

namespace AVAEligibility.Core.Reports;

/// <summary>
/// The worklist as a page to print or a workbook to sort: how many visits are ready and how many need something, then
/// every visit with its answer, what to do, and the estimate. <paramref name="mask"/> (on the methods) masks people's
/// details for anyone who mustn't see them; the person's own report shows them.
/// </summary>
public static class WorklistReport
{
    private static string Who(WorkItem w, Func<string, string> mask) => mask(w.Appointment.SortName);

    public static string Html(IReadOnlyList<WorkItem> items, string title, DateTime made, Func<string, string>? mask = null)
    {
        mask ??= s => s;
        static string H(string s) => WebUtility.HtmlEncode(s);
        var b = new StringBuilder();
        b.Append("<!doctype html><html><head><meta charset=\"utf-8\"><title>").Append(H(title)).Append("</title><style>")
         .Append("body{font:14px system-ui,sans-serif;margin:24px;color:#1d2330}h1{margin:0 0 4px}small{color:#667}")
         .Append("table{border-collapse:collapse;width:100%;margin-top:14px}th,td{text-align:left;vertical-align:top;padding:6px 8px;border-bottom:1px solid #dde}")
         .Append("th{background:#f3f5f8}.ready{color:#1f7a3a}.check{color:#8a6d00}.act{color:#b4531a}.rejected,.no{color:#b3261e}.fix{color:#556;font-size:13px}")
         .Append(".counts span{display:inline-block;margin-right:16px}</style></head><body>");
        b.Append("<h1>").Append(H(title)).Append("</h1><small>Made ").Append(made.ToString("yyyy-MM-dd HH:mm")).Append(" by AVA Eligibility. Estimates are estimates.</small>");
        b.Append("<p class=\"counts\">");
        foreach (var g in items.GroupBy(i => i.Word)) b.Append("<span><b>").Append(g.Count()).Append("</b> ").Append(H(g.Key)).Append("</span>");
        b.Append("</p><table><tr><th>When</th><th>Patient</th><th>Payer</th><th>Visit</th><th>Answer</th><th>What to do</th><th>Estimate</th></tr>");
        foreach (var w in items)
        {
            var a = w.Appointment;
            var css = w.Word.Split(' ')[0];
            b.Append("<tr><td>").Append(H($"{a.Date:yyyy-MM-dd} {a.Time}")).Append("</td><td>").Append(H(Who(w, mask))).Append("</td><td>")
             .Append(H(a.SelfPay ? "self-pay" : $"{a.Payer} {mask(a.MemberId)}")).Append("</td><td>").Append(H($"{a.Visit} {a.Procedure}")).Append("</td><td class=\"")
             .Append(css).Append("\"><b>").Append(H(w.Word)).Append("</b></td><td>");
            foreach (var n in w.Notes.OrderByDescending(n => n.Level))
                b.Append("<div>").Append(H(mask(n.Text))).Append("<div class=\"fix\">").Append(H(n.Fix)).Append("</div></div>");
            b.Append("</td><td>").Append(w.Estimate is { } e ? H($"{Words.Money(e.Amount)}") + "<div class=\"fix\">" + H(e.Basis) + "</div>" : "").Append("</td></tr>");
        }
        b.Append("</table><p><small>Estimates assume the provider is in network and the payer allows the full charge; the payer's own adjudication decides.</small></p></body></html>");
        return b.ToString();
    }

    public static void Excel(string path, IReadOnlyList<WorkItem> items, Func<string, string>? mask = null)
    {
        mask ??= s => s;
        using var book = new XLWorkbook();
        var sheet = book.AddWorksheet("Worklist");
        string[] heads = ["Date", "Time", "Patient", "Payer", "Member ID", "Visit", "Procedure", "Answer", "What it says", "What to do", "Estimate", "Estimate basis"];
        for (var i = 0; i < heads.Length; i++) sheet.Cell(1, i + 1).Value = heads[i];
        sheet.Row(1).Style.Font.Bold = true;
        var r = 2;
        foreach (var w in items)
        {
            var a = w.Appointment;
            var top = w.Notes.OrderByDescending(n => n.Level).FirstOrDefault();
            sheet.Cell(r, 1).Value = a.Date?.ToString("yyyy-MM-dd") ?? "";
            sheet.Cell(r, 2).Value = a.Time;
            sheet.Cell(r, 3).Value = Who(w, mask);
            sheet.Cell(r, 4).Value = a.SelfPay ? "self-pay" : a.Payer;
            sheet.Cell(r, 5).Value = mask(a.MemberId);
            sheet.Cell(r, 6).Value = a.Visit;
            sheet.Cell(r, 7).Value = a.Procedure;
            sheet.Cell(r, 8).Value = w.Word;
            sheet.Cell(r, 9).Value = string.Join(" ", w.Notes.OrderByDescending(n => n.Level).Select(n => mask(n.Text)));
            sheet.Cell(r, 10).Value = top?.Fix ?? "";
            if (w.Estimate is { } e)
            {
                sheet.Cell(r, 11).Value = e.Amount;
                sheet.Cell(r, 11).Style.NumberFormat.Format = "$#,##0.00";
                sheet.Cell(r, 12).Value = e.Basis;
            }
            r++;
        }
        sheet.Columns().AdjustToContents(1, Math.Min(r, 60), 8, 60);
        sheet.SheetView.FreezeRows(1);
        book.SaveAs(path);
    }
}
