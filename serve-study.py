#!/usr/bin/env python3
"""Serve the study to the headset, and write everything it sends back.

    ./serve-study.py

WebXR only runs in a secure context. Over localhost that is automatic; over a LAN
address -- which is what the headset has to use -- it means HTTPS, so this generates a
self-signed certificate on first run. The browser will warn about it once and the warning
is correct: the certificate is not signed by anyone. It is also serving only your own
machine to your own headset over your own network.

Data lands in runs/web-data/, in the same shape the Unity build writes:

    <participant>_events.csv        one row per discrete event
    <participant>_telemetry.csv     20 Hz, every column every row
    <participant>_responses.csv     one row per trial and per review trial
    <participant>_questionnaires.csv one row per questionnaire item
    bundles/<participant>_all.csv   everything above, joined, written on request
"""

from __future__ import annotations

import csv
import json
import shutil
import socket
import ssl
import subprocess
import sys
from datetime import datetime, timezone
from http.server import SimpleHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path

ROOT = Path(__file__).resolve().parent
WEB = ROOT / "web"
DATA = ROOT / "runs" / "web-data"
CERT = ROOT / "runs" / "study-cert.pem"
KEY = ROOT / "runs" / "study-key.pem"
PORT = 8443
REDIRECT_PORT = 8080   # plain HTTP, only to bounce a short typed address to HTTPS


def local_ip() -> str:
    """The address the headset should use. No packet is actually sent."""
    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    try:
        sock.connect(("10.255.255.255", 1))
        return sock.getsockname()[0]
    except OSError:
        return "127.0.0.1"
    finally:
        sock.close()


def ensure_certificate() -> bool:
    if CERT.exists() and KEY.exists():
        return True
    if shutil.which("openssl") is None:
        print("error: openssl not found, so no HTTPS certificate can be made.")
        return False

    CERT.parent.mkdir(parents=True, exist_ok=True)
    ip = local_ip()
    # The IP goes in subjectAltName: browsers reject a certificate whose name does not
    # match the address, and the headset reaches this by IP rather than by hostname.
    subprocess.run(
        [
            "openssl", "req", "-x509", "-newkey", "rsa:2048", "-nodes",
            "-keyout", str(KEY), "-out", str(CERT), "-days", "365",
            "-subj", "/CN=emotion-rooms",
            "-addext", f"subjectAltName=IP:{ip},IP:127.0.0.1,DNS:localhost",
        ],
        check=True, capture_output=True,
    )
    print(f"made a certificate for {ip}")
    return True


def append_rows(path: Path, rows: list[dict]) -> None:
    """Append, widening the header if a later row carries a new column.

    Rewriting on a new column rather than dropping it: the alternative is silently
    discarding a field because the first row of the session happened not to have it.
    """
    if not rows:
        return
    path.parent.mkdir(parents=True, exist_ok=True)

    existing: list[dict] = []
    columns: list[str] = []
    if path.exists():
        with path.open(newline="", encoding="utf-8") as handle:
            reader = csv.DictReader(handle)
            columns = list(reader.fieldnames or [])
            new_columns = [k for row in rows for k in row if k not in columns]
            if new_columns:
                existing = list(reader)

    fresh = [k for row in rows for k in row if k not in columns]
    for key in fresh:
        if key not in columns:
            columns.append(key)

    if existing or not path.exists():
        with path.open("w", newline="", encoding="utf-8") as handle:
            writer = csv.DictWriter(handle, fieldnames=columns, extrasaction="ignore")
            writer.writeheader()
            writer.writerows(existing)
            writer.writerows(rows)
    else:
        with path.open("a", newline="", encoding="utf-8") as handle:
            csv.DictWriter(handle, fieldnames=columns, extrasaction="ignore").writerows(rows)


def bundle(participant: str) -> dict:
    """Join this participant's files into one, the same shape as the Unity bundle."""
    rows: list[dict] = []
    for suffix, source in (
        ("responses", "response"),
        ("questionnaires", "questionnaire"),
        ("events", "event"),
        ("telemetry", "telemetry"),
    ):
        path = DATA / f"{participant}_{suffix}.csv"
        if not path.exists():
            continue
        with path.open(newline="", encoding="utf-8") as handle:
            for row in csv.DictReader(handle):
                rows.append({"source": source, "source_file": path.name, **row})

    if not rows:
        return {"error": f"no data yet for {participant}"}

    columns: list[str] = ["source", "source_file"]
    for row in rows:
        for key in row:
            if key not in columns:
                columns.append(key)

    out = DATA / "bundles" / f"{participant}_all.csv"
    out.parent.mkdir(parents=True, exist_ok=True)
    with out.open("w", newline="", encoding="utf-8") as handle:
        writer = csv.DictWriter(handle, fieldnames=columns, extrasaction="ignore")
        writer.writeheader()
        writer.writerows(rows)
    return {"file": str(out.relative_to(ROOT)), "rows": len(rows)}


