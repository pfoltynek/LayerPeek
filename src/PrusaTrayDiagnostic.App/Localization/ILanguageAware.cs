namespace PrusaTrayDiagnostic.App.Localization;

/// <summary>A view model that formats texts itself and formats them again after a language switch (T-029).</summary>
internal interface ILanguageAware
{
    /// <summary>UI thread: the language changed; every text the view model produced is formatted again.</summary>
    void RefreshTexts();
}
