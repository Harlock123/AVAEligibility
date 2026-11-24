using AVAEligibility.Core.Eligibility;
using AVAEligibility.Core.Inquiries;
using ClosedXML.Excel;

namespace AVAEligibility.Tests;

/// <summary>The appointment list read and checked, the 270 built from it, and each question matched to its answer.</summary>
public sealed class InquiryTests
{
    /// <summary>The settings the samples' 270 was made with.</summary>
    internal static readonly InquirySettings SampleSettings = new()
    {
        ProviderName = "Maple Street Family Medicine", ProviderNpi = "1642031870", SenderId = "MAPLESTFM", ReceiverId = "CLEARHOUSE01",
        TraceOriginator = "9MAPLE0001", ReferencePrefix = "MSFM", Usage = "T", ControlNumber = 1001,
    };

    internal static readonly DateTime SampleTime = new(2026, 11, 25, 8, 15, 0);

    private static IReadOnlyList<Appointment> List() => AppointmentList.Load(Samples.Path("appointments.csv"));

    [Fact]
    public void The_appointment_list_reads_by_its_headers()
    {
        var list = List();
        Assert.Equal(13, list.Count);
        Assert.True(AppointmentList.LooksLikeOne(Samples.Path("appointments.csv")));
        var emma = list.Single(a => a.First == "EMMA");
        Assert.Equal((true, ("HARTWELL", "DANIEL"), "BWH400100201", "99393", new DateOnly(2014, 6, 2)), (emma.IsDependent, emma.SubscriberName, emma.MemberId, emma.Procedure, emma.BirthDate));
        Assert.True(list.Single(a => a.First == "NOLAN").SelfPay);
        Assert.Equal(1450.00m, list.Single(a => a.First == "KENJI").Charge);
        var odd = AppointmentList.Read([["DOS", "Surname", "Given name", "DOB", "Insurance ID", "Payer ID"], ["12/1/2026", "Smith", "Jo", "not a date", "X1", "P1"]]);
        Assert.Equal((new DateOnly(2026, 12, 1), "SMITH", "JO", (DateOnly?)null), (odd[0].Date, odd[0].Last, odd[0].First, odd[0].BirthDate));
        Assert.Equal(["the birth date \"not a date\" isn't a date"], odd[0].Unread);
    }

    [Fact]
    public void Checks_catch_a_letter_o_in_a_member_id_before_it_is_sent()
    {
        var checks = InquiryBuilder.Check(List(), new DateOnly(2026, 11, 25));
        Assert.Equal(
        [
            "Row 4 (KOWALCZYK, PETER): Member ID BWH40010O512 has a letter O, I or l among its digits: is it a zero or a one? Check the card.",
            "Row 13 (PRICE, NOLAN): Self-pay: there's no insurance to ask about.",
        ], checks.Select(c => c.ToString()));
        Assert.Equal([CheckLevel.Warning, CheckLevel.Info], checks.Select(c => c.Level));
    }

    [Fact]
    public void Rows_that_cant_be_asked_are_left_out_and_say_why()
    {
        var rows = AppointmentList.Read(
        [
            ["Date", "Last name", "First name", "Birth date", "Payer ID", "Payer", "Member ID", "Subscriber", "Procedure"],
            ["2026-12-01", "BAKER", "AL", "2027-01-01", "P1", "PLAN ONE", "A100", "self", "99213"],
            ["2026-12-01", "", "", "", "P1", "PLAN ONE", "", "self", "99213"],
            ["2026-12-01", "STAR*R", "JO", "1990-01-01", "P1", "PLAN ONE", "A1 02-3", "self", "99213"],
            ["2026-12-01", "CRUZ", "LEO", "2015-05-05", "P1", "PLAN ONE", "A200", "CRUZ, MARIA", "99213"],
            ["2028-12-01", "DIAZ", "RITA", "1980-02-02", "", "", "A300", "self", "99213"],
        ]);
        var built = InquiryBuilder.Build(rows, SampleSettings, SampleTime);
        Assert.Equal(
        [
            "Row 1 (BAKER, AL): The birth date 2027-01-01 is after the visit.",
            "Row 2 (): There's no last name.",
            "Row 2 (): There's no first name: most payers need it to find the patient.",
            "Row 2 (): There's no member ID, and without one the payer needs the name and birth date.",
            "Row 2 (): There's no birth date: payers match on it.",
            "Row 3 (STAR*R, JO): Member ID A1 02-3 has spaces or dashes; it's sent without them.",
            "Row 3 (STAR*R, JO): A name or ID holds a separator character (* ~ : ^ |) that would break the file.",
            "Row 4 (CRUZ, LEO): The subscriber CRUZ, MARIA isn't on the list: the question goes with the subscriber's name and member ID only.",
            "Row 5 (DIAZ, RITA): There's no payer ID: the clearinghouse can't route the question.",
            "Row 5 (DIAZ, RITA): The visit date 2028-12-01 is more than a year from today: payers seldom answer for dates that far out.",
        ], built.Checks.Select(c => c.ToString()));
        Assert.Equal(["CRUZ"], built.Sent.Select(a => a.Last));
        var read = EligibilityReader.Parse(built.Text);
        Assert.Empty(read.Problems);
        var leo = Assert.Single(read.People, p => p.IsDependent);
        Assert.Equal(("MARIA", "A200", "LEO"), (leo.Subscriber!.First, leo.Subscriber.MemberId, leo.First));
        Assert.Throws<InvalidOperationException>(() => InquiryBuilder.Build(rows, SampleSettings with { ProviderNpi = "1642031871" }, SampleTime));
    }

