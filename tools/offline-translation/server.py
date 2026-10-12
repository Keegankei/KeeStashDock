"""Loopback-only LibreTranslate subset using local Argos models. No download code."""
import argparse
import json
import os
import re
import sys
import threading
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from urllib.parse import parse_qs

MAX_TEXT_LENGTH = 5000
MAX_BODY_BYTES = 65536


class TranslationError(Exception):
    def __init__(self, code, message):
        self.code = code
        self.message = message


class Engine:
    def __init__(self, runtime):
        import ctranslate2
        import sentencepiece
        from py3langid.langid import LanguageIdentifier, MODEL_FILE
        self.detector = LanguageIdentifier.from_pickled_model(MODEL_FILE, norm_probs=True)
        self.models = {}
        for source, target in (("en", "zh"), ("zh", "en")):
            directory = runtime / "models" / ("translate-" + source + "_" + target + "-1_9")
            tokenizer = sentencepiece.SentencePieceProcessor(model_file=str(directory / "sentencepiece.model"))
            translator = ctranslate2.Translator(str(directory / "model"), device="cpu", compute_type="int8", inter_threads=1, intra_threads=min(4, os.cpu_count() or 1))
            self.models[source, target] = tokenizer, translator

    def translate(self, text, source, target):
        if source == "auto":
            source, confidence = self.detector.classify(text)
            if confidence < 0.7:
                source = None
        if source not in {"en", "zh"} or target not in {"en", "zh"}:
            raise TranslationError("language_model_unavailable", "Only English and Simplified Chinese are installed. Source language may be unsupported or ambiguous; try a complete sentence.")
        if source == target:
            return text, source
        tokenizer, translator = self.models[source, target]
        # Preserve line breaks; never let CTranslate2 silently truncate long input.
        paragraphs = re.split(r"(\r\n|\r|\n)", text)
        output = []
        for paragraph in paragraphs:
            if not paragraph.strip() or paragraph in {"\r", "\n", "\r\n"}:
                output.append(paragraph)
                continue
            sentences = re.split(r"(?<=[.!?\u3002\uff01\uff1f])\s+|(?<=[\u3002\uff01\uff1f])(?=\S)", paragraph.strip())
            tokens = []
            for sentence in sentences:
                encoded = tokenizer.encode(sentence, out_type=str)
                # A long sentence is rejected intact rather than cut into misleading fragments.
                if len(encoded) > 256:
                    raise TranslationError("text_too_long", "A sentence is too long for the installed model. Split it into shorter sentences.")
                tokens.append(encoded)
            results = translator.translate_batch(tokens, beam_size=4, max_input_length=0, max_decoding_length=512, replace_unknowns=True, length_penalty=0.2)
            if any(len(result.hypotheses[0]) >= 512 for result in results):
                raise TranslationError("text_too_long", "The model reached its output limit. Try shorter sentences.")
            translated = [tokenizer.decode(result.hypotheses[0]).replace("\u2581", " ").strip() for result in results]
            output.append((" " if target == "en" else "").join(translated))
        return "".join(output), source


