"""
NLLB-200 translation server for ESPLanguage_EN.

Speaks the same API as Ollama / LM Studio (/v1/models, /v1/chat/completions), so ESPLanguage_EN's
"Local AI" engine can use Meta's NLLB-200 translation model without any change:

    Engine = Local AI (Ollama / LM Studio)   ->   Settings -> URL  http://localhost:8000/v1

Start:   python nllb_server.py                       (facebook/nllb-200-3.3B on the GPU)
         python nllb_server.py --model facebook/nllb-200-distilled-1.3B
         python nllb_server.py --device cpu          (slow, no GPU needed)

Needs:   torch (CUDA build for a GPU), transformers, sentencepiece
The source language of every text is picked from its script: Hangul -> Korean, Kana -> Japanese, Han -> Chinese.
Model licence: CC-BY-NC 4.0 (non-commercial use).
"""
import argparse
import json
import os
import sys
import threading
import time
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

import torch
from transformers import AutoModelForSeq2SeqLM, AutoTokenizer

TARGET = "eng_Latn"


def source_lang(text: str, han_default: str) -> str:
    han = False
    for c in text:
        o = ord(c)
        if 0xAC00 <= o <= 0xD7AF or 0x1100 <= o <= 0x11FF or 0x3130 <= o <= 0x318F:
            return "kor_Hang"
        if 0x3040 <= o <= 0x30FF:
            return "jpn_Jpan"
        if 0x3400 <= o <= 0x9FFF or 0xF900 <= o <= 0xFAFF:
            han = True
    return han_default if han else "kor_Hang"


SENTENCE_END = (".", "。", "!", "！", "?", "？")


def as_name(src: str, en: str) -> str:
    """NLLB writes sentences ("The Force Field."); mod texts are mostly names ("Force Field").
    Only applied when the source itself does not end like a sentence (DESC texts keep their punctuation)."""
    en = en.strip()
    if src.rstrip().endswith(SENTENCE_END):
        return en
    en = en.rstrip(".。 ")
    for lead in ("The ", "the ", "A ", "An ", "It's ", "It is "):
        if en.startswith(lead) and len(en) > len(lead) + 2:
            en = en[len(lead):]
            break
    return en[:1].upper() + en[1:] if en else en


def parse_items(content: str):
    """ESPLanguage_EN sends a JSON array of strings; plain lines work too."""
    content = content.strip()
    a, b = content.find("["), content.rfind("]")
    if a >= 0 and b > a:
        try:
            items = json.loads(content[a:b + 1])
            if isinstance(items, list):
                return [str(x) for x in items], True
        except json.JSONDecodeError:
            pass
    return [l for l in content.splitlines() if l.strip()], False


class Translator:
    def __init__(self, name: str, device: str, batch: int, han: str):
        self.name, self.device, self.batch, self.han = name, device, batch, han
        print(f"Loading {name} on {device} (the first start downloads the model) ...", flush=True)
        t = time.time()
        dtype = torch.float16 if device == "cuda" else torch.float32
        # fast copy: the first start saves the model in the dtype used here as .safetensors next to this script
        # (3.3B fp16 = 6.6 GB instead of 17.6 GB of fp32 .bin); later starts load that in seconds
        local = os.path.join(os.path.dirname(os.path.abspath(__file__)), "models",
                             name.rstrip("/").split("/")[-1] + ("-fp16" if dtype == torch.float16 else ""))
        if os.path.isfile(os.path.join(local, "config.json")):
            print(f"Using the local copy {local}", flush=True)
            self.tok = AutoTokenizer.from_pretrained(local)
            self.model = AutoModelForSeq2SeqLM.from_pretrained(local, dtype=dtype).to(device).eval()
        else:
            self.tok = AutoTokenizer.from_pretrained(name)
            # NLLB repos ship .bin weights on main; without use_safetensors=False transformers also fetches an
            # auto-converted .safetensors copy from a PR branch = the same 17.6 GB downloaded twice
            self.model = AutoModelForSeq2SeqLM.from_pretrained(name, dtype=dtype, use_safetensors=False).to(device).eval()
            if not os.path.isdir(name):
                try:
                    print(f"Saving a fast-loading copy to {local} ...", flush=True)
                    self.model.save_pretrained(local, safe_serialization=True)
                    self.tok.save_pretrained(local)
                    print("Saved. The next start loads from it (you may delete the hf folder to free space).", flush=True)
                except Exception as e:  # disk full etc.: keep running from the download
                    print(f"Could not save the local copy: {e}", flush=True)
        self.lock = threading.Lock()
        print(f"Ready in {time.time() - t:.0f}s", flush=True)

    @torch.inference_mode()
    def translate(self, texts):
        out = [""] * len(texts)
        groups = {}
        for i, s in enumerate(texts):
            groups.setdefault(source_lang(s, self.han), []).append(i)
        with self.lock:
            for lang, idx in groups.items():
                self.tok.src_lang = lang
                for k in range(0, len(idx), self.batch):
                    part = idx[k:k + self.batch]
                    enc = self.tok([texts[i] for i in part], return_tensors="pt", padding=True,
                                   truncation=True, max_length=256).to(self.device)
                    gen = self.model.generate(**enc, forced_bos_token_id=self.tok.convert_tokens_to_ids(TARGET),
                                              num_beams=4, max_new_tokens=256)
                    for i, s in zip(part, self.tok.batch_decode(gen, skip_special_tokens=True)):
                        out[i] = as_name(texts[i], s)
        return out


