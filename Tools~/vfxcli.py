#!/usr/bin/env python3
"""One command for agents: run a com.cuvara.vfx verb in a Unity project and print its JSON result.

  python Tools~/vfxcli.py <verb> [--project <unity project>] [--args <file.json> | --json '<object>']
                                 [--out <file.json>] [--mode auto|editor|batch] [--timeout <seconds>]

The verb runs inside Unity through `Cuvara.VFX.Editor.VfxCli`:

- editor: an editor already has the project open. The verb runs through
  `unity-mcp-cli run-tool script-execute <open path>`, which calls `VfxCli.Run`. Arguments and the
  result travel through temporary files, because script-execute cuts a result above 20000 characters.
- batch: no editor has the project open. A batch-mode editor WITH graphics (never -nographics: VFX
  Graph needs a GPU) runs `-executeMethod Cuvara.VFX.Editor.VfxCli.Batch`. Its private bytes are
  watched against the Sandbox memory limit (default 12 GB, CUVARA_VFX_SANDBOX_MEMORY_LIMIT_GB) and it
  is killed above it, by its PID only.
- auto (default): editor when one has the project open, else batch.

`--project Sandbox~` (the default), the full Sandbox~ path and the open path all mean the agent-owned
Sandbox; they resolve to the open path through `sandbox_paths.resolve_project`. When the open path or
the package link is missing the command fails with exit code 3 and names
`python Tools~/setup-sandbox.py`; it never creates links itself.

The result is `{"ok", "verb", "errors", "warnings", ...payload}` (Documentation~/DESIGN.md, "Agent
interface"). It is printed on stdout (indented JSON) and, with --out, written to that file with the
same text, so two runs on the same input give the same bytes. Which runner was used (editor PID,
batch exit code, time, peak memory) goes to stderr with the rest of the progress.

Path arguments (the keys recipe, asset, effect, source, target, out, file and texture, at any depth of
the arguments) may be written as DESIGN.md writes them, relative to the current directory. A relative
path that does not start with Assets/ or Packages/ is resolved against the current directory; a path
inside the repository is then passed as Packages/<package name>/<path in the repository>, a path inside
Sandbox~/Assets or Sandbox~/Packages as Assets/... or Packages/..., and any other path (and every `out`
path) as an absolute path. An absolute path inside Sandbox~/ is mapped the same way, because Unity sees
the project through the open path, not through Sandbox~. Each rewrite is logged on stderr.

A verb that runs across play mode (capture) answers through the editor with `"pending": true` and a
`result_file`; vfxcli.py waits for that file (the editor writes it once it is back in edit mode) and
prints the final result. Through a batch-mode editor the job writes the result and exits the editor.

Exit codes: 0 ok, 1 the verb reported "ok": false (or the call into Unity failed), 2 usage,
3 no editor available (no open path or package link, the project is open but Unity-MCP does not
answer, or no Unity editor is installed for the project's version).

Standard library only.
"""

import argparse
import importlib.util
import json
import os
import re
import shutil
import subprocess
import sys
import tempfile
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import sandbox_paths as sp  # noqa: E402

EXIT_OK, EXIT_FAILED, EXIT_USAGE, EXIT_NO_EDITOR = 0, 1, 2, 3
VERB_PATTERN = re.compile(r"^[a-z][a-z0-9-]*$")
SETUP_HINT = "run 'python Tools~/setup-sandbox.py' (once per clone) and 'python Tools~/setup-sandbox.py --open'"
GB = 1024 ** 3


class CliError(Exception):
    """A failure that ends the command with an exit code and an envelope."""

    def __init__(self, code, message):
        super().__init__(message)
        self.code = code


def log(message):
    print("[vfxcli] " + message, file=sys.stderr, flush=True)


