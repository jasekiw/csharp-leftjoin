#!/usr/bin/env python3
"""Smoke test the leftjoin language server the way the Cursor C# extension drives it.

Launches scripts/leftjoin-language-server with the extension's arguments, waits
for the pipe announcement, loads samples/EfCoreMariaDb and checks:
  - the committed Program.cs (which uses leftjoin/rightjoin) has no errors
  - a leftjoin range variable gets the nullable warning (project semantics loaded)
  - completion after a query clause offers leftjoin and rightjoin
  - "Convert to 'foreach'" is offered for join but not for leftjoin
"""

import json
import os
import pathlib
import socket
import subprocess
import sys
import tempfile
import threading
import time

REPO = pathlib.Path(__file__).resolve().parent.parent
ADAPTER = REPO / "scripts" / "leftjoin-language-server"
PROJECT = REPO / "samples" / "EfCoreMariaDb" / "EfCoreMariaDb.csproj"
PROGRAM = PROJECT.parent / "Program.cs"
TIMEOUT = float(os.environ.get("LSP_TEST_TIMEOUT", "300"))

PROBE = """
static class LspProbe
{
    static System.Collections.Generic.IEnumerable<int> Inner(int[] xs, int[] ys)
    {
        return /*inner*/from x in xs
               join y in ys on x equals y
               select x + y;
    }

    static System.Collections.Generic.IEnumerable<int> Outer(int[] xs, int[] ys)
    {
        return /*outer*/from x in xs
               leftjoin y in ys on x equals y
               select x;
    }

    static System.Collections.Generic.IEnumerable<int> Nullable(string[] xs, string[] ys)
    {
        return from x in xs
               leftjoin y in ys on x equals y
               select /*maybe-null*/y.Length;
    }

    static void Complete(int[] xs)
    {
        var q = from x in xs
                /*complete*/
    }
}
"""


class Client:
    def __init__(self, sock):
        self.sock = sock
        self.next_id = 0
        self.pending = {}
        self.notifications = []
        self.registrations = []
        self.lock = threading.Condition()
        threading.Thread(target=self._read, daemon=True).start()

    def _send(self, message):
        body = json.dumps(message).encode()
        self.sock.sendall(b"Content-Length: %d\r\n\r\n" % len(body) + body)

    def _read(self):
        buffer = b""
        while True:
            while b"\r\n\r\n" not in buffer:
                chunk = self.sock.recv(65536)
                if not chunk:
                    return
                buffer += chunk
            header, buffer = buffer.split(b"\r\n\r\n", 1)
            length = int(next(line.split(b":")[1] for line in header.split(b"\r\n")
                              if line.lower().startswith(b"content-length")))
            while len(buffer) < length:
                buffer += self.sock.recv(65536)
            message, buffer = json.loads(buffer[:length]), buffer[length:]
            self._dispatch(message)

    def _dispatch(self, message):
        if "method" in message and "id" in message:
            self._answer_server_request(message)
            return
        with self.lock:
            if "method" in message:
                self.notifications.append(message)
            else:
                self.pending[message["id"]] = message
            self.lock.notify_all()

    def _answer_server_request(self, message):
        method, params = message["method"], message.get("params") or {}
        result = None
        if method == "workspace/configuration":
            result = [None] * len(params.get("items", []))
        elif method == "client/registerCapability":
            with self.lock:
                self.registrations.extend(params.get("registrations", []))
        self._send({"jsonrpc": "2.0", "id": message["id"], "result": result})

    def request(self, method, params, timeout=TIMEOUT):
        with self.lock:
            self.next_id += 1
            request_id = self.next_id
        self._send({"jsonrpc": "2.0", "id": request_id, "method": method, "params": params})
        with self.lock:
            if not self.lock.wait_for(lambda: request_id in self.pending, timeout):
                raise TimeoutError(method)
            response = self.pending.pop(request_id)
        if "error" in response:
            raise RuntimeError(f"{method}: {response['error']}")
        return response.get("result")

    def notify(self, method, params):
        self._send({"jsonrpc": "2.0", "method": method, "params": params})

    def wait_notification(self, method, timeout=TIMEOUT):
        with self.lock:
            if not self.lock.wait_for(lambda: any(n["method"] == method for n in self.notifications), timeout):
                raise TimeoutError(method)


def committed_text(path):
    # Local edits in progress shouldn't fail the smoke test.
    try:
        return subprocess.run(
            ["git", "-C", str(REPO), "show", f"HEAD:{path.relative_to(REPO)}"],
            check=True, capture_output=True, text=True).stdout
    except (OSError, subprocess.CalledProcessError):
        return path.read_text()


def position_of(text, marker):
    index = text.index(marker) + len(marker)
    line = text.count("\n", 0, index)
    return {"line": line, "character": index - (text.rfind("\n", 0, index) + 1)}


def diagnostics(client, uri):
    identifiers = {r.get("registerOptions", {}).get("identifier")
                   for r in client.registrations if r["method"] == "textDocument/diagnostic"} or {None}
    found = []
    for identifier in identifiers:
        params = {"textDocument": {"uri": uri}}
        if identifier:
            params["identifier"] = identifier
        result = client.request("textDocument/diagnostic", params) or {}
        found.extend(result.get("items", []))
    return found


