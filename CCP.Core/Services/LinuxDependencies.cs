using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// The Linux runtime libraries the head needs, and the distro-aware message that names the one
    /// command installing whichever are missing (docs/avalonia-linux-install.md, "In-app work").
    /// Replaces the adapter's generic "Install webkit2gtk 4.0+ package."; an unknown distro keeps a
    /// generic list of package names as the fallback for tarball users. Package names are the
    /// table in that doc and must stay in step with packaging/aur/PKGBUILD.
    /// </summary>
    public static class LinuxDependencies
    {
        public enum Family { Unknown, Arch, Debian, Fedora }

        /// <summary>One need. Every entry of <see cref="Libraries"/> must load; an entry may list
        /// alternatives separated by '|' (any one loading is enough). Null package names mean the
        /// family's repositories cannot satisfy it (WPE, distro-check finding F2): no command is
        /// printed for it, <see cref="Message"/> says those pages are unavailable instead.</summary>
        public sealed record Dependency(string Need, string[] Libraries, string? Arch, string? Debian, string? Fedora);

        public static readonly IReadOnlyList<Dependency> All = new[]
        {
            new Dependency("web views", new[] { "libwebkit2gtk-4.1.so.0|libwebkit2gtk-4.0.so.37" },
                "webkit2gtk-4.1", "libwebkit2gtk-4.1-0", "webkit2gtk4.1"),
            new Dependency("web views (WPE)", new[] { "libwpe-1.0.so.1", "libWPEBackend-fdo-1.0.so.1", "libWPEWebKit-2.0.so.1" },
                // Ubuntu 24.04 / Fedora 44 ship none of the three, Debian 12 / Ubuntu 22.04 only WebKit 1.x
                // (distro-check F2, 2026-10-10). Arch extra ships all three (pacman -Si, 2026-10-10).
                "wpewebkit wpebackend-fdo libwpe", null, null),
            new Dependency("audio and video", new[] { "libvlc.so.5" },
                "vlc", "libvlc5 vlc-plugin-base", "vlc-libs"),
            new Dependency("sign-in token store", new[] { "libsecret-1.so.0" },
                "libsecret", "libsecret-1-0", "libsecret"),
            new Dependency("overlays and panic key", new[] { "libX11.so.6", "libXi.so.6" },
                "libx11 libxi", "libx11-6 libxi6", "libX11 libXi"),
        };

        /// <summary>Distro family from /etc/os-release text: ID first, then each ID_LIKE token.</summary>
        public static Family FamilyOf(string? osRelease)
        {
            string Field(string key) => (osRelease ?? "").Split('\n')
                .Select(l => l.Trim())
                .Where(l => l.StartsWith(key + "=", StringComparison.Ordinal))
                .Select(l => l[(key.Length + 1)..].Trim('"', '\''))
                .FirstOrDefault() ?? "";
            foreach (var id in (Field("ID") + " " + Field("ID_LIKE")).Split(' ', StringSplitOptions.RemoveEmptyEntries))
                switch (id.ToLowerInvariant())
                {
                    case "arch": case "cachyos": case "manjaro": case "endeavouros": return Family.Arch;
                    case "debian": case "ubuntu": return Family.Debian;
                    case "fedora": case "rhel": case "centos": return Family.Fedora;
                }
            return Family.Unknown;
        }

        /// <summary>The needs whose libraries do not load through <paramref name="canLoad"/>.</summary>
        public static IReadOnlyList<Dependency> Missing(Func<string, bool> canLoad, IEnumerable<Dependency>? among = null) =>
            (among ?? All).Where(d => !d.Libraries.All(alts => alts.Split('|').Any(canLoad))).ToList();

        /// <summary>The one install command for <paramref name="missing"/>, or null for an unknown
        /// distro (the caller then shows <see cref="Message"/>'s package list instead).</summary>
        public static string? InstallCommand(Family family, IEnumerable<Dependency> missing)
        {
            var list = missing.Where(d => IsPackaged(family, d)).ToList();
            if (list.Count == 0) return null;
            return family switch
            {
                Family.Arch => "sudo pacman -S --needed " + string.Join(" ", list.Select(d => d.Arch)),
                Family.Debian => "sudo apt install " + string.Join(" ", list.Select(d => d.Debian)),
                Family.Fedora => "sudo dnf install " + string.Join(" ", list.Select(d => d.Fedora)),
                _ => null,
            };
        }

        /// <summary>User-facing text naming what is missing and how to install it, or null when
        /// nothing is. The command is returned separately for a Copy button.
        /// ponytail: English only (like WebHost's panel; WPF has no such message) except the newer WPE line.</summary>
        public static (string Text, string? Command)? Message(IReadOnlyList<Dependency> missing, string? osRelease)
        {
            if (missing.Count == 0) return null;
            var family = FamilyOf(osRelease);
            var packaged = missing.Where(d => IsPackaged(family, d)).ToList();
            var unavailable = packaged.Count < missing.Count ? WpeText() : null;
            if (packaged.Count == 0) return (unavailable!, null);
            var needs = string.Join(", ", packaged.Select(d => d.Need));
            var command = InstallCommand(family, packaged);
            var text = command != null
                ? $"Missing system packages for {needs}. Install them with:\n{command}"
                : $"Missing system packages for {needs}. Install these libraries with your package manager: "
                  + string.Join(", ", packaged.SelectMany(d => d.Libraries).Select(l => l.Replace("|", " or ")));
            return (unavailable is null ? text : text + "\n" + unavailable, command);
        }

        /// <summary>Real check for this machine: dlopen each library, read /etc/os-release.
        /// Null off Linux or when everything is present.</summary>
        public static (string Text, string? Command)? Check(IEnumerable<Dependency>? among = null)
        {
            if (!OperatingSystem.IsLinux()) return null;
            string? osRelease = null;
            try { osRelease = File.ReadAllText("/etc/os-release"); } catch { }
            return Message(Missing(CanLoad, among), osRelease);
        }

        // Unknown family: only needs every known family packages get a library list.
        private static bool IsPackaged(Family family, Dependency d) => family switch
        {
            Family.Arch => d.Arch != null,
            Family.Debian => d.Debian != null,
            Family.Fedora => d.Fedora != null,
            _ => d.Arch != null && d.Debian != null && d.Fedora != null,
        };

        // English draft when no language is loaded (Core tests, LinuxSmoke); en.json carries the same text.
        // No Flatpak is offered: none has shipped yet (review wizard-hidpi-wpe P2).
        private static string WpeText() => Loc.Get("linux_dep_wpe_unavailable") is var s && s != "linux_dep_wpe_unavailable" ? s
            : "Built-in web pages need WPE WebKit 2.0, which only Arch and openSUSE Tumbleweed package in full; "
              + "on other distributions those pages are unavailable for now. Everything else works without it.";

        // Not freed: dlclose of GTK/WebKit is not safe, and the head loads them anyway.
        private static bool CanLoad(string library) => NativeLibrary.TryLoad(library, out _);
    }
}
