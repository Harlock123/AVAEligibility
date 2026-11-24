using AVAEligibility.Core.Eligibility;
using AVAEligibility.Core.Inquiries;
using AVAEligibility.Core.Sending;

namespace AVAEligibility.Tests;

/// <summary>Real-time sending, only ever to <see cref="MockClearinghouse"/> on this computer.</summary>
public sealed class SendingTests
{
    private static Clearinghouse Settings(MockClearinghouse mock, string envelope = "mime") => new()
    {
        Enabled = true, Endpoint = mock.Address, Envelope = envelope, SenderId = "MAPLESTFM", ReceiverId = "MOCKCH", Username = MockClearinghouse.User,
    };

    private static IReadOnlyList<Appointment> List() => AppointmentList.Load(Samples.Path("appointments.csv"));

    [Fact]
    public void Sending_is_off_until_turned_on_and_only_over_https_off_this_computer()
    {
        var on = new Clearinghouse { Enabled = true, Endpoint = "https://ch.example/core", SenderId = "S", ReceiverId = "R", Username = "u" };
        Assert.Null(on.Problem());
        Assert.Equal("Sending to a clearinghouse is off (Inquiries › Clearinghouse).", (on with { Enabled = false }).Problem());
        Assert.Equal("The clearinghouse's address must be HTTPS: the inquiries hold patients' details.", (on with { Endpoint = "http://ch.example/core" }).Problem());
        Assert.Null((on with { Endpoint = "http://127.0.0.1:9/core" }).Problem());
        Assert.Contains("user name", (on with { Username = "" }).Problem());
        Assert.Throws<InvalidOperationException>(() => new CoreClient(new HttpClient()).SendAsync("x", new Clearinghouse(), "p", DateTime.Now).GetAwaiter().GetResult());
    }

    [Theory]
    [InlineData("mime")]
    [InlineData("soap")]
    public void A_270_goes_in_a_core_envelope_and_the_271_comes_back(string envelope)
    {
        using var mock = new MockClearinghouse();
        var list = List();
        var kenji = list.Single(a => a.First == "KENJI");
        var whole = InquiryBuilder.Build(list, InquiryTests.SampleSettings, InquiryTests.SampleTime);
        var one = InquiryBuilder.Build([kenji], InquiryTests.SampleSettings, InquiryTests.SampleTime, whole.Traces);
        using var http = new HttpClient();
        var r = new CoreClient(http).SendAsync(one.Text, Settings(mock, envelope), MockClearinghouse.Password, InquiryTests.SampleTime).GetAwaiter().GetResult();
        Assert.True(r.Ok, $"{r.ErrorCode} {r.ErrorMessage}");
        Assert.Equal(CoreClient.ResponseType, r.PayloadType);
        var answer = Assert.Single(EligibilityReader.Parse(r.Payload).People);
        Assert.Equal(("KENJI", "BW0007"), (answer.First, answer.Trace));
        var sent = Assert.Single(mock.Requests);
        Assert.Equal(("X12_270_Request_005010X279A1", "RealTime", "MAPLESTFM", "MOCKCH", "2.2.0"), (sent["PayloadType"], sent["ProcessingMode"], sent["SenderID"], sent["ReceiverID"], sent["CORERuleVersion"]));
        Assert.True(Guid.TryParse(sent["PayloadID"], out _));
        Assert.Matches(@"^\d{4}-\d\d-\d\dT\d\d:\d\d:\d\dZ$", sent["TimeStamp"]);
        Assert.Equal(one.Text, sent["Payload"]);

        var wrong = new CoreClient(http).SendAsync(one.Text, Settings(mock, envelope), "guess", InquiryTests.SampleTime).GetAwaiter().GetResult();
        Assert.Equal(("UserNameAndPasswordInvalid", false), (wrong.ErrorCode, wrong.Ok));
    }

    [Fact]
    public void A_whole_list_asked_in_real_time_gives_the_worklist_the_answer_files_give()
    {
        using var mock = new MockClearinghouse { Failures = 1 };
        var folder = Directory.CreateTempSubdirectory("elig-answers-").FullName;
        using var http = new HttpClient();
        var asked = RealTime.AskAsync(List(), InquiryTests.SampleSettings, Settings(mock), MockClearinghouse.Password, new CoreClient(http), folder, InquiryTests.SampleTime).GetAwaiter().GetResult();
        Assert.Equal(12, asked.Count);
        Assert.Equal(("HARTWELL", "Receiver Down", (string?)null), (asked[0].Appointment.Last, asked[0].Response.ErrorCode, asked[0].Saved));
        Assert.Equal(11, Directory.GetFiles(folder).Length);
        Assert.Equal(Enumerable.Range(1001, 12).Select(n => n.ToString("D9")), mock.Requests.Select(r => EligibilityReader.Parse(r["Payload"]).X12.Segments[0][13]));

        var answers = Directory.GetFiles(folder).Select(EligibilityReader.Load).ToList();
        var work = Worklist.Build(List(), answers);
        var expected = WorklistTests.SampleWork().Select(w => w.Word).ToList();
        expected[0] = "no answer";
        Assert.Equal(expected, work.Select(w => w.Word));
    }

    [Fact]
    public void The_password_is_kept_in_a_file_only_its_owner_can_read()
    {
        var folder = Directory.CreateTempSubdirectory("elig-secret-").FullName;
        var store = new PasswordStore(folder);
        Assert.Null(store.Read());
        store.Save("p4ss");
        Assert.Equal("p4ss", store.Read());
        if (!OperatingSystem.IsWindows())
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(Path.Combine(folder, "clearinghouse.secret")));
        store.Forget();
        Assert.Null(store.Read());
    }
}
