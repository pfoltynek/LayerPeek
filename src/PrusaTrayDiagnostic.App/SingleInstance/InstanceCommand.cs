using System.Text;

namespace PrusaTrayDiagnostic.App.SingleInstance;

/// <summary>What a second start of the app asks the running instance to do (T-043).</summary>
internal enum InstanceCommand
{
    /// <summary>Bring the most recently used auxiliary window to the front, otherwise open the settings.</summary>
    Activate,
}

/// <summary>
/// The wire format between a second start and the running instance: one ASCII command word ended by a line feed. The
/// client sends it and closes the connection; there is no reply (a tiny write never blocks on a pipe or socket buffer).
/// </summary>
internal static class InstanceProtocol
{
    /// <summary>Longest message the server reads; anything longer is ignored.</summary>
    public const int MaxMessageLength = 64;

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
