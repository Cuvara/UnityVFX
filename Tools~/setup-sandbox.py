#!/usr/bin/env python3
"""Prepare, open, watch and close the agent-owned Sandbox project (Sandbox~/).

Every start and stop of the Sandbox editor by an agent goes through this script, so the editor is
watched against a memory limit the whole time. Layout and reasons: Tools~/sandbox_paths.py and
Documentation~/AUTHORING.md ("Sandbox", "Pitfalls").

Modes (all print one JSON object on stdout; progress goes to stderr):
  python Tools~/setup-sandbox.py                    check the editor version and the repository for
                                                    link cycles, create the links, pin Unity-MCP to a
                                                    local server; idempotent
  python Tools~/setup-sandbox.py --print-open-path  print only the open path
  python Tools~/setup-sandbox.py --open             set up, start the editor through the open path and
                                                    watch it until it is ready (and --hold seconds more)
  python Tools~/setup-sandbox.py --restore-nuget    bring back the Unity-MCP NuGet DLLs when
                                                    Sandbox~/Assets/Plugins/NuGet is absent or incomplete
                                                    against .nuget-installed.json: start the editor past the
                                                    compile-errors dialog, wait for the DLLs and a ready
                                                    editor, close it (--keep-open: leave it running);
                                                    --open does the same start when it sees the folder missing
  python Tools~/setup-sandbox.py --watch            print PID, private bytes and working set of this
                                                    clone's Sandbox editor (--duration seconds)
  python Tools~/setup-sandbox.py --close            save, close the window of that editor, wait, and
                                                    kill exactly that PID on timeout
  python Tools~/setup-sandbox.py --remove           remove the links (links only)

Memory guard: in --open, --watch and --close the editor is killed (that PID and its own child
processes only) when its private bytes exceed --memory-limit-gb (default 12, or the environment
variable CUVARA_VFX_SANDBOX_MEMORY_LIMIT_GB).

Safety: a link is only ever removed as a link (os.rmdir on a junction, os.unlink on a symlink),
never through a recursive delete. A real file or directory at a link location is never touched.
No process other than this clone's Sandbox editor and its own children is ever stopped.

Exit codes: 0 ok, 1 a problem is reported in "problems" (or the editor was killed), 2 usage.
"""

import argparse
import json
import os
import re
import secrets
import shutil
import subprocess
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import sandbox_paths as sp  # noqa: E402

MCP_CONFIG = os.path.join("UserSettings", "AI-Game-Developer-Config.json")
MCP_PORT_BASE = 27000
MCP_PORT_RANGE = 2000
ENV_MEMORY_LIMIT = "CUVARA_VFX_SANDBOX_MEMORY_LIMIT_GB"
GB = 1024 ** 3
SAVE_SNIPPET = "AssetDatabase.SaveAssets(); \"saved\""
NUGET_DIR = os.path.join("Assets", "Plugins", "NuGet")
NUGET_MANIFEST = ".nuget-installed.json"
RESTORE_DISMISS_MS = 400000  # how long unity-mcp-cli keeps clicking Ignore on the compile-errors dialog


def log(message):
    print(message, file=sys.stderr, flush=True)


# ---------------------------------------------------------------- editor version


def editor_version(sandbox):
    path = os.path.join(sandbox, "ProjectSettings", "ProjectVersion.txt")
    with open(path, encoding="utf-8") as handle:
        match = re.search(r"^m_EditorVersion:\s*(\S+)", handle.read(), re.MULTILINE)
    return match.group(1) if match else None


def editor_candidates(version):
    override = os.environ.get("UNITY_EDITOR_PATH")
    if override:
        return [override]
    if sys.platform == "win32":
        bases = [os.environ.get("ProgramFiles", r"C:\Program Files"), r"C:\Program Files"]
        return [os.path.join(b, "Unity", "Hub", "Editor", version, "Editor", "Unity.exe") for b in bases]
    if sys.platform == "darwin":
        return [os.path.join("/Applications/Unity/Hub/Editor", version, "Unity.app", "Contents", "MacOS", "Unity")]
    return [os.path.join(os.path.expanduser("~"), "Unity", "Hub", "Editor", version, "Editor", "Unity")]


