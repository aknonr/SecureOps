using System.Net;
using System.Net.Sockets;
using System.Text;

namespace SecureOps.Tests.Integration.Announcements;

/// <summary>Loopback-only protocol fixture. It records DATA without delivering or relaying anything.</summary>
internal sealed class LocalSmtpSink : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new(TimeSpan.FromSeconds(30));
    private readonly Task _server;
    internal string Mode { get; set; } = "Accepted";
    internal int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;
    internal List<(string[] Recipients, string Data)> Messages { get; } = [];
    internal int Connections { get; private set; }
    internal LocalSmtpSink() { _listener.Start(); _server = ServeAsync(); }
    private async Task ServeAsync()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                using TcpClient client = await _listener.AcceptTcpClientAsync(_stop.Token);
                Connections++;
                await SessionAsync(client);
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        catch (SocketException) when (_stop.IsCancellationRequested) { }
        catch (ObjectDisposedException) when (_stop.IsCancellationRequested) { }
    }
    private async Task SessionAsync(TcpClient client)
    {
        await using NetworkStream stream = client.GetStream();
        using var reader = new StreamReader(stream, Encoding.UTF8, false, leaveOpen: true);
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: true) { NewLine = "\r\n", AutoFlush = true };
        await writer.WriteLineAsync("220 localhost synthetic SMTP sink");
        List<string> recipients = [];
        while (!_stop.IsCancellationRequested)
        {
            string? line = await reader.ReadLineAsync(_stop.Token);
            if (line is null)
            { return; }
            if (line.StartsWith("EHLO", StringComparison.Ordinal) || line.StartsWith("HELO", StringComparison.Ordinal))
            { await writer.WriteLineAsync("250 localhost"); }
            else if (line.StartsWith("MAIL FROM:", StringComparison.Ordinal))
            { recipients.Clear(); await writer.WriteLineAsync("250 Sender accepted"); }
            else if (line.StartsWith("RCPT TO:", StringComparison.Ordinal))
            {
                string recipient = line[(line.IndexOf('<') + 1)..line.IndexOf('>')];
                if (Mode == "Rejected" || Mode == "Partial" && recipient.StartsWith("reject", StringComparison.Ordinal))
                { await writer.WriteLineAsync("550 Synthetic recipient rejection"); }
                else
                { recipients.Add(recipient); await writer.WriteLineAsync("250 Recipient accepted"); }
            }
            else if (line == "DATA")
            {
                await writer.WriteLineAsync("354 End with dot");
                var body = new StringBuilder();
                while ((line = await reader.ReadLineAsync(_stop.Token)) is not null && line != ".")
                {
                    if (body.Length > 4_100_000)
                    { throw new InvalidOperationException("Synthetic message bound exceeded."); }
                    body.Append(line.StartsWith("..", StringComparison.Ordinal) ? line[1..] : line).Append("\r\n");
                }
                Messages.Add((recipients.ToArray(), body.ToString()));
                if (Mode == "Unknown")
                { return; }
                await writer.WriteLineAsync(Mode == "DataRejected" ? "554 Synthetic DATA rejection" : "250 Synthetic DATA accepted");
            }
            else if (line == "QUIT")
            { await writer.WriteLineAsync("221 Closing"); return; }
            else if (line == "RSET")
            { recipients.Clear(); await writer.WriteLineAsync("250 Reset"); }
            else
            { await writer.WriteLineAsync("250 OK"); }
        }
    }
    public async ValueTask DisposeAsync() { _stop.Cancel(); _listener.Stop(); await _server; _stop.Dispose(); }
}
