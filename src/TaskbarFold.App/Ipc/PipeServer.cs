using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;

namespace TaskbarFold.App.Ipc;

/// <summary>
/// Listens for the one-shot requests sent by <c>TaskbarFold.exe --group</c>.
///
/// The pipe is created with an explicit ACL granting only the user who owns this
/// session, so another account on the same machine cannot make this resident pop
/// flyouts open. Enforcing it in the security descriptor lets the kernel do the check
/// at connect time, which is both stricter and simpler than inspecting each caller.
/// </summary>
public sealed class PipeServer : IDisposable
{
    private readonly Func<PipeMessage, string> _handler;
    private readonly CancellationTokenSource _shutdown = new();
    private Task? _loop;

    /// <param name="handler">
    /// Invoked on a background thread for each request; returns the line sent back.
    /// Implementations must marshal to the UI thread themselves.
    /// </param>
    public PipeServer(Func<PipeMessage, string> handler) =>
        _handler = handler ?? throw new ArgumentNullException(nameof(handler));

    public event EventHandler<Exception>? Faulted;

    public void Start() => _loop ??= Task.Run(() => AcceptLoopAsync(_shutdown.Token));

    private static NamedPipeServerStream CreateServerStream()
    {
        var security = new PipeSecurity();

        var user = WindowsIdentity.GetCurrent().User;
        if (user is not null)
        {
            security.AddAccessRule(new PipeAccessRule(user, PipeAccessRights.FullControl, AccessControlType.Allow));
        }

        // Local administrators are already able to do anything to this process; granting
        // them the pipe keeps elevated tooling and diagnostics working.
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            PipeAccessRights.FullControl,
            AccessControlType.Allow));

        return NamedPipeServerStreamAcl.Create(
            PipeProtocol.PipeName,
            PipeDirection.InOut,
            NamedPipeServerStream.MaxAllowedServerInstances,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous,
            inBufferSize: 0,
            outBufferSize: 0,
            pipeSecurity: security);
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                using var server = CreateServerStream();

                await server.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);

                try
                {
                    await HandleConnectionAsync(server, cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    if (server.IsConnected)
                    {
                        server.Disconnect();
                    }
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A client that vanished mid-handshake must not take the listener down.
                Faulted?.Invoke(this, ex);
                await Task.Delay(200, CancellationToken.None).ConfigureAwait(false);
            }
        }
    }

    private async Task HandleConnectionAsync(NamedPipeServerStream server, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(server, new UTF8Encoding(false), false, 1024, leaveOpen: true);
        using var writer = new StreamWriter(server, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };

        var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        string response;
        try
        {
            var message = JsonSerializer.Deserialize(line, PipeJsonContext.Default.PipeMessage);
            response = message is null ? PipeProtocol.ResponseError : _handler(message);
        }
        catch (JsonException)
        {
            response = PipeProtocol.ResponseError;
        }
        catch (Exception ex)
        {
            // The handler runs UI code. Letting it escape would kill the accept loop and
            // leave every pinned shortcut silently doing nothing for the rest of the session.
            Faulted?.Invoke(this, ex);
            response = PipeProtocol.ResponseError;
        }

        await writer.WriteLineAsync(response.AsMemory(), cancellationToken).ConfigureAwait(false);

        // Let the client finish reading before the stream is torn down.
        server.WaitForPipeDrain();
    }

    public void Dispose()
    {
        _shutdown.Cancel();

        // Unblock WaitForConnectionAsync: cancellation alone leaves the last accept pending.
        try
        {
            using var nudge = new NamedPipeClientStream(".", PipeProtocol.PipeName, PipeDirection.InOut);
            nudge.Connect(100);
        }
        catch (Exception ex) when (ex is TimeoutException or IOException or UnauthorizedAccessException)
        {
        }

        try
        {
            _loop?.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
        }

        _shutdown.Dispose();
    }
}