def load_setup_module():
    """Tools~/setup-sandbox.py as a module (its name has a hyphen): editor lookup, process memory."""
    path = os.path.join(os.path.dirname(os.path.abspath(__file__)), "setup-sandbox.py")
    spec = importlib.util.spec_from_file_location("cuvara_setup_sandbox", path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def envelope(verb, errors, warnings=None, **payload):
    result = {"ok": not errors, "verb": verb, "errors": list(errors), "warnings": list(warnings or [])}
    result.update(payload)
    return result


# ---------------------------------------------------------------- project


def resolve(project_arg, root):
    """(project path to pass to Unity, is_sandbox). Raises CliError(3) when the Sandbox is not set up."""
    project = sp.resolve_project(project_arg or sp.SANDBOX_DIR_NAME, root)
    is_sandbox = sp.same_path(project, sp.open_path(root))
    if is_sandbox:
        missing = []
        if not os.path.isdir(sp.open_path(root)):
            missing.append("open path " + sp.open_path(root))
        if not os.path.isdir(sp.package_link(root)):
            missing.append("package link " + sp.package_link(root))
        if missing:
            raise CliError(EXIT_NO_EDITOR, "the Sandbox is not set up (missing " + " and ".join(missing) + "): " + SETUP_HINT)
    if not os.path.isfile(os.path.join(project, "ProjectSettings", "ProjectVersion.txt")):
        raise CliError(EXIT_NO_EDITOR, "not a Unity project (no ProjectSettings/ProjectVersion.txt): " + project)
    return project, is_sandbox


def running_editors(setup, project, is_sandbox, root):
    """PIDs of editors that have the project open (import workers excluded)."""
    if is_sandbox:
        return setup.sandbox_editors(root)
    rows = setup.list_processes()
    found = []
    for pid, _ppid, name, command_line in rows:
        if not name.lower().startswith("unity") or "AssetImportWorker" in command_line:
            continue
        if "-projectpath" in command_line.lower() and setup._mentions(command_line, project):
            found.append(pid)
    return sorted(set(found))


# ---------------------------------------------------------------- editor path (Unity-MCP)


def csharp_verbatim(text):
    return '@"' + text.replace('"', '""') + '"'


def run_in_editor(verb, args_path, out_path, project, timeout):
    cli = shutil.which("unity-mcp-cli")
    if cli is None:
        raise CliError(EXIT_NO_EDITOR, "an editor has the project open, but unity-mcp-cli is not on PATH")
    code = (
        "var vfxArgs = System.IO.File.ReadAllText({args});\n"
        "var vfxResult = Cuvara.VFX.Editor.VfxCli.Run({verb}, vfxArgs);\n"
        "System.IO.File.WriteAllText({out}, vfxResult, new System.Text.UTF8Encoding(false));\n"
        "\"vfxcli wrote \" + vfxResult.Length + \" characters\""
    ).format(args=csharp_verbatim(args_path), verb=csharp_verbatim(verb), out=csharp_verbatim(out_path))
    snippet_timeout = max(10, min(int(timeout), 280))
    tool_input = os.path.join(os.path.dirname(args_path), "tool-input.json")
    with open(tool_input, "w", encoding="utf-8", newline="\n") as handle:
        json.dump({"csharpCode": code, "isMethodBody": True, "timeout": snippet_timeout}, handle)

    command = [cli, "run-tool", "script-execute", project, "--input-file", tool_input, "--raw",
               "--timeout", str((snippet_timeout + 15) * 1000)]
    log("editor: unity-mcp-cli run-tool script-execute (timeout %d s)" % snippet_timeout)
    completed = subprocess.run(command, capture_output=True, text=True, encoding="utf-8", errors="replace")
    text = (completed.stdout or "") + (completed.stderr or "")
    response = None
    for line in (completed.stdout or "").splitlines():
        line = line.strip()
        if line.startswith("{"):
            try:
                response = json.loads(line)
                break
            except ValueError:
                continue
    if response is None:
        if re.search(r"ECONNREFUSED|connection refused|not reachable|Not available", text, re.IGNORECASE):
            raise CliError(EXIT_NO_EDITOR, "an editor has the project open, but Unity-MCP does not answer: " + tail(text))
        raise CliError(EXIT_FAILED, "unity-mcp-cli gave no JSON response (exit %d): %s" % (completed.returncode, tail(text)))
    if response.get("status") != "success":
        raise CliError(EXIT_FAILED, "script-execute failed: " + tail(json.dumps(response.get("content") or response)))
    if not os.path.isfile(out_path):
        raise CliError(EXIT_FAILED, "script-execute succeeded but wrote no result: " + tail(json.dumps(response)))
    with open(out_path, encoding="utf-8") as handle:
        return json.load(handle)


PENDING_TIMEOUT = 900
"""Default seconds to wait for the result of a pending verb (capture) through the editor."""


def wait_for_pending(pending, still_running, editors, timeout):
    """A verb that runs across play mode (capture) returns `"pending": true` and the `result_file` the
    editor writes when it is back in edit mode. Wait for that file and return its result."""
    result_file = pending.get("result_file")
    if not result_file:
        raise CliError(EXIT_FAILED, "the verb is pending but gave no result_file: " + tail(json.dumps(pending)))
    log("pending job %s: waiting up to %d s for %s" % (pending.get("job"), timeout, result_file))
    started = time.monotonic()
    last_note = started
    while True:
        if os.path.isfile(result_file):
            try:
                with open(result_file, encoding="utf-8") as handle:
                    result = json.load(handle)
                log("pending job %s ended after %.0f s" % (pending.get("job"), time.monotonic() - started))
                return result
            except ValueError:
                pass  # written through a temporary name; a partial read is retried
        if time.monotonic() - started > timeout:
            raise CliError(EXIT_FAILED, "no result in %s after %d s (the editor may still be in play mode)" % (result_file, timeout))
        if time.monotonic() - last_note > 30:
            last_note = time.monotonic()
            if not still_running():
                raise CliError(EXIT_FAILED, "the editor (PID %s) ended before the pending job wrote %s"
                               % (", ".join(map(str, editors)), result_file))
            log("still waiting for job %s (%.0f s)" % (pending.get("job"), time.monotonic() - started))
        time.sleep(1)


# ---------------------------------------------------------------- batch path


def unity_executable(setup, project):
    version = setup.editor_version(project)
    if not version:
        raise CliError(EXIT_NO_EDITOR, "no m_EditorVersion in %s/ProjectSettings/ProjectVersion.txt" % project)
    for candidate in setup.editor_candidates(version):
        if os.path.isfile(candidate):
            return candidate, version
    raise CliError(EXIT_NO_EDITOR, "Unity %s is not installed (set UNITY_EDITOR_PATH to override)" % version)


def run_in_batch(setup, verb, args_path, out_path, project, timeout, memory_limit_gb):
    unity, version = unity_executable(setup, project)
    log_path = os.path.join(os.path.dirname(args_path), "batch-editor.log")
    command = [unity, "-batchmode", "-projectPath", project,
               "-executeMethod", "Cuvara.VFX.Editor.VfxCli.Batch",
               "-vfxVerb", verb, "-vfxArgs", args_path, "-vfxOut", out_path, "-logFile", log_path]
    log("batch: Unity %s -batchmode (with graphics) -executeMethod Cuvara.VFX.Editor.VfxCli.Batch" % version)
    started = time.monotonic()
    child = subprocess.Popen(command, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    watched = setup.Process(child.pid)
    peak = 0.0
    killed = None
    try:
        while child.poll() is None:
            private, _working = watched.memory()
            if private is not None:
                peak = max(peak, private / GB)
                if private / GB > memory_limit_gb:
                    killed = "private bytes %.2f GB above the limit of %.1f GB" % (private / GB, memory_limit_gb)
            if killed is None and time.monotonic() - started > timeout:
                killed = "no exit after %d s" % timeout
            if killed:
                log("killing batch editor PID %d: %s" % (child.pid, killed))
                if sys.platform == "win32":
                    subprocess.run(["taskkill", "/PID", str(child.pid), "/T", "/F"], capture_output=True, text=True)
                else:
                    child.kill()
                child.wait(timeout=60)
                break
            time.sleep(2)
    finally:
        watched.close()
    elapsed = time.monotonic() - started
    exit_code = child.returncode
    log("batch editor PID %d ended with exit code %s after %.0f s, peak %.2f GB private" % (child.pid, exit_code, elapsed, peak))

    batch = {"pid": child.pid, "exit_code": exit_code, "seconds": round(elapsed, 1), "peak_private_gb": round(peak, 2),
             "log": log_path}
    if killed:
        raise CliError(EXIT_FAILED, "batch editor killed: %s; log: %s" % (killed, log_tail(log_path)))
    if not os.path.isfile(out_path):
        raise CliError(EXIT_FAILED, "batch editor exit code %s and no result file; log: %s" % (exit_code, log_tail(log_path)))
    with open(out_path, encoding="utf-8") as handle:
        result = json.load(handle)
    if exit_code not in (0, 1, 2):
        result.setdefault("warnings", []).append("batch editor exit code %s" % exit_code)
    return result, batch


def log_tail(path, lines=20):
    try:
        with open(path, encoding="utf-8", errors="replace") as handle:
            content = handle.read().splitlines()
    except OSError:
        return "(no log at %s)" % path
    interesting = [line for line in content if re.search(r"error|exception|vfxcli", line, re.IGNORECASE)]
    return " | ".join((interesting or content)[-lines:])


def tail(text, limit=1500):
    text = (text or "").strip()
    return text if len(text) <= limit else "..." + text[-limit:]


# ---------------------------------------------------------------- main


def parse_args(argv):
    parser = argparse.ArgumentParser(
        prog="vfxcli.py", description="Run a com.cuvara.vfx verb in a Unity project and print its JSON result.")
    parser.add_argument("verb", help="catalog, describe, build, apply, import-texture, inspect, capture, ...")
    parser.add_argument("--project", default=sp.SANDBOX_DIR_NAME,
                        help="Unity project; Sandbox~ (default), its full path or the open path mean the Sandbox")
    source = parser.add_mutually_exclusive_group()
    source.add_argument("--args", dest="args_file", help="JSON file with the verb's arguments (an object)")
    source.add_argument("--json", dest="args_json", help="the verb's arguments as inline JSON (an object)")
    parser.add_argument("--out", help="also write the result to this file")
    parser.add_argument("--mode", choices=("auto", "editor", "batch"), default="auto",
                        help="auto (default): the open editor through Unity-MCP, else a batch-mode editor")
    parser.add_argument("--timeout", type=int, default=None,
                        help="seconds for the verb (editor: default 280, at most 280, and for a pending verb such "
                             "as capture the wait for its result, default 900; batch: default 1800)")
    parser.add_argument("--memory-limit-gb", type=float,
                        default=float(os.environ.get("CUVARA_VFX_SANDBOX_MEMORY_LIMIT_GB", "12")),
                        help="batch mode: kill the editor above this many GB of private bytes (default 12)")
    return parser.parse_args(argv)


def read_verb_args(options):
    """The arguments as JSON text of an object. Raises CliError(2) when unreadable."""
    if options.args_file:
        try:
            with open(options.args_file, encoding="utf-8-sig") as handle:
                text = handle.read()
        except OSError as error:
            raise CliError(EXIT_USAGE, "cannot read --args %s: %s" % (options.args_file, error))
    else:
        text = options.args_json or "{}"
    try:
        value = json.loads(text)
    except ValueError as error:
        raise CliError(EXIT_USAGE, "the arguments are not valid JSON: %s" % error)
    if not isinstance(value, dict):
        raise CliError(EXIT_USAGE, "the arguments must be a JSON object")
    changes = []
    value = resolve_path_args(value, sp.repo_root(), os.getcwd(), changes)
    for key, before, after in changes:
        log("path argument %s: %s -> %s" % (key, before, after))
    return json.dumps(value, ensure_ascii=False)


PATH_KEYS = ("recipe", "asset", "effect", "source", "target", "out", "file", "texture")
"""Argument keys whose string values are paths; rewritten by `resolve_path_args` at any depth."""

ABSOLUTE_KEYS = ("out",)
"""Path keys the verb writes with plain file IO (a folder for results): always passed as absolute paths."""


def package_name(root):
    try:
        with open(os.path.join(root, "package.json"), encoding="utf-8-sig") as handle:
            name = json.load(handle).get("name")
    except (OSError, ValueError, AttributeError):
        name = None
    return name if isinstance(name, str) and name else sp.PACKAGE_NAME


def resolve_path(key, value, root, cwd):
    """The form of one path argument the verbs accept (see the module docstring)."""
    if not isinstance(value, str) or not value.strip():
        return value
    text = value.strip().replace("\\", "/")
    project_form = text.startswith("Assets/") or text == "Assets" or text.startswith("Packages/")
    if project_form and not os.path.isabs(text):
        return value
    rooted = os.path.isabs(text)
    full = os.path.normpath(text if rooted else os.path.join(cwd, text))
    sandbox = sp.sandbox_dir(root)
    if sp.is_inside(full, sandbox):
        relative = os.path.relpath(full, sandbox).replace("\\", "/")
        if key not in ABSOLUTE_KEYS and (relative.startswith("Assets/") or relative == "Assets" or relative.startswith("Packages/")):
            return relative
        return full.replace("\\", "/")
    if rooted:
        return value
    if key not in ABSOLUTE_KEYS and sp.is_inside(full, root):
        relative = os.path.relpath(full, root).replace("\\", "/")
        return "Packages/" + package_name(root) + ("" if relative == "." else "/" + relative)
    return full.replace("\\", "/")


def resolve_path_args(value, root, cwd, changes, key=None):
    """`value` with every path argument rewritten by `resolve_path`; (key, before, after) appended to `changes`."""
    if isinstance(value, dict):
        return {k: resolve_path_args(v, root, cwd, changes, k) for k, v in value.items()}
    if isinstance(value, list):
        return [resolve_path_args(item, root, cwd, changes, key) for item in value]
    if key in PATH_KEYS and isinstance(value, str):
        resolved = resolve_path(key, value, root, cwd)
        if resolved != value:
            changes.append((key, value, resolved))
        return resolved
    return value


def emit(result, out):
    text = json.dumps(result, indent=2, ensure_ascii=False) + "\n"
    if out:
        directory = os.path.dirname(os.path.abspath(out))
        os.makedirs(directory, exist_ok=True)
        with open(out, "w", encoding="utf-8", newline="\n") as handle:
            handle.write(text)
    stream = getattr(sys.stdout, "buffer", None)
    if stream is not None:  # UTF-8 whatever the console code page is (graph names can hold any character)
        stream.write(text.encode("utf-8"))
        stream.flush()
    else:
        sys.stdout.write(text)
        sys.stdout.flush()


def main(argv=None):
    options = parse_args(argv)
    verb = options.verb
    root = sp.repo_root()
    workdir = None
    try:
        if not VERB_PATTERN.match(verb):
            raise CliError(EXIT_USAGE, "verb '%s' is not a verb name (lower case letters, digits and '-')" % verb)
        verb_args = read_verb_args(options)
        project, is_sandbox = resolve(options.project, root)
        setup = load_setup_module()
        editors = running_editors(setup, project, is_sandbox, root)

        workdir = tempfile.mkdtemp(prefix="vfxcli-")
        args_path = os.path.join(workdir, "args.json")
        out_path = os.path.join(workdir, "result.json")
        with open(args_path, "w", encoding="utf-8", newline="\n") as handle:
            handle.write(verb_args)

        mode = options.mode
        if mode == "auto":
            mode = "editor" if editors else "batch"
        if mode == "editor":
            if not editors:
                raise CliError(EXIT_NO_EDITOR, "no editor has %s open: %s" % (project, SETUP_HINT if is_sandbox else "open it first"))
            log("editor PID %s has %s open" % (", ".join(map(str, editors)), project))
            result = run_in_editor(verb, args_path, out_path, project, min(options.timeout or 280, 280))
            if result.get("pending") is True:
                result = wait_for_pending(result, lambda: running_editors(setup, project, is_sandbox, root), editors,
                                          options.timeout or PENDING_TIMEOUT)
        else:
            if editors:
                raise CliError(EXIT_NO_EDITOR, "editor PID %s has %s open; a batch-mode editor cannot open it as well "
                                               "(use --mode editor, or close it with python Tools~/setup-sandbox.py --close)"
                               % (", ".join(map(str, editors)), project))
            result, _batch = run_in_batch(setup, verb, args_path, out_path, project, options.timeout or 1800,
                                          options.memory_limit_gb)
    except CliError as error:
        if workdir:
            log("kept the work folder for inspection: " + workdir)
        result = envelope(verb, [str(error)])
        emit(result, options.out)
        return error.code
    if workdir:
        shutil.rmtree(workdir, ignore_errors=True)

    emit(result, options.out)
    return EXIT_OK if result.get("ok") is True else EXIT_FAILED


if __name__ == "__main__":
    sys.exit(main())
