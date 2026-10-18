using System.IO;
using System.IO.Pipes;
using System.Text;

namespace Tfx;

/// <summary>
/// Link between a running tfx and a tfx started from its built-in terminal.
/// The terminal's shell gets <see cref="ParentPidVariable"/> in its environment;
/// a tfx launched there sends its folder to the parent over a per-process named
/// pipe instead of opening a second window.
/// </summary>
internal static class InstanceLink
{
    public const string ParentPidVariable = "TFX_PARENT_PID";

    private const int ConnectTimeoutMs = 500;

    public static string PipeName(int pid) => $"tfx-link-{pid}";

    /// <summary>
    /// Sends <paramref name="folder"/> to the tfx with process id
    /// <paramref name="pid"/>. Returns false when that instance cannot be reached.
    /// </summary>
    public static bool TrySend(int pid, string folder)
    {
        try
        {
            using var client = new NamedPipeClientStream(
                ".", PipeName(pid), PipeDirection.Out, PipeOptions.CurrentUserOnly);
            client.Connect(ConnectTimeoutMs);
            var bytes = Encoding.UTF8.GetBytes(folder);
            client.Write(bytes, 0, bytes.Length);
            client.Flush();
            return true;
        }
        catch
        {
            return false;
        }
    }
}

/// <summary>
/// Receives folders sent by <see cref="InstanceLink.TrySend"/>. The callback runs
/// on a thread-pool thread; marshal to the UI thread inside it.
/// </summary>
internal sealed class InstanceLinkServer : IDisposable
{
    private const int MaxChars = 32768;
    private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(2);

    private readonly CancellationTokenSource _cts = new();
    private readonly Action<string> _onFolder;

    public InstanceLinkServer(Action<string> onFolder)
    {
        _onFolder = onFolder;
        _ = Task.Run(() => RunAsync(_cts.Token));
    }

    private async Task RunAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await using var server = new NamedPipeServerStream(
                        InstanceLink.PipeName(Environment.ProcessId),
                        PipeDirection.In,
                        1,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                    await server.WaitForConnectionAsync(ct);

                    // A client that connects and never writes must not stall the loop.
                    using var readCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    readCts.CancelAfter(ReadTimeout);
                    using var reader = new StreamReader(server, Encoding.UTF8);
                    var buffer = new char[MaxChars];
                    var total = 0;
                    int read;
                    while (total < MaxChars
                        && (read = await reader.ReadAsync(buffer.AsMemory(total, MaxChars - total), readCts.Token)) > 0)
                    {
                        total += read;
                    }

                    var folder = new string(buffer, 0, total).Trim();
                    if (folder.Length > 0)
                    {
                        _onFolder(folder);
                    }
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    // read timeout: drop this client, accept the next
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    await Task.Delay(1000, ct);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
    }
}
