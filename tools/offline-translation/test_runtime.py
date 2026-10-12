"""Offline safety regression tests: python test_runtime.py (no installed models needed)."""
import http.client
import importlib.util
import json
import socket
import threading
import tempfile
import unittest
import zipfile
from pathlib import Path
from types import SimpleNamespace


def load_sibling(name):
    spec = importlib.util.spec_from_file_location(name, Path(__file__).with_name(name + ".py"))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


server = load_sibling("server")
installer = load_sibling("setup_runtime")


class ArchiveTests(unittest.TestCase):
    def test_archive_cannot_escape_runtime(self):
        for name in ("../outside.txt", "/absolute.txt", "C:/outside.txt", "sub\\..\\outside.txt"):
            with self.subTest(name=name), tempfile.TemporaryDirectory() as temp:
                root = Path(temp)
                archive = root / "model.zip"
                with zipfile.ZipFile(archive, "w") as output:
                    output.writestr(name, "untrusted")
                with self.assertRaises(ValueError):
                    installer.extract_checked(archive, root / "models")
                self.assertFalse((root / "outside.txt").exists())

    def test_archive_cannot_install_symlinks(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            archive = root / "model.zip"
            entry = zipfile.ZipInfo("link")
            entry.create_system = 3
            entry.external_attr = 0o120777 << 16
            with zipfile.ZipFile(archive, "w") as output:
                output.writestr(entry, "../outside")
            with self.assertRaises(ValueError):
                installer.extract_checked(archive, root / "models")

    def test_nested_model_layout_extracts_within_runtime(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            archive = root / "model.zip"
            with zipfile.ZipFile(archive, "w") as output:
                output.writestr("en_zh/model/model.bin", b"model")
            installer.extract_checked(archive, root / "models")
            self.assertEqual((root / "models/en_zh/model/model.bin").read_bytes(), b"model")

    def test_corrupted_cached_download_is_rejected(self):
        with tempfile.TemporaryDirectory() as temp:
            file = Path(temp) / "package.whl"
            file.write_bytes(b"changed archive")
            with self.assertRaises(ValueError):
                installer.validate_download(file, "0" * 64)


class HttpRequestTests(unittest.TestCase):
    def setUp(self):
        self.requests = []
        def translate(text, source, target):
            self.requests.append((text, source, target))
            return "translated", source
        self.httpd = server.LocalServer(("127.0.0.1", 0), server.Handler)
        self.httpd.engine = SimpleNamespace(translate=translate)
        self.httpd.translation_lock = threading.Lock()
        self.thread = threading.Thread(target=self.httpd.serve_forever, daemon=True)
        self.thread.start()
        self.addCleanup(self.close_server)

    def close_server(self):
        self.httpd.shutdown()
        self.httpd.server_close()
        self.thread.join(timeout=3)

    def connection(self):
        connection = http.client.HTTPConnection("127.0.0.1", self.httpd.server_port, timeout=3)
        self.addCleanup(connection.close)
        return connection

    def test_complete_request_reaches_engine(self):
        connection = self.connection()
        connection.request("POST", "/translate", json.dumps({"q": "Hello.", "source": "en", "target": "zh"}),
                           {"Content-Type": "application/json"})
        response = connection.getresponse()
        self.assertEqual(response.status, 200)
        self.assertEqual(json.loads(response.read())["translatedText"], "translated")
        self.assertEqual(self.requests, [("Hello.", "en", "zh")])

    def test_truncated_body_is_rejected_even_if_received_prefix_is_valid_json(self):
        connection = self.connection()
        body = json.dumps({"q": "Hello.", "source": "en", "target": "zh"})
        connection.request("POST", "/translate", body,
                           {"Content-Type": "application/json", "Content-Length": str(len(body) + 20)})
        connection.sock.shutdown(socket.SHUT_WR)
        response = connection.getresponse()
        self.assertEqual(response.status, 400)
        self.assertEqual(json.loads(response.read())["code"], "invalid_input")
        self.assertEqual(self.requests, [])

    def test_nested_json_is_rejected_with_a_response_instead_of_dropping_connection(self):
        connection = self.connection()
        body = "[" * 2000 + "0" + "]" * 2000
        connection.request("POST", "/translate", body, {"Content-Type": "application/json"})
        response = connection.getresponse()
        self.assertEqual(response.status, 400)
        self.assertEqual(json.loads(response.read())["code"], "invalid_input")
        self.assertEqual(self.requests, [])

    def test_ambiguous_body_lengths_do_not_reach_engine(self):
        connection = self.connection()
        body = json.dumps({"q": "Hello.", "source": "en", "target": "zh"})
        connection.putrequest("POST", "/translate")
        connection.putheader("Content-Type", "application/json")
        connection.putheader("Content-Length", len(body))
        connection.putheader("Content-Length", len(body) + 1)
        connection.endheaders(body.encode("utf-8"))
        self.assertEqual(connection.getresponse().status, 400)
        self.assertEqual(self.requests, [])

    def test_browser_origin_cannot_submit_translation(self):
        connection = self.connection()
        connection.request("POST", "/translate", "q=Hello&source=en&target=zh",
                           {"Content-Type": "application/x-www-form-urlencoded", "Origin": "https://example.com"})
        self.assertEqual(connection.getresponse().status, 403)
        self.assertEqual(self.requests, [])


class InferenceTests(unittest.TestCase):
    def engine(self, detected="en", confidence=0.99, output=None):
        engine = server.Engine.__new__(server.Engine)
        engine.detector = SimpleNamespace(classify=lambda _: (detected, confidence))
        self.batches = []
        def translate_batch(tokens, **options):
            self.batches.append((tokens, options))
            return [SimpleNamespace(hypotheses=[output or ["\u2581result"]]) for _ in tokens]
        tokenizer = SimpleNamespace(encode=lambda text, **_: list(text), decode=lambda tokens: "".join(tokens))
        translator = SimpleNamespace(translate_batch=translate_batch)
        engine.models = {("en", "zh"): (tokenizer, translator), ("zh", "en"): (tokenizer, translator)}
        return engine

    def test_unsupported_source_does_not_reach_inference(self):
        engine = self.engine(detected="fr")
        with self.assertRaises(server.TranslationError) as error:
            engine.translate("Bonjour le monde.", "auto", "zh")
        self.assertEqual(error.exception.code, "language_model_unavailable")
        self.assertEqual(self.batches, [])

    def test_ambiguous_source_does_not_reach_inference(self):
        engine = self.engine(confidence=0.2)
        with self.assertRaises(server.TranslationError):
            engine.translate("ok", "auto", "zh")
        self.assertEqual(self.batches, [])

    def test_long_sentence_is_rejected_without_truncation(self):
        engine = self.engine()
        with self.assertRaises(server.TranslationError) as error:
            engine.translate("x" * 257, "en", "zh")
        self.assertEqual(error.exception.code, "text_too_long")
        self.assertEqual(self.batches, [])

    def test_sentencepiece_markers_are_removed_and_newlines_preserved(self):
        engine = self.engine()
        translated, detected = engine.translate("Hello.\r\n\r\nWorld.", "en", "zh")
        self.assertEqual(translated, "result\r\n\r\nresult")
        self.assertEqual(detected, "en")
        self.assertTrue(all(options["max_input_length"] == 0 for _, options in self.batches))

    def test_output_limit_is_reported_instead_of_returning_truncated_text(self):
        engine = self.engine(output=["x"] * 512)
        with self.assertRaises(server.TranslationError) as error:
            engine.translate("Hello.", "en", "zh")
        self.assertEqual(error.exception.code, "text_too_long")

    def test_traditional_chinese_is_not_silently_downgraded(self):
        engine = self.engine()
        with self.assertRaises(server.TranslationError):
            engine.translate("Hello.", "en", "zh-TW")
        self.assertEqual(self.batches, [])


if __name__ == "__main__":
    unittest.main()
