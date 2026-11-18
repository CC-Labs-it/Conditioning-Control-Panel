# Linux Racing Thoughts shell

Electron is launched by LinuxRaceHost with `--ozone-platform=x11`. The first stdin
JSON line is `launch` with the loopback asset URL; subsequent lines are `focus`
or Glassroad messages. EOF closes the shell. Stdout contains only JSON lines:
page messages, `shell-loaded` with `displayBackend`, `shell-focus` with `active`,
`shell-escape` with a positive increasing `sequence` and `displayBackend`, and
`shell-escape-up`. Core owns panic and pause decisions. The explicitly selected
Ozone backend is reported; unspecified backends are refused. The preload extends
Chromium's `chrome` object with the WebView2-shaped bridge. Electron autoplay is
allowed without a gesture. Navigation, popups and permissions are restricted;
requests are confined to the local server and the race service. No Electron
installation is needed for the Node tests.