def make_handler(tr: Translator):
    model_id = tr.name.split("/")[-1]

    class Handler(BaseHTTPRequestHandler):
        def send_json(self, obj, code=200):
            body = json.dumps(obj, ensure_ascii=False).encode("utf-8")
            self.send_response(code)
            self.send_header("Content-Type", "application/json; charset=utf-8")
            self.send_header("Content-Length", str(len(body)))
            self.end_headers()
            self.wfile.write(body)

        def do_GET(self):
            if self.path.rstrip("/") in ("/v1/models", "/models"):
                self.send_json({"object": "list", "data": [{"id": model_id, "object": "model"}]})
            else:
                self.send_json({"status": "ok", "model": tr.name})

        def do_POST(self):
            if not self.path.rstrip("/").endswith("/chat/completions"):
                return self.send_json({"error": "not found"}, 404)
            try:
                req = json.loads(self.rfile.read(int(self.headers.get("Content-Length", 0))) or b"{}")
                user = [m for m in req.get("messages", []) if m.get("role") == "user"]
                items, as_json = parse_items(user[-1]["content"] if user else "")
                t = time.time()
                res = tr.translate(items)
                print(f"{len(items)} text(s) in {time.time() - t:.1f}s   e.g. {items[0]!r} -> {res[0]!r}" if items else "empty", flush=True)
                content = json.dumps(res, ensure_ascii=False) if as_json else "\n".join(res)
                self.send_json({
                    "id": "nllb-" + str(int(time.time() * 1000)), "object": "chat.completion", "model": model_id,
                    "choices": [{"index": 0, "finish_reason": "stop", "message": {"role": "assistant", "content": content}}],
                })
            except Exception as e:  # keep serving
                self.send_json({"error": {"message": str(e)}}, 500)

        def log_message(self, *args):
            pass

    return Handler


def main():
    # the Windows console code page (cp1252 / cp874...) cannot print Korean / Chinese / Japanese
    for stream in (sys.stdout, sys.stderr):
        try:
            stream.reconfigure(encoding="utf-8", errors="replace")
        except (AttributeError, ValueError):
            pass
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--model", default="facebook/nllb-200-3.3B",
                    help="facebook/nllb-200-3.3B | nllb-200-distilled-1.3B | nllb-200-distilled-600M")
    ap.add_argument("--device", default="cuda" if torch.cuda.is_available() else "cpu")
    ap.add_argument("--host", default="127.0.0.1")
    ap.add_argument("--port", type=int, default=8000)
    ap.add_argument("--batch", type=int, default=16)
    ap.add_argument("--chinese", default="zho_Hans", help="Han-only text: zho_Hans (simplified) or zho_Hant (traditional)")
    a = ap.parse_args()
    tr = Translator(a.model, a.device, a.batch, a.chinese)
    print(f"ESPLanguage_EN -> Engine: Local AI -> Settings -> URL  http://localhost:{a.port}/v1", flush=True)
    ThreadingHTTPServer((a.host, a.port), make_handler(tr)).serve_forever()


if __name__ == "__main__":
    main()