def check_editor(sandbox, problems):
    version = editor_version(sandbox)
    if not version:
        problems.append("no m_EditorVersion in Sandbox~/ProjectSettings/ProjectVersion.txt")
        return {"version": None, "path": None, "found": False}
    for candidate in editor_candidates(version):
        if os.path.isfile(candidate):
            return {"version": version, "path": candidate, "found": True}
    problems.append("Unity " + version + " is not installed (set UNITY_EDITOR_PATH to override the lookup)")
    return {"version": version, "path": None, "found": False}


# ---------------------------------------------------------------- links


def remove_link(path):
    """Removes a link or junction itself; refuses anything else."""
    if not sp.is_link(path):
        raise RuntimeError("refusing to remove " + path + ": not a link")
    if sys.platform == "win32" and not os.path.islink(path):
        os.rmdir(path)  # a junction: removes the link, never the target's content
    else:
        os.unlink(path)


def create_link(path, target):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    if sys.platform == "win32":
        # A directory junction needs no administrator rights or developer mode.
        result = subprocess.run(["cmd", "/c", "mklink", "/J", path, target], capture_output=True, text=True)
        if result.returncode != 0:
            raise RuntimeError("mklink /J failed: " + (result.stderr or result.stdout).strip())
    else:
        os.symlink(target, path, target_is_directory=True)


def ensure_link(name, path, target, changes, problems):
    entry = {"path": path, "target": target, "state": None}
    if sp.is_link(path):
        current = sp.link_target(path)
        if current is not None and sp.same_path(current, target):
            entry["state"] = "linked"
            return entry
        remove_link(path)
        changes.append("relinked " + name + " (was " + str(current) + ")")
    elif os.path.lexists(path):
        entry["state"] = "blocked"
        problems.append(name + ": " + path + " exists and is not a link; it is left untouched")
        return entry
    create_link(path, target)
    if not (sp.is_link(path) and sp.same_path(sp.link_target(path), target)):
        entry["state"] = "failed"
        problems.append(name + ": link creation could not be verified at " + path)
        return entry
    changes.append("linked " + name)
    entry["state"] = "linked"
    return entry


def drop_link(name, path, changes, problems):
    entry = {"path": path, "state": None}
    if sp.is_link(path):
        remove_link(path)
        changes.append("removed link " + name)
        entry["state"] = "absent"
    elif os.path.lexists(path):
        entry["state"] = "blocked"
        problems.append(name + ": " + path + " is not a link; it is left untouched")
    else:
        entry["state"] = "absent"
    return entry


def migrate_old_layout(root, changes, problems):
    """Removes, as links only, what the first layout created: a junction at the home folder itself and
    the cycle-forming Sandbox~/Packages/com.cuvara.vfx -> repository root."""
    home = sp.home(root)
    if sp.is_link(home):
        remove_link(home)
        changes.append("removed old open-path link " + home)
    legacy = sp.legacy_package_link(root)
    if sp.is_link(legacy):
        remove_link(legacy)
        changes.append("removed old embedded-package link " + legacy)
    elif os.path.lexists(legacy):
        problems.append(legacy + " exists and is not a link: the manifest references file:../../package instead")


def check_repo_links(root, problems, warnings):
    """A link inside the repository that points at the root or an ancestor of itself is a hard error."""
    allowed = {os.path.normcase(sp.showcase_link(root))}
    found = []
    for path, target, cycle in sp.links_in_repo(root):
        found.append({"path": path, "target": target, "cycle": cycle})
        if cycle:
            problems.append("directory cycle: link " + path + " -> " + str(target) +
                            " (remove that link itself: os.rmdir on a junction, os.unlink on a symlink; never a recursive delete)")
        elif os.path.normcase(path) not in allowed:
            warnings.append("unexpected link inside the repository: " + path + " -> " + str(target))
    return found


# ---------------------------------------------------------------- Unity-MCP config


def read_mcp_config(sandbox):
    try:
        with open(os.path.join(sandbox, MCP_CONFIG), encoding="utf-8") as handle:
            return json.load(handle)
    except (OSError, ValueError):
        return None


