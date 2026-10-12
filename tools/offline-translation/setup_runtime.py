"""Install exact Windows x64 wheels and Argos models; never invoked by the server."""
import argparse
import hashlib
import importlib
import json
import sys
import zipfile
from pathlib import Path, PurePosixPath

# Wheels are installed as data, without running package setup scripts or pip.
WHEELS = [
    ("ctranslate2-4.8.2-cp312-cp312-win_amd64.whl", "4e/23/e3b5322ff7368fcbed181ea4c209149416e7940b5b04971d5ee4084afe1a", "d94421d565d0de61c032998f737a18942b0f2bef40c0424b1846ec6f67300105"),
    ("sentencepiece-0.2.2-cp312-cp312-win_amd64.whl", "8d/11/753fca2e6b109be3ab7867abf357dfe48677fe726ae5a5363d0b54ca9450", "7c6e7bf684dc12145bfa685d3060beaea55139134ba848289bee514ed42e7383"),
    ("numpy-2.3.5-cp312-cp312-win_amd64.whl", "2d/57/8aeaf160312f7f489dea47ab61e430b5cb051f59a98ae68b7133ce8fa06a", "86945f2ee6d10cdfd67bcb4069c1662dd711f7e2a4343db5cecec06b87cf31aa"),
    ("pyyaml-6.0.3-cp312-cp312-win_amd64.whl", "86/bf/899e81e4cce32febab4fb42bb97dcdf66bc135272882d1987881a4b519e9", "5fcd34e47f6e0b794d17de1b4ff496c00986e1c83f7ab2fb8fcfe9616ff7477b"),
    ("py3langid-0.2.2-py3-none-any.whl", "e8/e6/ff3e5902bc4c70b8a1f71a21386dd60b622ff1708163bf44b699d6516e4b", "a5460ccc565c7b6a7a0e72f42236900b810e73c065a1b769a69fa9d9cff13f00"),
]
MODELS = [
    ("translate-en_zh-1_9", "en", "zh", "433e7c4f034d87fbe2353161e05f18646d7999452f801a4e1f0378522b9850ab"),
    ("translate-zh_en-1_9", "zh", "en", "62e7af5a3a48b530e47b7b3e5c78c2de79073ecd815750d2bf3ab35b4a67da2d"),
]


def digest(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def validate_download(destination, checksum):
    if destination.is_file() and digest(destination) == checksum:
        print("Reusing " + destination.name, flush=True)
        return
    raise ValueError("Missing or invalid archive: " + destination.name + ". Run Setup.ps1 to download verified packages.")


def extract_checked(archive_path, destination):
    destination = destination.resolve()
    with zipfile.ZipFile(archive_path) as archive:
        infos = archive.infolist()
        if sum(info.file_size for info in infos) > 1024 * 1024 * 1024:
            raise ValueError("Unexpected archive expansion size.")
        for info in infos:
            path = PurePosixPath(info.filename)
            if path.is_absolute() or ".." in path.parts or "\\" in info.filename or ":" in info.filename:
                raise ValueError("Unsafe archive path.")
            if (info.external_attr >> 16) & 0o170000 == 0o120000:
                raise ValueError("Archive symlinks are not supported.")
            target = destination.joinpath(*path.parts).resolve()
            if not target.is_relative_to(destination):
                raise ValueError("Archive path escapes installation directory.")
        archive.extractall(destination)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--runtime", type=Path)
    parser.add_argument("--manifest", action="store_true")
    args = parser.parse_args()
    if args.manifest:
        manifest = [{"name": name, "url": "https://files.pythonhosted.org/packages/" + location + "/" + name, "sha256": checksum} for name, location, checksum in WHEELS]
        manifest += [{"name": name + ".argosmodel", "url": "https://argos-net.com/v1/" + name + ".argosmodel", "sha256": checksum} for name, _, _, checksum in MODELS]
        print(json.dumps(manifest))
        return
    if args.runtime is None:
        parser.error("--runtime is required unless --manifest is selected")
    runtime = args.runtime.resolve()
    downloads = runtime / "downloads"
    downloads.mkdir(parents=True, exist_ok=True)
    for name, location, checksum in WHEELS:
        archive = downloads / name
        validate_download(archive, checksum)
        extract_checked(archive, runtime / "packages")
    for name, source, target, checksum in MODELS:
        archive = downloads / (name + ".argosmodel")
        validate_download(archive, checksum)
        with zipfile.ZipFile(archive) as model_zip:
            metadata = json.loads(model_zip.read(name + "/metadata.json"))
            if (metadata.get("from_code"), metadata.get("to_code"), metadata.get("package_version")) != (source, target, "1.9"):
                raise ValueError("Unexpected model metadata.")
            required = {name + suffix for suffix in ("/model/model.bin", "/model/config.json", "/sentencepiece.model", "/README.md")}
            if not required.issubset(model_zip.namelist()):
                raise ValueError("Incomplete model archive.")
        extract_checked(archive, runtime / "models")
    # Validate native libraries before marking setup successful.
    importlib.invalidate_caches()
    import ctranslate2
    import sentencepiece
    import py3langid
    if ctranslate2.__version__ != "4.8.2" or sentencepiece.__version__ != "0.2.2":
        raise ValueError("Unexpected inference runtime version.")
    (runtime / "installed.json").write_text(json.dumps({"schema": 1, "models": [m[0] for m in MODELS], "wheels": [w[0] for w in WHEELS]}, indent=2), encoding="utf-8")
    print("English and Chinese models installed. No other translation models were downloaded.", flush=True)


if __name__ == "__main__":
    try:
        main()
    except Exception as error:
        # Setup has no translation input or credentials; diagnostic text is safe here.
        print("Setup failed: " + str(error), file=sys.stderr)
        sys.exit(1)
