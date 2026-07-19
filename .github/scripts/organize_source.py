from __future__ import annotations

import re
from pathlib import Path

ROOT = Path("MagicSettings")
PROJECTS = {
    ROOT / "MagicSettings": "runtime",
    ROOT / "MagicSettings.Server": "server",
    ROOT / "MagicSettings.Tests": "tests",
}

TYPE_RE = re.compile(
    r"^\s*(?:(?:public|internal|protected|private)\s+)"
    r"(?:(?:sealed|static|abstract|partial|readonly|ref)\s+)*"
    r"(?:record(?:\s+(?:class|struct))?|class|interface|enum|struct)\s+"
    r"([A-Za-z_][A-Za-z0-9_]*)",
    re.MULTILINE,
)
NAMESPACE_RE = re.compile(r"^namespace\s+([A-Za-z_][A-Za-z0-9_.]*)\s*$", re.MULTILINE)


def sanitize_csharp(text: str) -> str:
    """Mask comments and literals while preserving offsets and newlines."""
    out = list(text)
    i = 0
    state = "normal"
    raw_quotes = 0
    while i < len(text):
        ch = text[i]
        nxt = text[i + 1] if i + 1 < len(text) else ""
        if state == "normal":
            if ch == "/" and nxt == "/":
                out[i] = out[i + 1] = " "
                i += 2
                state = "line_comment"
                continue
            if ch == "/" and nxt == "*":
                out[i] = out[i + 1] = " "
                i += 2
                state = "block_comment"
                continue
            quote_run = 0
            if ch == '"':
                while i + quote_run < len(text) and text[i + quote_run] == '"':
                    quote_run += 1
            if quote_run >= 3:
                raw_quotes = quote_run
                for j in range(quote_run):
                    out[i + j] = " "
                i += quote_run
                state = "raw_string"
                continue
            if ch == "@" and nxt == '"':
                out[i] = out[i + 1] = " "
                i += 2
                state = "verbatim_string"
                continue
            if ch == "$" and nxt == '"':
                out[i] = out[i + 1] = " "
                i += 2
                state = "string"
                continue
            if ch in "$@" and nxt in "$@" and i + 2 < len(text) and text[i + 2] == '"':
                out[i] = out[i + 1] = out[i + 2] = " "
                i += 3
                state = "verbatim_string"
                continue
            if ch == '"':
                out[i] = " "
                i += 1
                state = "string"
                continue
            if ch == "'":
                out[i] = " "
                i += 1
                state = "char"
                continue
            i += 1
            continue
        if state == "line_comment":
            if ch == "\n":
                state = "normal"
            else:
                out[i] = " "
            i += 1
            continue
        if state == "block_comment":
            if ch == "*" and nxt == "/":
                out[i] = out[i + 1] = " "
                i += 2
                state = "normal"
            else:
                if ch != "\n":
                    out[i] = " "
                i += 1
            continue
        if state in {"string", "char"}:
            terminator = '"' if state == "string" else "'"
            if ch == "\\":
                out[i] = " "
                if i + 1 < len(text):
                    if text[i + 1] != "\n":
                        out[i + 1] = " "
                    i += 2
                else:
                    i += 1
            elif ch == terminator:
                out[i] = " "
                i += 1
                state = "normal"
            else:
                if ch != "\n":
                    out[i] = " "
                i += 1
            continue
        if state == "verbatim_string":
            if ch == '"' and nxt == '"':
                out[i] = out[i + 1] = " "
                i += 2
            elif ch == '"':
                out[i] = " "
                i += 1
                state = "normal"
            else:
                if ch != "\n":
                    out[i] = " "
                i += 1
            continue
        if state == "raw_string":
            run = 0
            if ch == '"':
                while i + run < len(text) and text[i + run] == '"':
                    run += 1
            if run >= raw_quotes:
                for j in range(raw_quotes):
                    out[i + j] = " "
                i += raw_quotes
                state = "normal"
            else:
                if ch != "\n":
                    out[i] = " "
                i += 1
    return "".join(out)


def matching_brace(masked: str, opening: int) -> int:
    depth = 0
    for index in range(opening, len(masked)):
        if masked[index] == "{":
            depth += 1
        elif masked[index] == "}":
            depth -= 1
            if depth == 0:
                return index
    raise RuntimeError(f"No matching brace at offset {opening}")


def declaration_starts(body: str, masked: str) -> list[tuple[int, str]]:
    starts: list[tuple[int, str]] = []
    depth = 0
    position = 0
    line_start = 0
    while line_start <= len(body):
        while position < line_start:
            if masked[position] == "{":
                depth += 1
            elif masked[position] == "}":
                depth -= 1
            position += 1
        line_end = body.find("\n", line_start)
        if line_end < 0:
            line_end = len(body)
        if depth == 0:
            match = TYPE_RE.match(body[line_start:line_end])
            if match:
                starts.append((line_start, match.group(1)))
        if line_end == len(body):
            break
        line_start = line_end + 1
    return starts


def include_leading_metadata(body: str, start: int, floor: int) -> int:
    lines = body[:start].splitlines(keepends=True)
    cursor = start
    while lines:
        line = lines[-1]
        stripped = line.strip()
        if not stripped or stripped.startswith("///") or stripped.startswith("//") or stripped.startswith("["):
            candidate = cursor - len(line)
            if candidate < floor:
                break
            cursor = candidate
            lines.pop()
        else:
            break
    return cursor