def mcp_summary(sandbox):
    config = read_mcp_config(sandbox) or {}
    return {"config": MCP_CONFIG, "connection_mode": config.get("connectionMode"), "host": config.get("host")}


def ensure_mcp(sandbox, root, changes, warnings):
    """Pins the plugin to a local server once; an existing local (Custom) config is kept."""
    summary = mcp_summary(sandbox)
    if summary["connection_mode"] == "Custom":
        return summary
    cli = shutil.which("unity-mcp-cli")
    if cli is None:
        warnings.append("unity-mcp-cli not found: the Unity-MCP plugin config is not pinned to a local server")
        return summary
    port = MCP_PORT_BASE + int(sp.clone_id(root), 16) % MCP_PORT_RANGE
    result = subprocess.run(
        [cli, "bootstrap-local", "--url", "http://localhost:%d" % port, "--token", secrets.token_urlsafe(32), sandbox],
        capture_output=True, text=True,
    )
    if result.returncode != 0:
        warnings.append("unity-mcp-cli bootstrap-local failed: " + (result.stderr or result.stdout).strip()[-300:])
        return summary
    changes.append("pinned the Unity-MCP plugin to a local server on port %d" % port)
    return mcp_summary(sandbox)


# ---------------------------------------------------------------- processes


if sys.platform == "win32":
    import ctypes
    from ctypes import wintypes

    _kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
    _psapi = ctypes.WinDLL("psapi", use_last_error=True)
    _PROCESS_QUERY_LIMITED_INFORMATION = 0x1000
    _SYNCHRONIZE = 0x00100000
    _STILL_ACTIVE = 259

    class _MemoryCounters(ctypes.Structure):
        _fields_ = [
            ("cb", wintypes.DWORD), ("PageFaultCount", wintypes.DWORD),
            ("PeakWorkingSetSize", ctypes.c_size_t), ("WorkingSetSize", ctypes.c_size_t),
            ("QuotaPeakPagedPoolUsage", ctypes.c_size_t), ("QuotaPagedPoolUsage", ctypes.c_size_t),
            ("QuotaPeakNonPagedPoolUsage", ctypes.c_size_t), ("QuotaNonPagedPoolUsage", ctypes.c_size_t),
            ("PagefileUsage", ctypes.c_size_t), ("PeakPagefileUsage", ctypes.c_size_t),
            ("PrivateUsage", ctypes.c_size_t),
        ]

    _kernel32.OpenProcess.restype = wintypes.HANDLE
    _kernel32.OpenProcess.argtypes = [wintypes.DWORD, wintypes.BOOL, wintypes.DWORD]
    _kernel32.GetExitCodeProcess.argtypes = [wintypes.HANDLE, ctypes.POINTER(wintypes.DWORD)]
    _kernel32.CloseHandle.argtypes = [wintypes.HANDLE]
    _psapi.GetProcessMemoryInfo.argtypes = [wintypes.HANDLE, ctypes.POINTER(_MemoryCounters), wintypes.DWORD]


class Process:
    """A handle on one PID, opened once, so its exit code is readable after it ends (Windows)."""

    def __init__(self, pid):
        self.pid = pid
        self.handle = None
        if sys.platform == "win32":
            self.handle = _kernel32.OpenProcess(_PROCESS_QUERY_LIMITED_INFORMATION | _SYNCHRONIZE, False, pid) or None

    def alive(self):
        if sys.platform == "win32":
            if not self.handle:
                return False
            code = wintypes.DWORD()
            return bool(_kernel32.GetExitCodeProcess(self.handle, ctypes.byref(code))) and code.value == _STILL_ACTIVE
        try:
            os.kill(self.pid, 0)
            return True
        except OSError:
            return False

    def exit_code(self):
        if sys.platform == "win32" and self.handle:
            code = wintypes.DWORD()
            if _kernel32.GetExitCodeProcess(self.handle, ctypes.byref(code)) and code.value != _STILL_ACTIVE:
                return code.value
        return None

    def memory(self):
        """(private bytes, working set) or (None, None)."""
        if sys.platform == "win32":
            if not self.handle:
                return None, None
            counters = _MemoryCounters()
            counters.cb = ctypes.sizeof(_MemoryCounters)
            if not _psapi.GetProcessMemoryInfo(self.handle, ctypes.byref(counters), counters.cb):
                return None, None
            return counters.PrivateUsage, counters.WorkingSetSize
        try:  # Linux
            with open("/proc/%d/status" % self.pid, encoding="utf-8") as handle:
                fields = dict(line.split(":", 1) for line in handle if ":" in line)
            kb = lambda key: int(fields[key].split()[0]) * 1024 if key in fields else None  # noqa: E731
            return kb("RssAnon") or kb("VmRSS"), kb("VmRSS")
        except OSError:
            result = subprocess.run(["ps", "-p", str(self.pid), "-o", "rss="], capture_output=True, text=True)
            rss = int(result.stdout.strip()) * 1024 if result.stdout.strip() else None
            return rss, rss

    def close(self):
        if sys.platform == "win32" and self.handle:
            _kernel32.CloseHandle(self.handle)
            self.handle = None


