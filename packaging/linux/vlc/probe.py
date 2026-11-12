#!/usr/bin/env python3
"""Decode a clip through the bundled libvlc with no window and no sound card.

usage: probe.py LIBVLC_SO CLIP MIN_VIDEO_FRAMES MIN_AUDIO_SECONDS
Video goes to vmem (RV32, as VlcFrameSink asks), audio to amem (S16N 48 kHz stereo). Exits 1
unless playback reaches Ended with at least the given number of frames and seconds of audio,
and, when MIN_VIDEO_FRAMES > 0, LibVLC accepted the RV32 chroma.
"""
import ctypes as C
import sys
import time

lib_path, clip, min_frames, min_audio = sys.argv[1], sys.argv[2], int(sys.argv[3]), float(sys.argv[4])
vlc = C.CDLL(lib_path)
vlc.libvlc_get_version.restype = C.c_char_p
vlc.libvlc_new.restype = C.c_void_p
vlc.libvlc_new.argtypes = [C.c_int, C.POINTER(C.c_char_p)]
vlc.libvlc_media_new_path.restype = C.c_void_p
vlc.libvlc_media_new_path.argtypes = [C.c_void_p, C.c_char_p]
vlc.libvlc_media_player_new_from_media.restype = C.c_void_p
vlc.libvlc_media_player_new_from_media.argtypes = [C.c_void_p]
for name in ("libvlc_media_player_play", "libvlc_media_player_get_state",
             "libvlc_media_player_release", "libvlc_media_release", "libvlc_release"):
    getattr(vlc, name).argtypes = [C.c_void_p]

W, H = 320, 240
frame = C.create_string_buffer(W * H * 4)
counts = {"frames": 0, "samples": 0, "chroma": b""}

# chroma is LibVLC's own 4-byte in/out buffer. A c_char_p argument would hand Python a bytes copy,
# so the write would never reach LibVLC; take it as a raw pointer and write through it.
FORMAT = C.CFUNCTYPE(C.c_uint, C.POINTER(C.c_void_p), C.POINTER(C.c_char), C.POINTER(C.c_uint),
                     C.POINTER(C.c_uint), C.POINTER(C.c_uint), C.POINTER(C.c_uint))
LOCK = C.CFUNCTYPE(C.c_void_p, C.c_void_p, C.POINTER(C.c_void_p))
DISPLAY = C.CFUNCTYPE(None, C.c_void_p, C.c_void_p)
PLAY = C.CFUNCTYPE(None, C.c_void_p, C.c_void_p, C.c_uint, C.c_int64)


@FORMAT
def on_format(_opaque, chroma, width, height, pitches, lines):
    buf = C.addressof(chroma.contents)  # raises on a bytes copy, so no frame is ever accepted
    C.memmove(buf, b"RV32", 4)
    counts["chroma"] = C.string_at(buf, 4)  # read back from LibVLC's buffer: the format it will use
    width[0], height[0], pitches[0], lines[0] = W, H, W * 4, H
    return 1


@LOCK
def on_lock(_opaque, planes):
    planes[0] = C.cast(frame, C.c_void_p)
    return None


@DISPLAY
def on_display(_opaque, _picture):
    counts["frames"] += 1


@PLAY
def on_play(_data, _samples, count, _pts):
    counts["samples"] += count


args = (C.c_char_p * 2)(b"--quiet", b"--no-video-title-show")
inst = vlc.libvlc_new(len(args), args)
if not inst:
    sys.exit("FAIL libvlc_new returned NULL (plugins not found?)")
media = vlc.libvlc_media_new_path(inst, clip.encode())
player = vlc.libvlc_media_player_new_from_media(media)
vlc.libvlc_video_set_format_callbacks(C.c_void_p(player), on_format, None)
vlc.libvlc_video_set_callbacks(C.c_void_p(player), on_lock, None, on_display, None)
vlc.libvlc_audio_set_callbacks(C.c_void_p(player), on_play, None, None, None, None, None)
vlc.libvlc_audio_set_format(C.c_void_p(player), b"S16N", 48000, 2)
vlc.libvlc_media_player_play(player)

ENDED, ERROR = 6, 7
deadline = time.monotonic() + 30
state = 0
while time.monotonic() < deadline:
    state = vlc.libvlc_media_player_get_state(player)
    if state in (ENDED, ERROR):
        break
    time.sleep(0.05)
vlc.libvlc_media_player_release(player)
vlc.libvlc_media_release(media)
vlc.libvlc_release(inst)

audio_s = counts["samples"] / 48000
# An audio-only clip never calls on_format, so chroma is only checked when frames are expected.
chroma_ok = min_frames == 0 or counts["chroma"] == b"RV32"
ok = (state == ENDED and chroma_ok and counts["frames"] >= min_frames
      and audio_s >= min_audio)
print(f"{'ok  ' if ok else 'FAIL'} {clip}: libvlc {vlc.libvlc_get_version().decode()}, "
      f"state={state}, chroma={counts['chroma'].decode(errors='replace')}, video frames={counts['frames']} (min {min_frames}), "
      f"audio={audio_s:.2f}s (min {min_audio})")
sys.exit(0 if ok else 1)
