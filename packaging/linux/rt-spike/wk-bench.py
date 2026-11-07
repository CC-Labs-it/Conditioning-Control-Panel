#!/usr/bin/env python3
"""rt-linux-spike/wk-bench.py - one URL in a WebKitGTK web view, the engine Avalonia.Controls.WebView 12.1 drives on
Linux (webkit2gtk-4.1, GTK3, engine default settings like the app). Quits when the page title says SPIKE-DONE.

  python3 wk-bench.py URL [--size 1920x1080] [--info out.json] [--scrolller-block] [--timeout 600]

--scrolller-block compiles and adds the same content-blocker rule the app puts on every WebKitGTK view
(CCP.Avalonia/Platform/ScrolllerWebKitFilter.cs) BEFORE the first load, as WebHost does. Engine facts go to --info.
"""
import argparse
import json
import os
import sys

ap = argparse.ArgumentParser()
ap.add_argument('url')
ap.add_argument('--size', default='1920x1080')
ap.add_argument('--info')
ap.add_argument('--scrolller-block', action='store_true')
ap.add_argument('--timeout', type=int, default=600)
a = ap.parse_args()

import gi  # noqa: E402
gi.require_version('Gtk', '3.0')
api = None
for v in ('4.1', '4.0'):
    try:
        gi.require_version('WebKit2', v)
        api = v
        break
    except ValueError:
        pass
if not api:
    print('wk-bench: no WebKit2 GObject typelib. Install one of: gir1.2-webkit2-4.1 (Debian/Ubuntu), '
          'webkit2gtk4.1 (Fedora), webkit2gtk-4.1 (Arch)', file=sys.stderr)
    sys.exit(3)
from gi.repository import GLib, Gtk, WebKit2  # noqa: E402

RULES = '[{"trigger":{"url-filter":"^[^:]+://+([^:/]+\\\\.)?scrolller\\\\.com[/:]"},"action":{"type":"block"}}]'
info = {
    'api': api,
    'webkit': '%d.%d.%d' % (WebKit2.get_major_version(), WebKit2.get_minor_version(), WebKit2.get_micro_version()),
    'gtk': '%d.%d.%d' % (Gtk.get_major_version(), Gtk.get_minor_version(), Gtk.get_micro_version()),
    'flatpak': os.path.exists('/.flatpak-info'),
    'session': os.environ.get('XDG_SESSION_TYPE'), 'wayland': bool(os.environ.get('WAYLAND_DISPLAY')),
    'gdkBackend': os.environ.get('GDK_BACKEND'),
    'env': {k: v for k, v in os.environ.items() if k.startswith('WEBKIT_') or k in ('LIBGL_ALWAYS_SOFTWARE', 'GSK_RENDERER')},
    'scrolllerFilter': None, 'webProcessGone': None, 'timedOut': False,
}
if os.path.exists('/.flatpak-info'):
    try:
        info['flatpakInfo'] = open('/.flatpak-info').read()[:600]
    except OSError:
        pass

w, h = (int(x) for x in a.size.split('x'))
win = Gtk.Window(title='Racing Thoughts spike (WebKitGTK)')
win.set_default_size(w, h)
win.move(0, 0)
view = WebKit2.WebView()
s = view.get_settings()
s.set_enable_webgl(True)
s.set_media_playback_requires_user_gesture(False)
s.set_enable_write_console_messages_to_stdout(True)
try:
    info['hardwareAcceleration'] = s.get_hardware_acceleration_policy().value_nick
except Exception as e:  # removed in some builds
    info['hardwareAcceleration'] = 'n/a: %s' % e
try:
    view.set_is_muted(True)  # every test browser starts muted (owner rule)
except Exception:
    pass
win.add(view)


def finish(*_):
    if a.info:
        with open(a.info, 'w') as f:
            json.dump(info, f, indent=1)
    Gtk.main_quit()
    return False


def on_title(v, _p):
    if v.get_title() == 'SPIKE-DONE':
        GLib.timeout_add(500, finish)


def on_gone(_v, reason):
    info['webProcessGone'] = getattr(reason, 'value_nick', str(reason))
    finish()


def on_timeout():
    info['timedOut'] = True
    return finish()


view.connect('notify::title', on_title)
view.connect('web-process-terminated', on_gone)
win.connect('destroy', finish)
GLib.timeout_add_seconds(a.timeout, on_timeout)


def go():
    view.load_uri(a.url)
    return False


if a.scrolller_block:
    out_dir = os.path.dirname(os.path.abspath(a.info or '.')) or '.'
    store = WebKit2.UserContentFilterStore.new(os.path.join(out_dir, 'webkit-filters'))

    def saved(st, res):
        try:
            flt = st.save_finish(res)
            view.get_user_content_manager().add_filter(flt)
            info['scrolllerFilter'] = 'compiled and added'
        except Exception as e:
            info['scrolllerFilter'] = 'FAILED: %s' % e
        go()

    store.save('ccp-scrolller-block-spike', GLib.Bytes.new(RULES.encode()), None, saved)
else:
    GLib.idle_add(go)

win.show_all()
Gtk.main()