def list_processes():
    """[(pid, parent pid, name, command line)] of every process (Windows: CIM; POSIX: ps)."""
    if sys.platform == "win32":
        script = ("Get-CimInstance Win32_Process | ForEach-Object { '' + $_.ProcessId + '|' + $_.ParentProcessId"
                  " + '|' + $_.Name + '|' + $_.CommandLine }")
        out = subprocess.run(["powershell", "-NoProfile", "-Command", script], capture_output=True, text=True).stdout
        rows = []
        for line in out.splitlines():
            parts = line.split("|", 3)
            if len(parts) == 4 and parts[0].isdigit():
                rows.append((int(parts[0]), int(parts[1] or 0), parts[2], parts[3]))
        return rows
    out = subprocess.run(["ps", "-eo", "pid=,ppid=,comm=,args="], capture_output=True, text=True).stdout
    rows = []
    for line in out.splitlines():
        parts = line.split(None, 3)
        if len(parts) >= 3 and parts[0].isdigit():
            rows.append((int(parts[0]), int(parts[1]), os.path.basename(parts[2]), parts[3] if len(parts) > 3 else ""))
    return rows


def _mentions(command_line, path):
    line = command_line.replace("/", "\\").lower() if sys.platform == "win32" else command_line
    needle = path.replace("/", "\\").lower() if sys.platform == "win32" else path
    return needle.rstrip("\\/") in line


def sandbox_editors(root, rows=None):
    """PIDs of editors (not import workers) whose command line carries this clone's sandbox home."""
    rows = rows if rows is not None else list_processes()
    found = []
    for pid, _ppid, name, command_line in rows:
        if not name.lower().startswith("unity") or "AssetImportWorker" in command_line:
            continue
        if "-projectpath" not in command_line.lower():
            continue
        if _mentions(command_line, sp.home(root)):
            found.append(pid)
    return sorted(set(found))


def kill_editor(pid, root, reason, report):
    """Kills exactly that PID, then its own child processes that belong to this Sandbox."""
    rows = list_processes()
    children = [p for p, ppid, _name, cmd in rows if ppid == pid and _mentions(cmd, sp.home(root))]
    for target in [pid] + children:
        if sys.platform == "win32":
            subprocess.run(["taskkill", "/PID", str(target), "/F"], capture_output=True, text=True)
        else:
            try:
                os.kill(target, 9)
            except OSError:
                pass
    report.setdefault("killed", []).append({"pid": pid, "children": children, "reason": reason})
    log("killed PID %d (%s); children %s" % (pid, reason, children))


def stop_launcher(launcher):
    """Ends our own `unity-mcp-cli open` launcher (the cmd shim and its node child) by exact PID, no tree kill.

    The editor was started by the launcher; the launcher's job after the launch is only to watch for the
    compile-errors dialog. Never `taskkill /T`: that could reach the editor.
    """
    pids = [p for p, ppid, name, _cmd in list_processes() if ppid == launcher.pid and name.lower().startswith("node")]
    pids.append(launcher.pid)
    for pid in pids:
        if sys.platform == "win32":
            subprocess.run(["taskkill", "/PID", str(pid), "/F"], capture_output=True, text=True)
        else:
            try:
                os.kill(pid, 15)
            except OSError:
                pass
    try:
        launcher.communicate(timeout=10)
    except subprocess.TimeoutExpired:
        pass
    return pids


