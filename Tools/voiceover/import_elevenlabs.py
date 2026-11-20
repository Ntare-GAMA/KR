"""Brings ElevenLabs voice files into the game.

  python Tools/voiceover/import_elevenlabs.py            match, convert and install
  python Tools/voiceover/import_elevenlabs.py --dry-run  only show the matching

ElevenLabs names each download after the first 15 characters of its text, e.g.
  ElevenLabs_audio_elevenlabs-v4_Good day! Welco_2026-11-20T19_34_31.mp3
so each file is matched to a line of Documentation/voice-script.csv by that prefix ("_" stands for a character
Windows cannot keep, like ":" or "?"; [tags] such as [warmly] are ignored). Where several lines start the same
way, the clip's length decides. A sub-folder named after a character (Aline, Mzee Gakuba, MF-Thanks ...) limits
its files to that character. OVERRIDES settles the files the prefix cannot (lines voiced with "here" for the
business name, Kinyarwanda written phonetically, lines split in two). When a line has several takes, the
newest wins.

Each matched clip is trimmed of leading and trailing silence, loudness-normalised (-16 LUFS, mono, 44.1 kHz) and
written to Assets/Resources/Story/VO/<conversation>_<node>.wav, the name ConversationRunner loads. Staff lines
are voiced by one staff voice, so the scratch _f/_m takes of a recorded staff line are removed. Installed files
are listed in Tools/voiceover/recorded.json; make_voice_lines.py scratch never overwrites them.
"""
import csv
import hashlib
import json
import math
import os
import re
import subprocess
import sys
import tempfile
import unicodedata

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
SRC = os.path.join(ROOT, "Assets", "Sourced Assets", "ElevenLabs Audio")
VO = os.path.join(ROOT, "Assets", "Resources", "Story", "VO")
CSV = os.path.join(ROOT, "Documentation", "voice-script.csv")
RECORDED = os.path.join(os.path.dirname(__file__), "recorded.json")

# Sub-folder -> the conversations its files belong to.
FOLDERS = {
    "Maman_Fils": "mf_tea", "MF-Thanks": "mf_thanks", "MF-Chats": "mf_chat", "Jean-Paul-jp-chat": "jp_chat",
    "Jean-Paul-jp-library": "jp_library", "Aline": "al_chat", "Mzee Gakuba": "gk_", "Officer Mukamana": "off_",
}
# Part of a file name -> line id ("" = not used: a spare take).
OVERRIDES = {
    "[delighted] ..._2026-11-20T16_56_46": "mf_tea_a2",
    "[warmly] Mwa..._2026-11-20T16_56_46": "mf_tea_a1",
    "[warmly] Muraho_2026-11-20T181357": "svc_food_a",
    "Muraho! Welcome_2026-11-20T181544": "svc_shop_a",
    "mu.ɾɑ.ɦo! Wel_2026-11-20T20_03_08": "svc_salon_a",
    "Thank you. [war": "off_lesson_f6",
    "Welcome here! A": "svc_hotel_a",
    "Hey! Welcome he": "svc_gym_a",
    "[excited] Hey! ": "al_chat_j1",
    "Good day, welco_2026-11-20T20_04_08": "svc_office_a",
    "ElevenLabs_2026-11-20T17_55_42__s50_v4": "",
    "Do you have an _2026-11-20T18_53_47": "",
}
# Lines delivered in parts: joined in this order with a short pause.
JOINS = {
    "svc_clinic_a": ["Good day, welco_2026-11-20T18_49_50", "Do you have an _2026-11-20T19_54_40"],
}
RATE = 15.5  # spoken characters per second, measured on the unambiguous clips


def clean(s):
    s = re.sub(r"\[[^\]]*\]?", "", s).replace("mu.ɾɑ.ɦo", "Muraho")
    s = unicodedata.normalize("NFKD", s).lower()
    return re.sub(r"\s+", " ", s).strip()


def spoken(t):
    return t.replace("{place}", "here").replace("{name}", "friend")


def duration(p):
    out = subprocess.run(["ffprobe", "-v", "error", "-show_entries", "format=duration", "-of", "csv=p=0", p],
                         capture_output=True, text=True).stdout
    return float(out.strip() or 0)


def load_lines():
    lines = {}
    for r in csv.DictReader(open(CSV, encoding="utf-8")):
        lid = re.sub(r"(_[fm])?\.wav$", "", r["file"])
        g = lines.setdefault(lid, {"id": lid, "text": r["line"], "files": []})
        g["files"].append(r["file"])
    return lines


