# Racing Thoughts Electron shell

CCP starts Electron with `main.js` and pipes stdin and stdout. The first line is
`{"type":"launch","url":"<WebAssetServer.Url('glassroad/index.html', query)>"}`.
Send later page protocol frames as JSON lines. `{"type":"shell-close"}` closes
the shell; closing stdin also destroys the window. stdout carries page frames
and `{"type":"escape"}` for each non-repeated keydown. CCP owns the two-Escape
panic policy and sends `{"type":"kept-escape"}` when the page should pause.
No diagnostics or token belong on stdout. The shell queues host frames until
the page bridge installs its message listener, with a bounded queue and line size. One shell owns one window.

The preload's `ccpHost` is the additive WebView bridge. Audio stays muted until
the page reports its own running AudioContext. Navigation, new windows, webview
tags and other permissions are refused. Requests are limited to the local asset
origin, local blobs/data, the race room WebSocket and its signed-song origin.
The Linux host, engine fetch, panic registration and sandbox selection are later
A5 steps; this shell is not launched by the app yet.