def sample(process, started):
    private, working = process.memory()
    return {
        "t": round(time.monotonic() - started, 1),
        "private_gb": round(private / GB, 2) if private is not None else None,
        "working_set_gb": round(working / GB, 2) if working is not None else None,
    }


def over_limit(entry, limit_gb):
    return entry["private_gb"] is not None and entry["private_gb"] > limit_gb


def nuget_status(sandbox):
    """State of the committed Unity-MCP NuGet DLLs: complete, incomplete or absent.

    `.nuget-installed.json` (written by the plugin's restore) lists every package and its DLLs. Without
    that file there is nothing to compare against, so the state is `absent` (also when only some DLLs
    are there: the plugin restores the full set).
    """
    folder = os.path.join(sandbox, NUGET_DIR)
    manifest = os.path.join(folder, NUGET_MANIFEST)
    status = {"folder": folder, "state": "absent", "expected": None, "present": 0, "missing": []}
    try:
        with open(manifest, encoding="utf-8") as handle:
            packages = json.load(handle).get("packages", {})
        expected = sorted({dll for entry in packages.values() for dll in entry.get("dlls", [])})
    except (OSError, ValueError, AttributeError):
        return status
    status["expected"] = len(expected)
    status["missing"] = [dll for dll in expected if not os.path.isfile(os.path.join(folder, dll))]
    status["present"] = len(expected) - len(status["missing"])
    status["state"] = "complete" if expected and not status["missing"] else "incomplete"
    return status


def mcp_ready(open_path):
    cli = shutil.which("unity-mcp-cli")
    if cli is None:
        return False
    result = subprocess.run([cli, "wait-for-ready", "--timeout", "2000", "--interval", "1000", open_path],
                            capture_output=True, text=True)
    return result.returncode == 0 and "SUCCESS" in result.stdout + result.stderr


# ---------------------------------------------------------------- modes


def setup(root, report, changes, problems, warnings):
    sandbox = sp.sandbox_dir(root)
    home = sp.home(root)
    report["editor"] = check_editor(sandbox, problems)
    migrate_old_layout(root, changes, problems)
    report["repo_links"] = check_repo_links(root, problems, warnings)
    home_issues = sp.home_problems(home, root)
    problems.extend("sandbox home " + home + ": " + issue for issue in home_issues)
    links = {}
    if not home_issues:
        if os.path.lexists(home) and not os.path.isdir(home):
            problems.append("sandbox home " + home + " exists and is not a folder; it is left untouched")
        else:
            os.makedirs(home, exist_ok=True)
            links["open_path"] = ensure_link("open_path", sp.open_path(root), sandbox, changes, problems)
            links["package"] = ensure_link("package", sp.package_link(root), root, changes, problems)
    if os.path.isdir(sp.showcase_source(root)):
        links["showcase"] = ensure_link("showcase", sp.showcase_link(root), sp.showcase_source(root), changes, problems)
    else:
        links["showcase"] = {"path": sp.showcase_link(root), "target": sp.showcase_source(root),
                             "state": "skipped: Samples~/Showcase does not exist"}
    report["links"] = links
    report["mcp"] = ensure_mcp(sandbox, root, changes, warnings)
    report["nuget"] = nuget_status(sandbox)
    if report["nuget"]["state"] != "complete":
        warnings.append("NuGet DLLs %s in %s: run --restore-nuget (--open does the same) or follow the recovery in "
                        "Documentation~/AUTHORING.md, Pitfalls" % (report["nuget"]["state"], NUGET_DIR))


def remove(root, report, changes, problems):
    report["links"] = {
        "open_path": drop_link("open_path", sp.open_path(root), changes, problems),
        "package": drop_link("package", sp.package_link(root), changes, problems),
        "showcase": drop_link("showcase", sp.showcase_link(root), changes, problems),
        "legacy_package": drop_link("legacy_package", sp.legacy_package_link(root), changes, problems),
    }
    home = sp.home(root)
    if sp.is_link(home):
        remove_link(home)
        changes.append("removed old open-path link " + home)
    elif os.path.isdir(home) and not os.listdir(home):
        os.rmdir(home)
        changes.append("removed empty sandbox home " + home)


