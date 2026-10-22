using System.Text;

namespace PrusaTrayDiagnostic.App.SingleInstance;

/// <summary>What a command of a later start asks the running instance to do (T-043).</summary>
internal enum InstanceCommandKind
{
    /// <summary>Bring the most recently used auxiliary window to the front, otherwise open the settings.</summary>
    Activate,

#if DEBUG
    // Debug builds only, accepted only in the isolated test mode (T-044): the end-to-end tests open windows
    // deterministically through the channel instead of clicking into the notification area.

    /// <summary>Test mode: like the tray item Settings.</summary>
    Settings,

    /// <summary>Test mode: like the tray item Diagnostics; the argument is the printer id, or none for the first printer.</summary>
    Diagnostics,

    /// <summary>Test mode: like the tray item About.</summary>
    About,

    /// <summary>Test mode: like the tray item Camera of the printer in the argument (opens or closes its camera window).</summary>
    Camera,

    /// <summary>Test mode: like the tray item Quit.</summary>
    Quit,

    /// <summary>Test mode: a deliberate crash (T-046), the argument says where: <c>ui</c>, <c>background</c> or <c>task</c>.</summary>
    Crash,
#endif
}

/// <summary>A command of a later start (T-043); in Debug builds also a test command with an argument (T-044).</summary>
/// <param name="Argument">Printer id of a test command; <see langword="null"/> otherwise.</param>
internal readonly record struct InstanceCommand(InstanceCommandKind Kind, string? Argument = null)
{
    /// <summary>Bring the most recently used auxiliary window to the front, otherwise open the settings.</summary>
    public static InstanceCommand Activate { get; } = new(InstanceCommandKind.Activate);

    /// <summary>The wire text without the line feed, also used in log messages (no secrets: a printer id at most).</summary>
    public override string ToString() => InstanceProtocol.ToText(this);
}

/// <summary>
/// The wire format between a second start and the running instance: one ASCII command word ended by a line feed, which
/// the server acknowledges with a line feed once the command is handed over. The client keeps the connection open until
/// then: closing right after writing could lose the command (a pipe instance the server has not accepted yet is closed
/// with its data). The server's pipe has an input buffer (<see cref="NamedPipeInstanceTransport.InputBufferSize"/>), so
/// the write normally completes at once; should the running instance hang without reading or answering, the client gives
/// up at the end of its time budget (<see cref="SingleInstanceGuard"/>).
/// <para>
/// Debug builds also know the test commands of T-044: <c>settings</c>, <c>diagnostics</c>, <c>diagnostics:&lt;id&gt;</c>,
/// <c>about</c>, <c>camera:&lt;id&gt;</c>, <c>quit</c> and <c>crash:&lt;ui|background|task&gt;</c> (T-046) (printer ids of ASCII letters, digits, <c>-</c> and <c>_</c>).
/// </para>
/// </summary>
internal static class InstanceProtocol
{
    /// <summary>Longest message the server reads; anything longer is ignored.</summary>
    public const int MaxMessageLength = 64;

    /// <summary>The reply to a command that was handed over.</summary>
    public const byte Acknowledgement = (byte)'\n';

    private const string ActivateText = "activate";

    public static byte[] Encode(InstanceCommand command)
    {
        string text = ToText(command);
        if (text.Length >= MaxMessageLength)
        {
            throw new ArgumentOutOfRangeException(nameof(command), command, "The command is too long.");
        }

        return Encoding.ASCII.GetBytes(text + "\n");
    }

    /// <summary>The wire text of <paramref name="command"/> without the line feed.</summary>
    public static string ToText(InstanceCommand command)
    {
        string word = command.Kind switch
        {
            InstanceCommandKind.Activate => ActivateText,
#if DEBUG
            InstanceCommandKind.Settings => "settings",
            InstanceCommandKind.Diagnostics => "diagnostics",
            InstanceCommandKind.About => "about",
            InstanceCommandKind.Camera => "camera",
            InstanceCommandKind.Quit => "quit",
            InstanceCommandKind.Crash => "crash",
#endif
            _ => throw new ArgumentOutOfRangeException(nameof(command), command.Kind, "Unknown instance command."),
        };
        if (command.Argument is null)
        {
            return word;
        }

        if (!TakesArgument(command.Kind) || !IsValidArgument(command.Argument))
        {
            throw new ArgumentOutOfRangeException(nameof(command), command.Kind, "Invalid argument of the instance command.");
        }

        return word + ":" + command.Argument;
    }

    /// <summary>Parses one received message (without or with its line feed); unknown text is <see langword="false"/>.</summary>
    public static bool TryDecode(ReadOnlySpan<byte> message, out InstanceCommand command)
    {
        ReadOnlySpan<byte> text = message.TrimEnd("\r\n"u8);
        if (text.SequenceEqual("activate"u8))
        {
            command = InstanceCommand.Activate;
            return true;
        }

#if DEBUG
        if (TryDecodeTestCommand(text, out command))
        {
            return true;
        }
#endif

        command = default;
        return false;
    }

#if DEBUG
    private static bool TryDecodeTestCommand(ReadOnlySpan<byte> text, out InstanceCommand command)
    {
        command = default;
        int colon = text.IndexOf((byte)':');
        ReadOnlySpan<byte> word = colon >= 0 ? text[..colon] : text;
        InstanceCommandKind? kind = word switch
        {
            _ when word.SequenceEqual("settings"u8) => InstanceCommandKind.Settings,
            _ when word.SequenceEqual("diagnostics"u8) => InstanceCommandKind.Diagnostics,
            _ when word.SequenceEqual("about"u8) => InstanceCommandKind.About,
            _ when word.SequenceEqual("camera"u8) => InstanceCommandKind.Camera,
            _ when word.SequenceEqual("quit"u8) => InstanceCommandKind.Quit,
            _ when word.SequenceEqual("crash"u8) => InstanceCommandKind.Crash,
            _ => null,
        };
        if (kind is not { } found)
        {
            return false;
        }

        if (colon < 0)
        {
            // The camera command needs its printer, the crash command its kind.
            if (found is InstanceCommandKind.Camera or InstanceCommandKind.Crash)
            {
                return false;
            }

            command = new InstanceCommand(found);
            return true;
        }

        string argument = Encoding.ASCII.GetString(text[(colon + 1)..]);
        if (!TakesArgument(found) || !IsValidArgument(argument))
        {
            return false;
        }

        command = new InstanceCommand(found, argument);
        return true;
    }
#endif

    private static bool TakesArgument(InstanceCommandKind kind) => kind switch
    {
#if DEBUG
        InstanceCommandKind.Diagnostics or InstanceCommandKind.Camera or InstanceCommandKind.Crash => true,
#endif
        _ => false,
    };

    private static bool IsValidArgument(string argument)
    {
        if (argument.Length == 0)
        {
            return false;
        }

        foreach (char c in argument)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_')
            {
                return false;
            }
        }

        return true;
    }
}
