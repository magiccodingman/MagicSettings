from pathlib import Path
import shutil

root = Path("MagicSettings")
runtime = root / "MagicSettings"
server = root / "MagicSettings.Server"
tests = root / "MagicSettings.Tests"


def remove_file_usings(project: Path) -> None:
    for path in project.rglob("*.cs"):
        if path.name == "GlobalUsings.cs":
            continue
        text = path.read_text(encoding="utf-8")
        lines = text.splitlines()
        namespace_index = next(
            (index for index, line in enumerate(lines) if line.startswith("namespace ")),
            None,
        )
        if namespace_index is None:
            raise RuntimeError(f"No namespace declaration found in {path}")
        path.write_text("\n".join(lines[namespace_index:]).rstrip() + "\n", encoding="utf-8")


remove_file_usings(runtime)
remove_file_usings(server)
remove_file_usings(tests)

(runtime / "GlobalUsings.cs").write_text(
    """global using System.Collections;
global using System.Collections.Concurrent;
global using System.ComponentModel.DataAnnotations;
global using System.Globalization;
global using System.Net.Http.Headers;
global using System.Net.Security;
global using System.Reflection;
global using System.Security.Cryptography;
global using System.Security.Cryptography.X509Certificates;
global using System.Text;
global using System.Text.Json;
global using System.Text.Json.Nodes;
global using MagicSettings.Share;
global using Microsoft.Extensions.Configuration;
global using Microsoft.Extensions.DependencyInjection;
global using Microsoft.Extensions.Hosting;
global using Microsoft.Extensions.Logging;
global using Microsoft.Extensions.Options;
""",
    encoding="utf-8",
)

(server / "GlobalUsings.cs").write_text(
    """global using System.Collections.Concurrent;
global using System.Security.Cryptography;
global using System.Text;
global using MagicSettings.Share;
global using Microsoft.AspNetCore.Http;
""",
    encoding="utf-8",
)

(tests / "GlobalUsings.cs").write_text(
    """global using System.Text.Json.Nodes;
global using MagicSettings.Server;
global using MagicSettings.Share;
global using Microsoft.Extensions.DependencyInjection;
global using Microsoft.Extensions.Hosting;
global using Xunit;
""",
    encoding="utf-8",
)

moves = {
    runtime / "Internal" / "MagicSettingExplanation.cs": runtime / "Diagnostics" / "MagicSettingExplanation.cs",
    runtime / "Internal" / "MagicSettingSourceValue.cs": runtime / "Diagnostics" / "MagicSettingSourceValue.cs",
    runtime / "Internal" / "MagicSettingsChangedEventArgs.cs": runtime / "Runtime" / "MagicSettingsChangedEventArgs.cs",
}
for source, destination in moves.items():
    if not source.exists():
        raise RuntimeError(f"Expected source file does not exist: {source}")
    destination.parent.mkdir(parents=True, exist_ok=True)
    shutil.move(source, destination)

internal = runtime / "Internal"
if internal.exists():
    internal.rmdir()

print("Centralized project usings and corrected feature placement.")