def code_action_titles(client, uri, position):
    actions = client.request("textDocument/codeAction", {
        "textDocument": {"uri": uri},
        "range": {"start": position, "end": position},
        "context": {"diagnostics": []},
    }) or []
    titles = []
    for action in actions:
        titles.append(action["title"])
        titles.extend(nested["title"] for nested in action.get("data", {}).get("NestedCodeActions", []) or [])
    return titles


def main():
    log_dir = tempfile.mkdtemp(prefix="leftjoin-lsp-logs-")
    # Same arguments as anysphere.csharp 1.0.1 in standalone mode.
    args = [
        "--logLevel", "Information",
        "--razorSourceGenerator", "/nonexistent/.razor/Microsoft.CodeAnalysis.Razor.Compiler.dll",
        "--razorDesignTimePath", "/nonexistent/.razor/Targets/Microsoft.NET.Sdk.Razor.DesignTime.targets",
        "--extension", "/nonexistent/.razor/Microsoft.VisualStudioCode.RazorExtension.dll",
        "--telemetryLevel", "off",
        "--extensionLogDirectory", log_dir,
    ]
    adapter = subprocess.Popen([str(ADAPTER), *args], stdout=subprocess.PIPE, text=True)
    failures = []
    try:
        pipe_name = None
        for line in adapter.stdout:
            print(f"  adapter: {line.rstrip()}")
            if line.startswith('{"pipeName"'):
                pipe_name = json.loads(line)["pipeName"]
                break
        if not pipe_name:
            raise RuntimeError("adapter never announced a pipe")
        threading.Thread(target=lambda: [print(f"  adapter: {l.rstrip()}") for l in adapter.stdout], daemon=True).start()

        sock = socket.socket(socket.AF_UNIX, socket.SOCK_STREAM)
        sock.connect(pipe_name)
        client = Client(sock)

        started = time.time()
        client.request("initialize", {
            "processId": os.getpid(),
            "rootUri": PROJECT.parent.as_uri(),
            "workspaceFolders": [{"uri": PROJECT.parent.as_uri(), "name": "EfCoreMariaDb"}],
            "capabilities": {
                "textDocument": {
                    "diagnostic": {"dynamicRegistration": True},
                    "completion": {"completionItem": {"snippetSupport": True}},
                    "codeAction": {"codeActionLiteralSupport": {"codeActionKind": {"valueSet": ["", "quickfix", "refactor"]}}},
                },
                "workspace": {"configuration": True},
            },
        })
        client.notify("initialized", {})
        client.notify("project/open", {"projects": [PROJECT.as_uri()]})
        client.wait_notification("workspace/projectInitializationComplete")
        print(f"project loaded in {time.time() - started:.1f}s")

        uri = PROGRAM.as_uri()
        text = committed_text(PROGRAM)
        client.notify("textDocument/didOpen", {"textDocument": {
            "uri": uri, "languageId": "csharp", "version": 1, "text": text}})

        errors = [d for d in diagnostics(client, uri) if d.get("severity") == 1]
        for d in errors:
            print(f"  error {d.get('code')} line {d['range']['start']['line'] + 1}: {d['message']}")
        print(f"Program.cs errors: {len(errors)}")
        if errors:
            failures.append("Program.cs has errors")

        probed = text + PROBE
        client.notify("textDocument/didChange", {
            "textDocument": {"uri": uri, "version": 2},
            "contentChanges": [{"text": probed}],
        })

        nullable_line = position_of(probed, "/*maybe-null*/")["line"]
        warned = [d for d in diagnostics(client, uri)
                  if d.get("code") == "CS8602" and d["range"]["start"]["line"] == nullable_line]
        print(f"CS8602 on the leftjoin range variable: {'yes' if warned else 'no'}")
        if not warned:
            failures.append("no nullable warning for leftjoin range variable (project semantics not loaded?)")

        completion = client.request("textDocument/completion", {
            "textDocument": {"uri": uri}, "position": position_of(probed, "/*complete*/"),
        }) or {}
        labels = {item["label"] for item in (completion.get("items", []) if isinstance(completion, dict) else completion)}
        offered = sorted(labels & {"join", "leftjoin", "rightjoin", "where", "select"})
        print(f"query keywords offered: {offered}")
        for keyword in ("leftjoin", "rightjoin"):
            if keyword not in labels:
                failures.append(f"completion missing {keyword}")

        inner = code_action_titles(client, uri, position_of(probed, "/*inner*/"))
        outer = code_action_titles(client, uri, position_of(probed, "/*outer*/"))
        print(f"code actions on join query: {inner}")
        print(f"code actions on leftjoin query: {outer}")
        if not any("foreach" in t for t in inner):
            failures.append("Convert to foreach missing for join (probe is not exercising the refactoring)")
        if any("foreach" in t for t in outer):
            failures.append("Convert to foreach offered for leftjoin")

        try:
            client.request("shutdown", None, timeout=30)
            client.notify("exit", None)
        except (TimeoutError, OSError):
            pass
    finally:
        try:
            adapter.wait(timeout=15)
        except subprocess.TimeoutExpired:
            adapter.terminate()

    if failures:
        print("FAILED:\n  " + "\n  ".join(failures))
        return 1
    print("All language server checks passed")
    return 0


if __name__ == "__main__":
    sys.exit(main())
