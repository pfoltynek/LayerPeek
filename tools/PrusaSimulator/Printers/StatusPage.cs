using System.Net;

namespace LayerPeek.Simulator.Printers;

/// <summary>
/// The page at <c>/</c>: a stand-in for the PrusaLink web, e.g. for "Open PrusaLink" through the app's proxy
/// (T-028). It polls <c>/api/v1/status</c> with <c>fetch</c>; the proxy adds the login, a browser opened directly asks
/// for it (Digest).
/// </summary>
internal static class StatusPage
{
    public static string Render(SimulatedModel model, string hostname)
    {
        string title = WebUtility.HtmlEncode($"PrusaLink simulator – {model.ProductName} ({hostname})");
        return $$"""
            <!DOCTYPE html>
            <html lang="en">
            <head>
            <meta charset="utf-8">
            <title>{{title}}</title>
            <style>
            body { font-family: system-ui, sans-serif; margin: 2em; }
            dt { font-weight: bold; } dd { margin: 0 0 .5em 0; }
            </style>
            </head>
            <body>
            <h1 id="title">{{title}}</h1>
            <dl>
            <dt>State</dt><dd id="state">…</dd>
            <dt>Progress</dt><dd id="progress">–</dd>
            <dt>Nozzle</dt><dd id="nozzle">–</dd>
            <dt>Bed</dt><dd id="bed">–</dd>
            </dl>
            <script>
            async function refresh() {
              try {
                const response = await fetch('api/v1/status', { headers: { 'Accept': 'application/json' } });
                if (!response.ok) { document.getElementById('state').textContent = 'HTTP ' + response.status; return; }
                const status = await response.json();
                const p = status.printer;
                document.getElementById('state').textContent = p.state;
                document.getElementById('progress').textContent = status.job ? status.job.progress.toFixed(1) + ' %' : '–';
                document.getElementById('nozzle').textContent = p.temp_nozzle.toFixed(1) + ' / ' + p.target_nozzle.toFixed(0) + ' °C';
                document.getElementById('bed').textContent = p.temp_bed.toFixed(1) + ' / ' + p.target_bed.toFixed(0) + ' °C';
              } catch (e) {
                document.getElementById('state').textContent = 'not reachable';
              }
            }
            refresh();
            setInterval(refresh, 2000);
            </script>
            </body>
            </html>
            """;
    }
}