def runtime_folder(name: str) -> str:
    if name.startswith("I") and len(name) > 1 and name[1].isupper():
        return "Abstractions"
    if name.endswith("Attribute"):
        return "Metadata"
    if "Secret" in name:
        return "Secrets"
    if "Migration" in name:
        return "Migrations"
    if "Identity" in name or "Authentication" in name or name in {"MagicHash", "MagicNodeAuthenticationHandler", "MagicHttpClientBuilderExtensions"}:
        return "Security"
    if "ControlPlane" in name or name in {"MagicManifestBuilder", "HttpMagicControlPlaneTransport"}:
        return "ControlPlane"
    if (
        name in {
            "MagicFailureAction", "MagicArrayMergePolicy", "MagicSettingsFailurePolicy",
            "MagicSettingsOptions", "MagicSettingsEnvironmentResolver", "MagicSettingsPathResolver",
            "MagicIdentityPathResolver", "MagicSettingsConfigurationSource",
            "MagicSettingsConfigurationProvider", "MagicSettingsDocumentResult",
            "MagicSettingsDocumentStore", "MagicJsonPath", "MagicJsonFlattener",
            "MagicEnvironmentOverrides",
        }
        or "Configuration" in name
        or name.endswith("Options")
        or name.endswith("PathResolver")
    ):
        return "Configuration"
    if "Initialization" in name or "Runtime" in name or "HostedService" in name or "CommandLine" in name:
        return "Runtime"
    return "Internal"


def server_folder(name: str) -> str:
    if name.startswith("I") and len(name) > 1 and name[1].isupper():
        return "Abstractions"
    if name.startswith("InMemory"):
        return "InMemory"
    if "Secret" in name:
        return "Secrets"
    if "Proof" in name or "AspNet" in name:
        return "Authentication"
    if "Credential" in name:
        return "Credentials"
    if "Sync" in name or "RemoteRecord" in name:
        return "Synchronization"
    return "Internal"


def test_folder(name: str) -> str:
    if name in {"TemporaryDirectory", "TestSettings", "TestApplication", "TestDatabase", "TestControlPlane", "TestControlPlaneSection"}:
        return "Infrastructure"
    if any(token in name for token in ("Credential", "Enrollment", "Identity", "Proof", "Secret")):
        return "Security"
    if "Server" in name:
        return "Server"
    return "Configuration"


def folder_for(kind: str, name: str) -> str:
    if kind == "runtime":
        return runtime_folder(name)
    if kind == "server":
        return server_folder(name)
    return test_folder(name)


created: list[Path] = []
deleted: list[Path] = []
seen: set[Path] = set()

for project, kind in PROJECTS.items():
    source_files = sorted(project.glob("Source.*.cs"))
    if not source_files:
        raise RuntimeError(f"No consolidated source files found in {project}")

    for source in source_files:
        text = source.read_text(encoding="utf-8")
        masked = sanitize_csharp(text)
        namespace_matches = list(NAMESPACE_RE.finditer(masked))
        if not namespace_matches:
            raise RuntimeError(f"No block namespace found in {source}")

        preamble = text[: namespace_matches[0].start()]
        preamble = "\n".join(
            line
            for line in preamble.splitlines()
            if not line.startswith("// Consolidated source file.") and line.strip() != "global using Xunit;"
        ).strip()

        for namespace_match in namespace_matches:
            namespace = namespace_match.group(1)
            opening = masked.find("{", namespace_match.end())
            closing = matching_brace(masked, opening)
            body = text[opening + 1 : closing]
            body_masked = masked[opening + 1 : closing]
            starts = declaration_starts(body, body_masked)
            if not starts:
                raise RuntimeError(f"Namespace {namespace} in {source} has no declarations")

            adjusted: list[tuple[int, str]] = []
            floor = 0
            for start, name in starts:
                adjusted.append((include_leading_metadata(body, start, floor), name))
                floor = start

            for index, (start, name) in enumerate(adjusted):
                end = adjusted[index + 1][0] if index + 1 < len(adjusted) else len(body)
                declaration = body[start:end].strip()
                folder = folder_for(kind, name)
                destination = project / folder / f"{name}.cs"
                if destination in seen or destination.exists():
                    raise RuntimeError(f"Duplicate destination {destination}")
                seen.add(destination)
                destination.parent.mkdir(parents=True, exist_ok=True)
                parts = ([preamble] if preamble else []) + [f"namespace {namespace};", declaration]
                destination.write_text("\n\n".join(parts).rstrip() + "\n", encoding="utf-8")
                created.append(destination)

        source.unlink()
        deleted.append(source)

    scaffold = project / "UnitTest1.cs"
    if scaffold.exists():
        scaffold.unlink()
        deleted.append(scaffold)

(ROOT / "MagicSettings.Tests" / "GlobalUsings.cs").write_text("global using Xunit;\n", encoding="utf-8")
created.append(ROOT / "MagicSettings.Tests" / "GlobalUsings.cs")

remaining = list(ROOT.rglob("Source.*.cs"))
if remaining:
    raise RuntimeError(f"Consolidated files remain: {remaining}")

print(f"Created {len(created)} focused source files and removed {len(deleted)} consolidated/scaffold files.")
for path in sorted(created):
    print(f"  + {path}")
for path in sorted(deleted):
    print(f"  - {path}")
