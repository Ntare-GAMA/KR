"""Cuts single sound events (one plate set down, one sip, one step...) out of a longer recording.

  python Tools/audio/slice_events.py <input> <out_prefix> [--max N] [--min 0.25] [--maxlen 2.5] [--gap 0.18]

Finds the parts louder than the recording's quiet floor, separated by at least --gap seconds of quiet, and writes
the first N that last between --min and --maxlen seconds as <out_prefix>_1.wav, _2.wav ... (mono, 44.1 kHz,
short fades). Needs ffmpeg on PATH.
"""
import argparse
import math
import struct
import subprocess

RATE = 44100
HOP = 441  # 10 ms


def main():
    a = argparse.ArgumentParser()
    a.add_argument("input")
    a.add_argument("prefix")
    a.add_argument("--max", type=int, default=4)
    a.add_argument("--min", type=float, default=0.25)
    a.add_argument("--maxlen", type=float, default=2.5)
    a.add_argument("--gap", type=float, default=0.18)
    o = a.parse_args()
    raw = subprocess.run(["ffmpeg", "-v", "error", "-i", o.input, "-ac", "1", "-ar", str(RATE), "-f", "s16le", "-"],
                         capture_output=True, check=True).stdout
    n = len(raw) // 2
    s = struct.unpack(f"<{n}h", raw[: n * 2])
    env = []
    for i in range(0, n - HOP, HOP):
        w = s[i:i + HOP]
        env.append(20 * math.log10(max(1.0, math.sqrt(sum(x * x for x in w) / HOP)) / 32768))
    floor = sorted(env)[len(env) // 5]
    peak = sorted(env)[-max(1, len(env) // 50)]
    thr = floor + (peak - floor) * 0.35
    gap = int(o.gap * 100)
    segs, start, quiet = [], None, 0
    for i, e in enumerate(env):
        if e > thr:
            if start is None:
                start = i
            quiet = 0
        elif start is not None:
            quiet += 1
            if quiet >= gap:
                segs.append((start, i - quiet + 1))
                start, quiet = None, 0
    if start is not None:
        segs.append((start, len(env)))
    made = 0
    for a0, a1 in segs:
        t0 = max(0.0, a0 / 100 - 0.06)
        dur = (a1 - a0) / 100 + 0.18
        if dur < o.min:
            continue
        dur = min(dur, o.maxlen)
        made += 1
        out = f"{o.prefix}_{made}.wav"
        subprocess.run(["ffmpeg", "-v", "error", "-y", "-ss", f"{t0:.3f}", "-t", f"{dur:.3f}", "-i", o.input, "-ac", "1",
                        "-ar", str(RATE), "-af", f"afade=t=in:d=0.01,afade=t=out:st={max(0.0, dur - 0.08):.3f}:d=0.08", out],
                       check=True)
        if made >= o.max:
            break
    print(f"{o.input}: {len(segs)} events found, {made} written ({o.prefix}_*.wav)")


if __name__ == "__main__":
    main()