# Shared state between the researcher's laptop and the headset.
#
# The headset is for the rooms and nothing else. It opens one page once, the participant
# presses one button to enter VR, and after that it is driven entirely from the laptop:
# the researcher presses Start and the session begins in the headset. Nobody types or
# reads a questionnaire with a headset on, which was the whole point and which the first
# version got backwards.
STATE = {
    "command": "idle",      # idle | run | stop
    "participant": "",
    "headset": "away",      # away | ready | in_vr | running | finished
    "trial": 0,
    "of": 0,
    "seen": 0.0,            # when the headset last checked in
    "bundled": "",          # who has already had their combined file written
}


def participant_of(state: dict) -> str:
    return state.get("participant") or "unknown"


class Handler(SimpleHTTPRequestHandler):
    def __init__(self, *args, **kwargs):
        super().__init__(*args, directory=str(WEB), **kwargs)

    def log_message(self, fmt, *args):
        if "POST" not in fmt % args:
            return
        sys.stderr.write("  %s\n" % (fmt % args))

    def _json(self, payload: dict, code: int = 200) -> None:
        body = json.dumps(payload).encode()
        self.send_response(code)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def _body(self) -> dict:
        length = int(self.headers.get("Content-Length", 0))
        return json.loads(self.rfile.read(length) or b"{}")

    def do_GET(self):
        # Short aliases. Typing "https://192.168.1.23:8443/vr.html" on a virtual keyboard
        # inside a headset is the single most error-prone step in the whole setup, so the
        # headset page answers to /v as well.
        if self.path in ("/v", "/v/"):
            self.path = "/vr.html"

        if self.path == "/info":
            # The page cannot work its own LAN address out, and location.origin is
            # whatever the researcher typed -- which was localhost, so the address shown
            # for the headset pointed the headset at itself. Only the server knows.
            self._json({"ip": local_ip(), "port": PORT,
                        "short": f"{local_ip()}:{REDIRECT_PORT}",
                        "headset_url": f"https://{local_ip()}:{PORT}/vr.html"})
            return

        if self.path == "/state":
            import time

            state = dict(STATE)
            # The headset is only "there" if it checked in recently. Without this the
            # panel would keep claiming a headset that has been taken off and put down.
            state["connected"] = (time.time() - STATE["seen"]) < 4.0
            self._json(state)
            return

        if self.path.startswith("/bundle"):
            from urllib.parse import parse_qs, urlparse

            participant = parse_qs(urlparse(self.path).query).get("participant", [""])[0]
            self._json(bundle(participant))
            return
        super().do_GET()

    def do_POST(self):
        try:
            payload = self._body()
        except (ValueError, json.JSONDecodeError):
            self._json({"error": "bad json"}, 400)
            return

        participant = payload.get("participant", "unknown")

        if self.path == "/log":
            rows = payload.get("rows", [])
            events = [r for r in rows if r.get("source") == "event"]
            telemetry = [r for r in rows if r.get("source") == "telemetry"]
            append_rows(DATA / f"{participant}_events.csv", events)
            append_rows(DATA / f"{participant}_telemetry.csv", telemetry)
            self._json({"written": len(rows)})

        elif self.path == "/responses":
            # Rewritten whole each time. The page holds every response it has collected,
            # so the file is a snapshot rather than an append log, and a retry after a
            # dropped request cannot duplicate a trial.
            rows = payload.get("rows", [])
            path = DATA / f"{participant}_responses.csv"
            if rows:
                path.parent.mkdir(parents=True, exist_ok=True)
                columns: list[str] = []
                for row in rows:
                    for key in row:
                        if key not in columns:
                            columns.append(key)
                with path.open("w", newline="", encoding="utf-8") as handle:
                    writer = csv.DictWriter(handle, fieldnames=columns, extrasaction="ignore")
                    writer.writeheader()
                    writer.writerows(rows)
            self._json({"written": len(rows)})

        elif self.path == "/command":
            # From the laptop panel.
            for key in ("command", "participant"):
                if key in payload:
                    STATE[key] = payload[key]
            print(f"  panel: {STATE['command']} {STATE['participant']}")
            self._json(dict(STATE))

        elif self.path == "/headset":
            # From the headset, a few times a second.
            import time

            STATE["seen"] = time.time()
            for key in ("headset", "trial", "of"):
                if key in payload:
                    STATE[key] = payload[key]
            # Consumed once: the headset has picked the command up, so it should not
            # start a second session on the next poll.
            if STATE["command"] == "run" and payload.get("headset") == "running":
                STATE["command"] = "idle"

            # Combine automatically at the end. Asking a researcher to press a button
            # after the participant has left is asking for a file that never gets written.
            if payload.get("headset") == "finished" and STATE.get("bundled") != participant_of(STATE):
                result = bundle(participant_of(STATE))
                STATE["bundled"] = participant_of(STATE)
                print(f"  session finished -- {result.get('file', result.get('error'))}")
            self._json({"command": payload.get("ack") and "idle" or STATE["command"],
                        "participant": STATE["participant"]})

        elif self.path == "/form-submit":
            answers = payload.get("answers", {})
            items = payload.get("items", list(answers))
            answered = sum(1 for i in items if answers.get(i))
            state = ("Completed" if answered == len(items)
                     else "PartlyAnswered" if answered else "Skipped")
            rows = [
                {
                    "participant": participant,
                    "form": payload.get("form", ""),
                    "item": item,
                    "answer": answers.get(item, ""),
                    "state": state,
                    "answered_items": answered,
                    "total_items": len(items),
                    "utc": datetime.now(timezone.utc).isoformat(),
                }
                for item in items
            ]
            append_rows(DATA / f"{participant}_questionnaires.csv", rows)
            print(f"  {payload.get('form')} submitted for {participant} "
                  f"({answered}/{len(items)})")
            self._json({"state": state})

        else:
            self._json({"error": "unknown endpoint"}, 404)