class Handler(BaseHTTPRequestHandler):
    server_version = "KeeStashDockOffline/1"

    def log_message(self, *_args):
        pass  # No request paths, bodies, source text or translations are logged.

    def setup(self):
        super().setup()
        self.connection.settimeout(10)

    def reply(self, status, body):
        data = json.dumps(body, ensure_ascii=False).encode("utf-8")
        self.send_response(status)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(data)))
        self.send_header("Cache-Control", "no-store")
        self.send_header("X-Content-Type-Options", "nosniff")
        self.end_headers()
        try:
            self.wfile.write(data)
        except (BrokenPipeError, ConnectionResetError):
            pass

    def allowed(self):
        # No browser access/CORS. Fixed Host prevents DNS-rebinding to this local service.
        expected = {"127.0.0.1:" + str(self.server.server_port), "localhost:" + str(self.server.server_port)}
        if self.headers.get("Host", "") not in expected or self.headers.get("Origin"):
            self.reply(403, {"error": "Only local application requests are accepted.", "code": "forbidden"})
            return False
        return True

    def do_GET(self):
        if not self.allowed():
            return
        if self.path == "/health":
            self.reply(200, {"service": "keestashdock-offline", "pid": os.getpid()})
        elif self.path == "/languages":
            self.reply(200, [{"code": "en", "name": "English", "targets": ["en", "zh"]}, {"code": "zh", "name": "Chinese (Simplified)", "targets": ["en", "zh"]}])
        else:
            self.reply(404, {"error": "Unknown endpoint."})

    def do_POST(self):
        if not self.allowed():
            return
        if self.path != "/translate":
            self.reply(404, {"error": "Unknown endpoint."})
            return
        try:
            lengths = self.headers.get_all("Content-Length", [])
            if len(lengths) != 1:
                raise ValueError()
            length = int(lengths[0])
            if not 0 < length <= MAX_BODY_BYTES or self.headers.get("Transfer-Encoding"):
                self.reply(413, {"error": "Request is too large or has no fixed length.", "code": "text_too_long"})
                return
            body = self.rfile.read(length)
            if len(body) != length:
                raise ValueError()
            raw = body.decode("utf-8")
            content_type = self.headers.get("Content-Type", "").split(";", 1)[0].lower()
            if content_type == "application/json":
                payload = json.loads(raw)
            elif content_type == "application/x-www-form-urlencoded":
                payload = {key: values[0] for key, values in parse_qs(raw, keep_blank_values=True).items()}
            else:
                self.reply(415, {"error": "Use JSON or URL-encoded form data.", "code": "invalid_input"})
                return
            if not isinstance(payload, dict):
                raise ValueError()
            text = payload.get("q")
            source = payload.get("source", "auto")
            target = payload.get("target")
            if not isinstance(text, str) or not text.strip() or not isinstance(source, str) or not isinstance(target, str) or payload.get("format", "text") != "text":
                raise ValueError()
            if len(text) > MAX_TEXT_LENGTH:
                self.reply(413, {"error": "Text exceeds 5000 characters.", "code": "text_too_long"})
                return
        except (ValueError, UnicodeError, TimeoutError, RecursionError):
            self.reply(400, {"error": "Invalid translation request.", "code": "invalid_input"})
            return
        if not self.server.translation_lock.acquire(blocking=False):
            self.reply(429, {"error": "The local engine is busy. Retry after the current request finishes.", "code": "rate_limited"})
            return
        try:
            translated, detected = self.server.engine.translate(text, source, target)
            self.reply(200, {"translatedText": translated, "detectedLanguage": {"language": detected}})
        except TranslationError as error:
            self.reply(400, {"error": error.message, "code": error.code})
        except Exception:
            self.reply(500, {"error": "The local model could not translate this request.", "code": "provider_unavailable"})
        finally:
            self.server.translation_lock.release()


class LocalServer(ThreadingHTTPServer):
    daemon_threads = True
    allow_reuse_address = False

    def handle_error(self, _request, _address):
        pass  # The standard traceback can contain user request data.


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--runtime", required=True, type=Path)
    parser.add_argument("--port", type=int, default=5000)
    args = parser.parse_args()
    if not 1024 <= args.port <= 65535:
        raise ValueError("Port must be between 1024 and 65535.")
    def reject_outbound_network(event, _arguments):
        if event in {"socket.connect", "socket.getaddrinfo"}:
            raise RuntimeError("Outbound network access is disabled in the offline engine.")
    sys.addaudithook(reject_outbound_network)
    with LocalServer(("127.0.0.1", args.port), Handler) as server:
        server.engine = Engine(args.runtime.resolve())
        server.translation_lock = threading.Lock()
        print("Local offline engine ready on 127.0.0.1:" + str(args.port), flush=True)
        try:
            server.serve_forever()
        except KeyboardInterrupt:
            pass


if __name__ == "__main__":
    try:
        main()
    except Exception as error:
        # This runs only during startup, before accepting translation requests.
        print("Offline engine startup failed: " + str(error), file=sys.stderr)
        sys.exit(1)
