using LayerPeek.Core.Settings;

namespace LayerPeek.App.Installer;

/// <summary>The start after logon the user chose, as the install hook reads it (T-201).</summary>
internal interface IAutostartChoiceSource
{
    /// <summary>The stored choice; <see langword="null"/> while the user never chose (or there are no settings).</summary>
    /// <exception cref="Exception">The settings cannot be read; the caller treats that like no choice.</exception>
    bool? Read();
}

/// <summary>
/// The stored choice of the running app (T-201 migration): read, and stored only while there is none yet.
/// </summary>
internal interface IAutostartChoiceStore : IAutostartChoiceSource
{
    /// <summary>Stores <paramref name="choice"/> unless a choice is stored already.</summary>
    /// <returns>Whether it was stored.</returns>
    Task<bool> StoreIfUnsetAsync(bool choice, CancellationToken cancellationToken);
}

/// <summary>
/// The choice in the settings store of the running app (<see cref="JsonSettingsStore"/>, so that the write waits for the
/// other writes of the app); see <see cref="AutostartChoiceMigration"/>.
/// </summary>
internal sealed class SettingsStoreAutostartChoice(JsonSettingsStore store) : IAutostartChoiceStore
{
    public bool? Read() => store.ReadStartAtLoginChoice();

    public Task<bool> StoreIfUnsetAsync(bool choice, CancellationToken cancellationToken) =>
        store.SetStartAtLoginChoiceIfUnsetAsync(choice, cancellationToken);
}

/// <summary>
/// <see cref="AppSettings.StartAtLogin"/> from <c>settings.json</c> of the data folder (T-201), read only
/// (<see cref="JsonSettingsStore.ReadStartAtLoginChoice"/>): the install hook neither converts nor writes the settings.
/// </summary>
internal sealed class SettingsFileAutostartChoice : IAutostartChoiceSource
{
    private readonly Func<string> _directory;

    /// <param name="directory">The data folder with <c>settings.json</c>; resolved only when reading.</param>
    public SettingsFileAutostartChoice(Func<string> directory)
    {
        ArgumentNullException.ThrowIfNull(directory);
        _directory = directory;
    }

    public bool? Read()
    {
        using JsonSettingsStore store = new(_directory());
        return store.ReadStartAtLoginChoice();
    }
}