class RedirectHandler(SimpleHTTPRequestHandler):
    """Bounce plain HTTP to the HTTPS study.

    So the address typed into the headset can be `192.168.1.23:8080` -- no scheme, no
    path, no punctuation beyond a colon and a dot. The browser assumes http, lands here,
    and is sent on. WebXR still runs over HTTPS; this only removes the typing.
    """

    def do_GET(self):
        target = f"https://{local_ip()}:{PORT}/vr.html"
        self.send_response(302)
        self.send_header("Location", target)
        self.send_header("Content-Length", "0")
        self.end_headers()

    def log_message(self, fmt, *args):
        pass


def start_redirect() -> None:
    import threading

    try:
        server = ThreadingHTTPServer(("0.0.0.0", REDIRECT_PORT), RedirectHandler)
    except OSError:
        return
    threading.Thread(target=server.serve_forever, daemon=True).start()


def main() -> int:
    if not WEB.is_dir():
        print(f"error: no web/ folder at {WEB}")
        return 1
    if not (WEB / "data" / "participants" / "index.json").exists():
        print("error: web/data/participants is missing. Build it with:\n"
              "  python3 -m pipeline.cli build-participants --count 30\n"
              "  cp -R unity/Assets/StreamingAssets/participants web/data/")
        return 1
    if not ensure_certificate():
        return 1

    DATA.mkdir(parents=True, exist_ok=True)

    context = ssl.SSLContext(ssl.PROTOCOL_TLS_SERVER)
    context.load_cert_chain(CERT, KEY)

    try:
        server = ThreadingHTTPServer(("0.0.0.0", PORT), Handler)
    except OSError as exc:
        # Exiting silently here looked exactly like a server that started and stopped,
        # which is what a second copy of the study does to the first.
        print(f"error: port {PORT} is already in use ({exc}).")
        print("Another copy of the study server is running. Stop it first:")
        print("  pkill -f serve-study.py")
        return 1

    server.socket = context.wrap_socket(server.socket, server_side=True)
    start_redirect()

    ip = local_ip()
    print()
    print("  Emotion Rooms is being served.")
    print()
    print(f"    TYPE THIS IN THE HEADSET:   {ip}:{REDIRECT_PORT}")
    print(f"    Researcher panel (here):    https://localhost:{PORT}/")
    print()
    print("  The headset will warn that the certificate is not trusted. That is expected:")
    print("  tap Advanced, then Proceed. WebXR needs HTTPS and this is a self-signed")
    print("  certificate for your own machine.")
    print()
    print(f"  Data is written to {DATA.relative_to(ROOT)}/ as it arrives.")
    print("  Ctrl-C to stop.")
    print()

    try:
        server.serve_forever()
    except KeyboardInterrupt:
        print("\nstopped")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