    [Fact]
    public void The_270_built_from_the_list_is_the_samples_270_byte_for_byte()
    {
        var built = InquiryBuilder.Build(List(), SampleSettings, SampleTime);
        Assert.Equal(File.ReadAllText(Samples.Path("clinic-270.x12")), built.Text);
        Assert.Equal(4, built.Transactions);
        Assert.Equal(12, built.Traces.Count);
        Assert.Equal("BW0007", built.Traces.Single(kv => kv.Key.First == "KENJI").Value);
        Assert.Empty(EligibilityReader.Parse(built.Text).Problems);
    }

    [Fact]
    public void An_excel_list_reads_as_the_csv_does()
    {
        var path = Path.Combine(Directory.CreateTempSubdirectory("elig-xlsx-").FullName, "appointments.xlsx");
        using (var book = new XLWorkbook())
        {
            var sheet = book.AddWorksheet("Schedule");
            var lines = File.ReadAllLines(Samples.Path("appointments.csv"));
            for (var r = 0; r < lines.Length; r++)
            {
                var cells = AppointmentList.SplitCsv(lines[r]);
                for (var c = 0; c < cells.Count; c++) sheet.Cell(r + 1, c + 1).SetValue(cells[c]);
            }
            book.SaveAs(path);
        }
        Assert.True(AppointmentList.LooksLikeOne(path));
        Assert.Equal(InquiryBuilder.Build(List(), SampleSettings, SampleTime).Text, InquiryBuilder.Build(AppointmentList.Load(path), SampleSettings, SampleTime).Text);
    }

    [Fact]
    public void Service_types_follow_the_procedure_and_npis_are_checked()
    {
        Assert.Equal(["30", "98"], ServiceTypes.For("99214"));
        Assert.Equal(["30", "BZ"], ServiceTypes.For("99393"));
        Assert.Equal(["30", "MH"], ServiceTypes.For("90837"));
        Assert.Equal(["30", "62"], ServiceTypes.For("72148"));
        Assert.Equal(["30", "4"], ServiceTypes.For("71046"));
        Assert.Equal(["30", "5"], ServiceTypes.For("80053"));
        Assert.Equal(["30", "86"], ServiceTypes.For("99284"));
        Assert.Equal(["30", "35"], ServiceTypes.For("D1110"));
        Assert.Equal(["30"], ServiceTypes.For("G0438"));
        Assert.True(Npi.IsValid("1642031870"));
        Assert.False(Npi.IsValid("1642031871"));
        Assert.False(Npi.IsValid("164203187"));
    }

    [Fact]
    public void Every_question_finds_its_answer_by_trace_and_a_refusing_payer_answers_its_own()
    {
        var files = new[] { "clinic-270.x12", "bluewater-271.x12", "prairie-271.x12", "summit-271.x12", "cascade-271.x12" }.Select(Samples.Load).ToList();
        var all = Matcher.All(files);
        Assert.Equal(12, all.Count);
        Assert.Equal(11, all.Count(x => x.Answer.How.StartsWith("trace", StringComparison.Ordinal)));
        var samuel = all.Single(x => x.Asked.First == "SAMUEL").Answer;
        Assert.Equal((null, "CASCADE MUTUAL"), (samuel.Person, samuel.Refusal!.Payer!.Name));
        Assert.Same(files[1].People.Single(p => p.First == "EMMA"), all.Single(x => x.Asked.First == "EMMA").Answer.Person);

        // Without traces, by who it is.
        var appointments = List();
        var peter = Matcher.For(appointments.Single(a => a.First == "PETER"), files);
        Assert.Equal(("member ID and name", "PETER"), (peter.How, peter.Person!.First));
        var emma = Matcher.For(appointments.Single(a => a.First == "EMMA"), files);
        Assert.True(emma.Person!.IsDependent);
        Assert.False(Matcher.For(appointments.Single(a => a.First == "NOLAN"), files).Found);
    }
}
