using System.Net;
using System.Text;
using System.Xml.Linq;
using AVAEligibility.Core.Eligibility;
using AVAEligibility.Core.Sending;

namespace AVAEligibility.Tests;

/// <summary>
/// A clearinghouse on this computer, for tests only: it takes CORE real-time requests (MIME multipart or SOAP), checks
/// the user name and password and the envelope's fields, and answers each 270 with a 271 about that one person, cut
/// from the samples' 271s by trace number. A payer that refused the whole batch answers with its refusal. Nothing
/// ever goes off this computer.
/// </summary>
internal sealed class MockClearinghouse : IDisposable
{
    public const string User = "maple-test";
    public const string Password = "not-a-real-password-9";
    private readonly HttpListener _listener = new();
    private readonly List<EligibilityFile> _answers;
    private readonly Task _serving;

    public string Address { get; }
    public List<Dictionary<string, string>> Requests { get; } = [];
    public int Failures { get; set; }

    public MockClearinghouse()
    {
        _answers = [.. WorklistTests.AnswerFiles.Select(Samples.Load)];
        for (var attempt = 0; ; attempt++)
        {
            var port = Random.Shared.Next(20000, 60000);
            Address = $"http://127.0.0.1:{port}/core/realtime/";
            try
            {
                _listener.Prefixes.Clear();
                _listener.Prefixes.Add(Address);
                _listener.Start();
                break;
            }
            catch (HttpListenerException) when (attempt < 20) { }
        }
        _serving = Task.Run(Serve);
    }

    private async Task Serve()
    {
        while (_listener.IsListening)
        {
            HttpListenerContext c;
            try
            {
                c = await _listener.GetContextAsync();
            }
            catch (Exception e) when (e is HttpListenerException or ObjectDisposedException or InvalidOperationException)
            {
                return;
            }
            try
            {
                await Answer(c);
            }
            catch (Exception e)
            {
                // A mistake in the mock shows as a 500 with its message, never as a request left hanging.
                c.Response.StatusCode = 500;
                c.Response.StatusDescription = e.GetType().Name;
            }
            finally
            {
                c.Response.Close();
            }
        }
    }

    private async Task Answer(HttpListenerContext c)
    {
        var body = await new StreamReader(c.Request.InputStream, Encoding.UTF8).ReadToEndAsync();
        var soap = c.Request.ContentType?.Contains("soap", StringComparison.OrdinalIgnoreCase) == true;
        Dictionary<string, string> fields;
        if (soap)
        {
            var doc = XDocument.Parse(body);
            fields = doc.Descendants().Where(e => !e.HasElements).ToDictionary(e => e.Name.LocalName, e => e.Value);
            fields["UserName"] = fields.GetValueOrDefault("Username", "");
        }
        else
        {
            var boundary = c.Request.ContentType!.Split("boundary=")[1].Trim('"');
            fields = CoreClient.Parts(body, boundary).ToDictionary(p => p.Name, p => p.Value);
        }
        lock (Requests) Requests.Add(fields);

        string code = "Success", message = "", payload = "";
        if (fields.GetValueOrDefault("UserName") != User || fields.GetValueOrDefault("Password") != Password) (code, message) = ("UserNameAndPasswordInvalid", "The user name or password is wrong.");
        else if (fields.GetValueOrDefault("PayloadType") != CoreClient.RequestType || fields.GetValueOrDefault("ProcessingMode") != "RealTime") (code, message) = ("PayloadTypeIncorrect", "Only real-time 270s.");
        else if (Failures > 0) { Failures--; (code, message) = ("Receiver Down", "Try again later."); }
        else payload = Cut(EligibilityReader.Parse(fields["Payload"]).People.Single(p => p.Trace != null));

        var response = new Dictionary<string, string>
        {
            ["PayloadType"] = code == "Success" ? CoreClient.ResponseType : "CoreEnvelopeError", ["ProcessingMode"] = "RealTime", ["PayloadID"] = Guid.NewGuid().ToString(),
            ["TimeStamp"] = "2026-11-25T08:16:00Z", ["SenderID"] = fields.GetValueOrDefault("ReceiverID", ""), ["ReceiverID"] = fields.GetValueOrDefault("SenderID", ""),
            ["CORERuleVersion"] = CoreClient.RuleVersion, ["ErrorCode"] = code, ["ErrorMessage"] = message, ["Payload"] = payload,
        };
        byte[] bytes;
        if (soap)
        {
            XNamespace s = "http://www.w3.org/2003/05/soap-envelope", cor = "http://www.caqh.org/SOAP/WSDL/CORERule2.2.0.xsd";
            var xml = new XElement(s + "Envelope", new XElement(s + "Body", new XElement(cor + "COREEnvelopeRealTimeResponse", response.Select(kv => new XElement(kv.Key, kv.Value)))));
            bytes = Encoding.UTF8.GetBytes(xml.ToString());
            c.Response.ContentType = "application/soap+xml; charset=utf-8";
        }
        else
        {
            const string boundary = "mock-core-boundary";
            var b = new StringBuilder();
            foreach (var (k, v) in response) b.Append("--").Append(boundary).Append("\r\nContent-Disposition: form-data; name=\"").Append(k).Append("\"\r\n\r\n").Append(v).Append("\r\n");
            b.Append("--").Append(boundary).Append("--\r\n");
            bytes = Encoding.UTF8.GetBytes(b.ToString());
            c.Response.ContentType = $"multipart/form-data; boundary={boundary}";
        }
        c.Response.ContentLength64 = bytes.Length;
        await c.Response.OutputStream.WriteAsync(bytes);
    }

    /// <summary>A 271 about the one person asked: the sample answer's envelope, payer and provider, and that person's loops.</summary>
    private string Cut(Person asked)
    {
        var refusal = _answers.SelectMany(f => f.Transactions).FirstOrDefault(t => t.Payer?.Id == asked.Transaction.Payer?.Id && t.People.Count == 0);
        if (refusal != null) return File.ReadAllText(Samples.Path(_answers.First(f => f.Transactions.Contains(refusal)).Name));
        var answer = _answers.SelectMany(f => f.People).Single(p => p.Trace == asked.Trace);
        var file = _answers.First(f => f.Transactions.Contains(answer.Transaction));
        var t = answer.Transaction.Source;
        var header = t.Body.TakeWhile(s => !(s.Id == "HL" && s[3] is "22" or "23")).Select(s => s.Text).ToList();
        var body = new List<string>();
        if (answer.IsDependent)
        {
            var sub = answer.Subscriber!;
            body.Add(sub.Segments[0].Text);
            body.AddRange(sub.Segments.Where(s => s.Id == "NM1").Take(1).Select(s => s.Text));
        }
        foreach (var s in answer.Segments)
            body.Add(s.Id == "HL" && s[3] == "22" ? string.Join(file.X12.Delimiters.Element, s.Elements[..4]) + file.X12.Delimiters.Element + "0" : s.Text);
        var all = new List<string> { t.Start.Text };
        all.AddRange(header);
        all.AddRange(body);
        all.Add($"SE*{all.Count + 1}*{t.Control}");
        return string.Join("~\n", new[] { t.Interchange!.Text, t.Group!.Text }.Concat(all).Concat([t.GroupEnd!.Text, t.InterchangeEnd!.Text])) + "~\n";
    }

    public void Dispose()
    {
        _listener.Stop();
        _listener.Close();
        _serving.Wait(TimeSpan.FromSeconds(5));
    }
}
