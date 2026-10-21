"""stdout protocol: one JSON object per line, nothing else. Logs go to stderr."""
import json
import sys

STAGES = ("load", "diffusion", "estimation", "style", "postprocess", "validate", "write")


def _emit(event):
    sys.stdout.write(json.dumps(event) + "\n")
    sys.stdout.flush()


def progress(stage, value):
    _emit({"event": "progress", "stage": stage, "progress": round(float(value), 4)})


def warning(code, message):
    _emit({"event": "warning", "code": code, "message": message})


def done(**fields):
    _emit({"event": "done", **fields})


def error(code, message):
    _emit({"event": "error", "code": code, "message": message})


def log(message):
    print(message, file=sys.stderr, flush=True)