def load_files():
    files, seen = [], {}
    for d, _, names in os.walk(SRC):
        folder = os.path.basename(d)
        for n in sorted(names):
            if not n.lower().endswith((".mp3", ".wav")):
                continue
            p = os.path.join(d, n)
            h = hashlib.md5(open(p, "rb").read()).hexdigest()
            m = re.search(r"v-?4_(.*?)(?:\.\.\.)?_2026-(\d\d-\d\dT\d\d_?\d\d_?\d\d)", n)
            f = {"path": p, "name": n, "folder": folder if d != SRC else "", "prefix": m.group(1) if m else "",
                 "stamp": re.sub(r"\D", "", m.group(2)) if m else "", "dur": duration(p)}
            if h in seen:  # byte-identical copy: keep the one in a character folder
                if f["folder"] and not seen[h]["folder"]:
                    seen[h].update(f)
                continue
            seen[h] = f
            files.append(f)
    return files


def match(lines, files):
    joined = {part for parts in JOINS.values() for part in parts}
    choice, pairs = {}, []
    for i, f in enumerate(files):
        f["line"] = None
        if any(part in f["name"] for part in joined):
            continue
        ov = next((v for k, v in OVERRIDES.items() if k in f["name"]), None)
        if ov is not None:
            f["line"] = ov or None
            continue
        hint = next((v for k, v in FOLDERS.items() if f["folder"].startswith(k)), None)
        pre = clean(f["prefix"])
        if not pre:
            continue
        pat = re.compile(re.escape(pre).replace("_", "."))
        for l in lines.values():
            if hint and not l["id"].startswith(hint):
                continue
            if pat.match(clean(spoken(l["text"]))):
                err = abs(math.log(f["dur"] / (len(spoken(l["text"])) / RATE + 0.3)))
                if err <= 0.6:
                    pairs.append((err, i, l["id"]))
    # Best fits first, one take per line; the remaining takes join the line they fit best.
    used, first = set(), {}
    for err, i, lid in sorted(pairs):
        if i not in used and lid not in first:
            first[lid] = i
            used.add(i)
    for err, i, lid in sorted(pairs):
        if i not in used:
            files[i]["line"] = lid
            used.add(i)
    for lid, i in first.items():
        files[i]["line"] = lid
    for f in files:
        if f["line"]:
            best = choice.get(f["line"])
            if best is None or f["stamp"] > best["stamp"]:
                choice[f["line"]] = f
    return choice


FILTER = ("silenceremove=start_periods=1:start_threshold=-50dB:start_silence=0.04,areverse,"
          "silenceremove=start_periods=1:start_threshold=-50dB:start_silence=0.08,areverse,"
          "loudnorm=I=-16:TP=-1.5:LRA=11")


def convert(sources, out):
    if len(sources) == 1:
        args = ["-i", sources[0], "-af", FILTER]
    else:
        args = []
        for s in sources:
            args += ["-i", s]
        n = len(sources)
        chain = "".join(f"[{k}:a]aresample=44100,aformat=channel_layouts=mono[a{k}];" for k in range(n))
        gap = "anullsrc=r=44100:cl=mono,atrim=duration=0.25[g];"
        order = "".join(f"[a{k}]" + ("[g]" if k == 0 else "") for k in range(n))
        args += ["-filter_complex", chain + gap + order + f"concat=n={n * 2 - 1}:v=0:a=1[c];[c]{FILTER}[o]", "-map", "[o]"]
    subprocess.run(["ffmpeg", "-v", "error", "-y", *args, "-ac", "1", "-ar", "44100", "-c:a", "pcm_s16le", out], check=True)


def main():
    sys.stdout.reconfigure(encoding="utf-8")
    dry = "--dry-run" in sys.argv
    lines, files = load_lines(), load_files()
    choice = match(lines, files)
    plan = {lid: [f["path"]] for lid, f in choice.items()}
    for lid, parts in JOINS.items():
        srcs = [next(f["path"] for f in files if part in f["name"]) for part in parts if any(part in f["name"] for f in files)]
        if len(srcs) == len(parts):
            plan[lid] = srcs
    for lid in sorted(lines):
        mark = " + ".join(os.path.basename(p)[30:60] for p in plan[lid]) if lid in plan else "-- not recorded"
        print(f"{lid:22} {mark}")
    spare = [f["name"] for f in files if not f["line"] and not any(p in f["name"] for ps in JOINS.values() for p in ps)]
    print(f"\n{len(plan)}/{len(lines)} lines recorded; {len(spare)} files not used: {spare}")
    if dry:
        return
    recorded = {}
    for lid, srcs in sorted(plan.items()):
        out = os.path.join(VO, lid + ".wav")
        convert(srcs, out)
        for gendered in lines[lid]["files"]:
            if gendered != lid + ".wav":
                for x in (gendered, gendered + ".meta"):
                    if os.path.exists(os.path.join(VO, x)):
                        os.remove(os.path.join(VO, x))
        recorded[lid + ".wav"] = [os.path.relpath(s, ROOT).replace("\\", "/") for s in srcs]
    json.dump(recorded, open(RECORDED, "w", encoding="utf-8"), ensure_ascii=False, indent=1)
    print(f"Installed {len(recorded)} lines -> Assets/Resources/Story/VO/ (list: Tools/voiceover/recorded.json)")


if __name__ == "__main__":
    main()
