using System.Security;
using System.Security.Principal;
using System.Text.Json.Serialization;

namespace TaskbarFold.App.Ipc;

/// <param name="Command">One of the <see cref="PipeProtocol"/> command constants.</param>
/// <param name="GroupId">Target group for <c>show-group</c>.</param>
/// <param name="X">Cursor X in physical screen pixels, captured by the signalling process.</param>
/// <param name="Y">Cursor Y in physical screen pixels.</param>
public sealed record PipeMessage(string Command, Guid? GroupId = null, int X = 0, int Y = 0);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(PipeMessage))]
public sealed partial class PipeJsonContext : JsonSerializerContext;

public static class PipeProtocol
{
    public const string ShowGroup = "show-group";
    public const string OpenSettings = "open-settings";
    public const string Ping = "ping";

    public const string ResponseOk = "ok";
    public const string ResponseUnknownGroup = "unknown-group";
    public const string ResponseError = "error";

    /// <summary>
    /// One pipe per user. Two people signed into the same machine each get their own
    /// resident and their own groups, and neither can poke the other's.
    /// </summary>
    public static string PipeName
    {
        get
        {
            string sid;
            try
            {
                sid = WindowsIdentity.GetCurrent().User?.Value ?? "unknown";
            }
            catch (SecurityException)
            {
                sid = "unknown";
            }

            return $"TaskbarFold.{sid}";
        }
    }
}
