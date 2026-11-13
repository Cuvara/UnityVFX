"""Path rules of the agent-owned Sandbox project (Sandbox~/).

Unity 6000.6 cannot map package scripts to their classes when the project path contains a hidden
component (a folder that ends in '~' or starts with '.'): every package ScriptableObject is then
saved with `m_Script: {fileID: 0}`. The committed project therefore stays in `Sandbox~/`, but Unity
always opens it through a link in a per-clone folder outside the repository:

  <sandbox home>/Sandbox   -> <repository>/Sandbox~   (the open path: Unity opens the project here)
  <sandbox home>/package   -> <repository>            (Packages/manifest.json: "file:../../package")

No link inside the repository points at the repository or one of its ancestors: such a link is a
directory cycle, and a cycle walked by the editor's initial Search indexing made the editor grow
by gigabytes per second. See Documentation~/AUTHORING.md, sections "Sandbox" and "Pitfalls".

`Tools~/setup-sandbox.py` creates the links and starts, watches and closes the editor;
`Tools~/vfxcli.py` maps `--project Sandbox~` to the open path with `resolve_project`.
Standard library only.
"""

import hashlib
import os
import sys

ENV_HOME = "CUVARA_VFX_SANDBOX_HOME"
"""Environment variable that overrides the per-clone sandbox home folder (CI, or a custom location)."""

SANDBOX_DIR_NAME = "Sandbox~"
PACKAGE_NAME = "com.cuvara.vfx"
OPEN_LINK_NAME = "Sandbox"
PACKAGE_LINK_NAME = "package"
MANIFEST_PACKAGE_REFERENCE = "file:../../" + PACKAGE_LINK_NAME

# Folders the link scan does not enter: git data and what the editor generates (large, never linked).
SCAN_SKIP = {".git", "Library", "Temp", "Logs", "obj", "UserSettings"}


def repo_root():
    """The repository root, which is also the package root (this file lives in Tools~/)."""
    return os.path.dirname(os.path.dirname(os.path.abspath(__file__)))


def sandbox_dir(root=None):
    """The committed Sandbox project folder (Sandbox~/)."""
    return os.path.join(root or repo_root(), SANDBOX_DIR_NAME)


def showcase_link(root=None):
    """The link to the Showcase sample inside the Sandbox project (target: Samples~/Showcase)."""
    return os.path.join(sandbox_dir(root), "Assets", "Showcase")


def showcase_source(root=None):
    return os.path.join(root or repo_root(), "Samples~", "Showcase")


def legacy_package_link(root=None):
    """Link of the first layout (Sandbox~/Packages/com.cuvara.vfx -> repository root): a cycle; removed on sight."""
    return os.path.join(sandbox_dir(root), "Packages", PACKAGE_NAME)


def clone_id(root=None):
    """8 hex digits of a hash of the repository root path, so two clones never share a sandbox home."""
    real = os.path.normcase(os.path.realpath(root or repo_root()))
    return hashlib.sha256(real.encode("utf-8")).hexdigest()[:8]


def default_home(root=None):
    """The per-clone default sandbox home. It follows the rule checked by `home_problems`."""
    name = "vfx-sandbox-" + clone_id(root)
    if sys.platform == "win32":
        base = os.environ.get("LOCALAPPDATA") or os.path.join(os.path.expanduser("~"), "AppData", "Local")
        return os.path.join(base, "Cuvara", name)
    if sys.platform == "darwin":
        return os.path.join(os.path.expanduser("~"), "Library", "Caches", "Cuvara", name)
    # Linux: ~/.cache and ~/.local are dot folders, which Unity treats as hidden.
    return os.path.join(os.path.expanduser("~"), "Cuvara", name)


def home(root=None):
    """The sandbox home folder: $CUVARA_VFX_SANDBOX_HOME or the per-clone default."""
    override = os.environ.get(ENV_HOME)
    return os.path.abspath(override) if override else default_home(root)


def open_path(root=None):
    """The path Unity opens the Sandbox through (a link to Sandbox~)."""
    return os.path.join(home(root), OPEN_LINK_NAME)


def package_link(root=None):
    """The link the Sandbox manifest references as file:../../package (a link to the repository root)."""
    return os.path.join(home(root), PACKAGE_LINK_NAME)


def _components(path):
    _drive, rest = os.path.splitdrive(os.path.abspath(path))
    return [part for part in rest.replace("\\", "/").split("/") if part]


def is_inside(path, ancestor):
    """True when `path` is `ancestor` or below it (string comparison; never follows links)."""
    path_norm = os.path.normcase(os.path.abspath(path))
    ancestor_norm = os.path.normcase(os.path.abspath(ancestor))
    return path_norm == ancestor_norm or path_norm.startswith(ancestor_norm.rstrip(os.sep) + os.sep)


def home_problems(path, root=None):
    """Reasons the folder cannot be the sandbox home; an empty list means it is valid."""
    problems = []
    hidden = [part for part in _components(path) if part.startswith(".") or part.endswith("~")]
    if hidden:
        problems.append("hidden path component(s) " + ", ".join(hidden) + ": Unity then cannot map package scripts to classes")
    if is_inside(path, root or repo_root()):
        problems.append("inside the repository root, which is the package: Unity would import the project into itself")
    return problems


def is_link(path):
    """True for a symbolic link or a Windows directory junction (never follows it)."""
    if os.path.islink(path):
        return True
    isjunction = getattr(os.path, "isjunction", None)  # Python 3.12+
    if isjunction is not None:
        return isjunction(path)
    if sys.platform == "win32" and os.path.lexists(path):
        try:
            os.readlink(path)  # reads junctions too on Python 3.8+
            return True
        except (OSError, ValueError):
            return False
    return False


def link_target(path):
    """The absolute target of a link or junction, or None when the path is not a link."""
    if not is_link(path):
        return None
    target = os.readlink(path)
    if target.startswith("\\\\?\\"):
        target = target[4:]
    if not os.path.isabs(target):
        target = os.path.join(os.path.dirname(path), target)
    return os.path.abspath(target)


def same_path(a, b):
    return os.path.normcase(os.path.abspath(a)) == os.path.normcase(os.path.abspath(b))


def links_in_repo(root=None):
    """Every link under the repository root as (path, target, is_cycle); never follows a link.

    `is_cycle` is true when the link points at the repository root, at one of its own ancestors,
    or at a folder that contains the link: a walker that follows it never ends.
    """
    root = root or repo_root()
    found = []
    for directory, subdirs, files in os.walk(root, followlinks=False):
        keep = []
        for name in subdirs:
            path = os.path.join(directory, name)
            if is_link(path):
                target = link_target(path)
                cycle = target is not None and (is_inside(root, target) or is_inside(path, target))
                found.append((path, target, cycle))
            elif name not in SCAN_SKIP:
                keep.append(name)
        subdirs[:] = keep
        for name in files:
            path = os.path.join(directory, name)
            if os.path.islink(path):
                found.append((path, link_target(path), False))
    return found


def resolve_project(project, root=None):
    """Maps the Sandbox project (`Sandbox~`, its full path or the open path) to the open path.

    Any other project path is returned unchanged, so callers can pass it to unity-mcp-cli as is.
    Callers check that the open path exists and name Tools~/setup-sandbox.py when it does not.
    """
    if project is None:
        return open_path(root)
    absolute = os.path.abspath(project)
    if project.rstrip("/\\") == SANDBOX_DIR_NAME or any(
            same_path(absolute, c) for c in (sandbox_dir(root), open_path(root))):
        return open_path(root)
    return absolute
