"""Minimal client for the accetsgame Blender bridge (localhost:9876).

Wire format, one request/response per connection:

    request   <8 hex digits of body length> <JSON body>
    response  <8 hex digits of body length> <JSON body>

The JSON body carries the snippet to run at the TOP level:

    {"code": "import bpy; print(bpy.app.version_string)"}
    -> {"ok": true, "out": "...", "err": "", "result": null, "state": {...}}

Usage:
    python blender_bridge_client.py --code "import bpy; print(bpy.app.version_string)"
    python blender_bridge_client.py --file some_snippet.py
    python blender_bridge_client.py --state-only
"""

import argparse
import json
import socket
import sys

HOST = "127.0.0.1"
PORT = 9876


def call(code, host=HOST, port=PORT, timeout=60.0):
    body = json.dumps({"code": code}).encode("utf-8")
    frame = ("%08x" % len(body)).encode("ascii") + body

    with socket.create_connection((host, port), timeout=timeout) as sock:
        sock.settimeout(timeout)
        sock.sendall(frame)

        header = b""
        while len(header) < 8:
            chunk = sock.recv(8 - len(header))
            if not chunk:
                raise ConnectionError("bridge closed before sending a header")
            header += chunk

        length = int(header.decode("ascii"), 16)
        payload = b""
        while len(payload) < length:
            chunk = sock.recv(length - len(payload))
            if not chunk:
                raise ConnectionError("bridge closed mid-body")
            payload += chunk

    return json.loads(payload.decode("utf-8"))


def main():
    parser = argparse.ArgumentParser()
    group = parser.add_mutually_exclusive_group(required=True)
    group.add_argument("--code", help="snippet to run inside Blender")
    group.add_argument("--file", help="path to a .py snippet to run")
    group.add_argument("--state-only", action="store_true",
                       help="send an empty snippet and print just the scene snapshot")
    parser.add_argument("--host", default=HOST)
    parser.add_argument("--port", type=int, default=PORT)
    parser.add_argument("--timeout", type=float, default=60.0)
    parser.add_argument("--no-state", action="store_true",
                        help="suppress the scene snapshot in the output")
    args = parser.parse_args()

    if args.file:
        with open(args.file, "r", encoding="utf-8") as handle:
            code = handle.read()
    elif args.state_only:
        code = ""
    else:
        code = args.code

    try:
        reply = call(code, args.host, args.port, args.timeout)
    except Exception as exc:
        print("BRIDGE ERROR: %s: %s" % (type(exc).__name__, exc), file=sys.stderr)
        return 2

    if args.no_state:
        reply.pop("state", None)

    print("ok=%s" % reply.get("ok"))
    if reply.get("out"):
        print("--- out ---")
        print(reply["out"], end="" if reply["out"].endswith("\n") else "\n")
    if reply.get("err"):
        print("--- err ---", file=sys.stderr)
        print(reply["err"], end="" if reply["err"].endswith("\n") else "\n", file=sys.stderr)
    if reply.get("result") is not None:
        print("--- result ---")
        print(json.dumps(reply["result"], ensure_ascii=False, indent=2, default=str))
    if "state" in reply:
        print("--- state ---")
        print(json.dumps(reply["state"], ensure_ascii=False, indent=2, default=str))

    return 0 if reply.get("ok") else 1


if __name__ == "__main__":
    sys.exit(main())