def watch_process(process, root, args, report, started, stop_when=None, label="watch"):
    """Samples every --interval seconds; kills the editor above the limit. Returns the end reason."""
    samples = report.setdefault("samples", [])
    deadline = started + args.timeout
    while True:
        if not process.alive():
            return "exited"
        entry = sample(process, started)
        samples.append(entry)
        peak = report.setdefault("peak", {"private_gb": 0.0, "working_set_gb": 0.0})
        for key in peak:
            if entry[key] is not None and entry[key] > peak[key]:
                peak[key] = entry[key]
        log("%s t=%5.1fs pid=%d private=%s GB working_set=%s GB" % (
            label, entry["t"], process.pid, entry["private_gb"], entry["working_set_gb"]))
        if over_limit(entry, args.memory_limit_gb):
            kill_editor(process.pid, root, "private bytes %.2f GB above the %.1f GB limit" % (
                entry["private_gb"], args.memory_limit_gb), report)
            return "killed_above_memory_limit"
        if stop_when is not None and stop_when(entry):
            return "condition"
        if time.monotonic() > deadline:
            return "timeout"
        time.sleep(args.interval)


def open_editor(root, args, report, changes, problems, warnings, restore=False):
    """Starts the editor and watches it until ready. `restore`: also bring back the NuGet DLLs.

    The Unity-MCP plugin restores its NuGet DLLs only once the editor runs past the compile-errors dialog
    that the missing DLLs cause (Documentation~/AUTHORING.md, Pitfalls). So a restore start keeps
    `unity-mcp-cli open` clicking Ignore for RESTORE_DISMISS_MS, and "ready" additionally needs the DLL set
    to be complete against `.nuget-installed.json`.
    """
    setup(root, report, changes, problems, warnings)
    if problems:
        return
    running = sandbox_editors(root)
    if running:
        problems.append("a Sandbox editor of this clone already runs: PID %s" % running)
        return
    restore = restore or report["nuget"]["state"] != "complete"  # --open restores a missing set by itself
    report["restore_nuget"] = restore
    cli = shutil.which("unity-mcp-cli")
    if cli is None:
        problems.append("unity-mcp-cli not found")
        return
    open_path = sp.open_path(root)
    sandbox = sp.sandbox_dir(root)
    launch = [cli, "open", "--unity", report["editor"]["version"]]
    if restore:
        launch += ["--launch-dismiss-timeout-ms", str(RESTORE_DISMISS_MS)]
        log("NuGet DLLs %s: starting with the launch-errors dialog dismissed for up to %d s" % (
            report["nuget"]["state"], RESTORE_DISMISS_MS // 1000))
    # `unity-mcp-cli open` keeps running after the launch (it watches for a launch-errors dialog for
    # about 30 s): read its output only up to the PID, so the memory watch starts at once.
    launcher = subprocess.Popen(launch + [open_path], stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True)
    output, pid = [], None
    for line in launcher.stdout:
        output.append(line)
        match = re.search(r"Launched Unity Editor \(PID:\s*(\d+)\)", line)
        if match:
            pid = int(match.group(1))
            break
    if pid is None:
        launcher.wait()
        problems.append("unity-mcp-cli open failed: " + "".join(output).strip()[-400:])
        return
    report["launcher_pid"] = launcher.pid
    report["editor_pid"] = pid
    changes.append("started editor PID %d through %s" % (pid, open_path))
    process = Process(pid)
    started = time.monotonic()
    ready = {"at": None}

    def until_ready_and_held(entry):
        # After a restore start the plugin writes the DLLs before the editor compiles with them: wait for both.
        restored = not restore or nuget_status(sandbox)["state"] == "complete"
        if ready["at"] is None and restored and mcp_ready(open_path):
            ready["at"] = entry["t"]
            log("ready t=%.1fs; holding %d s" % (entry["t"], args.hold))
        return ready["at"] is not None and entry["t"] >= ready["at"] + args.hold

    reason = watch_process(process, root, args, report, started, until_ready_and_held, "open")
    report["ready_after_s"] = ready["at"]
    if reason == "condition":
        report["state"] = "ready"
    elif reason == "timeout":
        report["state"] = "not ready after %d s (still running, watch it with --watch)" % args.timeout
        problems.append(report["state"])
    elif reason == "exited":
        report["state"] = "exited during startup (exit code %s)" % process.exit_code()
        problems.append(report["state"])
    else:
        report["state"] = reason
        problems.append("editor PID %d killed above the memory limit" % pid)
    process.close()
    try:  # reap the launcher (our own child), which ends on its own after its dialog watch
        rest, _ = launcher.communicate(timeout=5 if restore and reason == "condition" else 60)
        output.append(rest or "")
    except subprocess.TimeoutExpired:
        if restore and reason == "condition":
            # The editor is past the dialog: the 400 s dismiss watch of our own launcher is no longer needed.
            stopped = stop_launcher(launcher)
            changes.append("stopped the launcher dismiss watch (PIDs %s)" % stopped)
        else:
            warnings.append("unity-mcp-cli open (PID %d) still running after 60 s" % launcher.pid)
    if "dismissed Unity launch-errors dialog" in "".join(output):
        warnings.append("unity-mcp-cli dismissed a launch-errors dialog: read the console for compile errors")
    if restore:
        report["launcher_output"] = "".join(output).strip()[-600:]
        report["nuget_after"] = nuget_status(sandbox)
        if report["nuget_after"]["state"] == "complete":
            changes.append("restored the NuGet DLLs (%d)" % report["nuget_after"]["present"])
        elif not problems:
            problems.append("the editor is ready but the NuGet DLLs are %s" % report["nuget_after"]["state"])


def restore_nuget(root, args, report, changes, problems, warnings):
    """Brings the NuGet DLLs back through the plugin's own restore (see open_editor), then closes the editor."""
    status = nuget_status(sp.sandbox_dir(root))
    if status["state"] == "complete":
        report["nuget"] = status
        report["restore_nuget"] = False
        report["state"] = "nothing to restore: %d of %d DLLs present" % (status["present"], status["expected"])
        return
    open_editor(root, args, report, changes, problems, warnings, restore=True)
    if report.get("editor_pid") is None or args.keep_open:
        return
    if report.get("state") != "ready":
        return  # a problem is reported; leave the editor for the caller to inspect with --watch and --close
    close = {}
    close_editor(root, args, close, changes, problems, warnings)
    report["closed"] = {"ended": close.get("ended"), "seconds": close.get("seconds"),
                        "peak": close.get("peak"), "killed": close.get("killed")}


def watch(root, args, report, problems):
    pids = sandbox_editors(root)
    report["editors"] = pids
    if not pids:
        report["state"] = "not running"
        return
    for pid in pids:
        process = Process(pid)
        started = time.monotonic()
        saved_timeout = args.timeout
        args.timeout = args.duration
        reason = watch_process(process, root, args, report, started, None, "watch")
        args.timeout = saved_timeout
        if reason == "killed_above_memory_limit":
            problems.append("editor PID %d killed above the memory limit" % pid)
        report["state"] = reason if reason != "timeout" else "running"
        process.close()


def close_editor(root, args, report, changes, problems, warnings):
    pids = sandbox_editors(root)
    report["editors"] = pids
    if not pids:
        report["ended"] = "not running"
        return
    open_path = sp.open_path(root)
    cli = shutil.which("unity-mcp-cli")
    for pid in pids:
        process = Process(pid)
        if cli is not None:
            saved = subprocess.run([cli, "run-tool", "script-execute", open_path, "--input",
                                    json.dumps({"csharpCode": SAVE_SNIPPET}), "--timeout", "30000"],
                                   capture_output=True, text=True)
            if "saved" not in saved.stdout:
                warnings.append("SaveAssets through Unity-MCP did not answer; closing anyway")
        # A normal window close (WM_CLOSE / SIGTERM) of exactly that PID.
        if sys.platform == "win32":
            subprocess.run(["taskkill", "/PID", str(pid)], capture_output=True, text=True)
        else:
            os.kill(pid, 15)
        started = time.monotonic()
        reason = watch_process(process, root, args, report, started, None, "close")
        seconds = round(time.monotonic() - started, 1)
        code = process.exit_code()
        if reason == "exited":
            report["ended"] = "exited by itself %.1f s after the close request (exit code %s)" % (seconds, code)
            if code not in (0, None):
                problems.append("editor PID %d ended with exit code %s: it may have been killed by another process" % (pid, code))
            else:
                changes.append("editor PID %d closed" % pid)
        elif reason == "timeout":
            kill_editor(pid, root, "no exit within %d s of the close request" % args.timeout, report)
            report["ended"] = "killed by this script after the %d s timeout" % args.timeout
            problems.append(report["ended"])
        else:
            report["ended"] = "killed by this script above the memory limit after %.1f s" % seconds
            problems.append(report["ended"])
        report["seconds"] = seconds
        process.close()


def main():
    parser = argparse.ArgumentParser(description="Prepare, open, watch and close the Sandbox~ Unity project.")
    mode = parser.add_mutually_exclusive_group()
    mode.add_argument("--print-open-path", action="store_true", help="print only the open path")
    mode.add_argument("--open", action="store_true", help="set up, start the editor and watch it until ready")
    mode.add_argument("--restore-nuget", action="store_true",
                      help="bring back the Unity-MCP NuGet DLLs if Sandbox~/Assets/Plugins/NuGet is absent or "
                           "incomplete: start the editor past the compile-errors dialog, wait for the DLLs and "
                           "a ready editor, then close it")
    mode.add_argument("--watch", action="store_true", help="print the memory of this clone's Sandbox editor")
    mode.add_argument("--close", action="store_true", help="save and close this clone's Sandbox editor")
    mode.add_argument("--remove", action="store_true", help="remove the links (links only)")
    parser.add_argument("--memory-limit-gb", type=float,
                        default=float(os.environ.get(ENV_MEMORY_LIMIT, "12")),
                        help="kill the editor above this many GB of private bytes (default 12)")
    parser.add_argument("--interval", type=float, default=3.0, help="seconds between memory samples (default 3)")
    parser.add_argument("--timeout", type=int, default=None,
                        help="seconds --open waits for ready (default 900) and --close waits for the exit (default 300)")
    parser.add_argument("--keep-open", action="store_true", help="with --restore-nuget: leave the editor running")
    parser.add_argument("--hold", type=int, default=0, help="with --open: keep watching this many seconds after ready")
    parser.add_argument("--duration", type=int, default=0, help="with --watch: seconds to keep sampling (0: one sample)")
    args = parser.parse_args()
    if args.timeout is None:
        args.timeout = 900 if (args.open or args.restore_nuget) else 300

    root = sp.repo_root()
    if args.print_open_path:
        print(sp.open_path(root))
        return 0

    changes, problems, warnings = [], [], []
    report = {"ok": False, "root": root, "sandbox": sp.sandbox_dir(root), "home": sp.home(root),
              "open_path": sp.open_path(root), "package_link": sp.package_link(root),
              "home_env": sp.ENV_HOME, "memory_limit_gb": args.memory_limit_gb}
    try:
        if not os.path.isdir(sp.sandbox_dir(root)):
            problems.append("Sandbox~ not found at " + sp.sandbox_dir(root))
        elif args.remove:
            remove(root, report, changes, problems)
        elif args.restore_nuget:
            restore_nuget(root, args, report, changes, problems, warnings)
        elif args.open:
            open_editor(root, args, report, changes, problems, warnings)
        elif args.watch:
            watch(root, args, report, problems)
        elif args.close:
            close_editor(root, args, report, changes, problems, warnings)
        else:
            setup(root, report, changes, problems, warnings)
    except (OSError, RuntimeError) as error:
        problems.append(str(error))

    report.update(changes=changes, problems=problems, warnings=warnings)
    report["ok"] = not problems
    print(json.dumps(report, indent=2))
    return 0 if report["ok"] else 1


if __name__ == "__main__":
    sys.exit(main())
