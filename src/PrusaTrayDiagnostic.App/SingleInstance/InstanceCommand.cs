using System.Text;

namespace PrusaTrayDiagnostic.App.SingleInstance;

/// <summary>What a second start of the app asks the running instance to do (T-043).</summary>
internal enum InstanceCommand
{
    /// <summary>Bring the most recently used auxiliary window to the front, otherwise open the settings.</summary>
    Activate,
}

/// <summary>
/// The wire format between a second start and the running instance: one ASCII command word ended by a line feed, which
/// the server acknowledges with a line feed once the command is handed over. The client keeps the connection open until
/// then: closing right after writing could lose the command (a pipe instance the server has not accepted yet is closed
/// with its data). The server's pipe has an input buffer (<see cref="NamedPipeInstanceTransport.InputBufferSize"/>), so
/// the write normally completes at once; should the running instance hang without reading or answering, the client gives
/// up at the end of its time budget (<see cref="SingleInstanceGuard"/>).
/// </summary>
internal static class InstanceProtocol
{
    /// <summary>Longest message the server reads; anything longer is ignored.</summary>
    public const int MaxMessageLength = 64;

    /// <summary>The reply to a command that was handed over.</summary>
    public const byte Acknowledgement = (byte)'\n';

    private const string ActivateText = "activate";

    public static byte[] Encode(InstanceCommand command) => command switch
    {
        InstanceCommand.Activate => Encoding.ASCII.GetBytes(ActivateText + "\n"),
        _ => throw new ArgumentOutOfRangeException(nameof(command), command, "Unknown instance command."),
    };

    /// <summary>Parses one received message (without or with its line feed); unknown text is <see langword="false"/>.</summary>
    public static bool TryDecode(ReadOnlySpan<byte> message, out InstanceCommand command)
    {
        ReadOnlySpan<byte> text = message.TrimEnd("\r\n"u8);
        if (text.SequenceEqual("activate"u8))
        {
            command = InstanceCommand.Activate;
            return true;
        }

        command = default;
        return false;
    }
}
