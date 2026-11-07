using System;
using System.Threading.Tasks;

namespace ConditioningControlPanel.Avalonia.Browser;

// WPF BrowserService.cs:646, 711, 1450.
internal static class BrowserAdCss
{
    internal const string Script = """
        (function() {
            var style = document.createElement('style');
            style.textContent = `

                    /* Hide common ad containers */
                    [class*='ad-'], [class*='ads-'], [class*='advert'],
                    [id*='ad-'], [id*='ads-'], [id*='advert'],
                    [class*='banner'], [id*='banner'],
                    [class*='sponsor'], [id*='sponsor'],
                    .advertisement, .ad-container, .ad-wrapper,
                    iframe[src*='doubleclick'], iframe[src*='googlesyndication'],
                    iframe[src*='ads'], iframe[src*='adserver'],
                    [data-ad], [data-ads], [data-ad-slot],
                    div[aria-label*='Advertisement'],
                    /* Popup overlays */
                    .popup-ad, .pop-up, .modal-ad,
                    /* Specific to adult sites */
                    .exo-ad, .exoclick, .trafficjunky,
                    [class*='exo_'], [id*='exo_']
                    {
                        display: none !important;
                        visibility: hidden !important;
                        height: 0 !important;
                        width: 0 !important;
                        opacity: 0 !important;
                        pointer-events: none !important;
                    }
                `;
            document.head.appendChild(style);
        })();
        """;

    internal static Task ApplyAsync(Uri url, bool success, Func<string, Task<string?>> execute)
    {
        var host = url.IsAbsoluteUri ? url.Host : "";
        if (!success || host.Equals("hypnotube.com", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".hypnotube.com", StringComparison.OrdinalIgnoreCase))
            return Task.CompletedTask;
        return execute(Script);
    }
}
