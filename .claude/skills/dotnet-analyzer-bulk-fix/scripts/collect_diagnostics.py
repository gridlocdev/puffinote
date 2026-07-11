#!/usr/bin/env python3
"""
collect_diagnostics.py — enumerate and bucket Roslyn/.NET analyzer diagnostics.

Purpose
-------
Produce a complete, structured list of analyzer diagnostics (esp. the ones
`dotnet format` can't auto-fix, like IDE1006 and IDE0060) so fixes can be planned
and partitioned across subagents. Works two ways:

  1. Run a command and parse its output:
        python3 collect_diagnostics.py --run build  --out diagnostics.json
        python3 collect_diagnostics.py --run format --out diagnostics.json
  2. Parse a log you already captured:
        python3 collect_diagnostics.py --log build.log --out diagnostics.json

Prerequisites (important)
-------------------------
* IDE-prefixed style rules (IDE0060, IDE1006, ...) only appear at BUILD time when
  the project has  <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>  and the
  rule severity in .editorconfig is `warning` or `error`. Without that, use
  `--run format` (dotnet format prints them regardless) or capture a
  `dotnet format --verify-no-changes` log.
* `--run` requires the dotnet SDK on PATH and a restored solution. If restore is
  needed, run `dotnet restore` first; this script does not restore for you.

Output
------
JSON with diagnostics bucketed by rule id and by project, a flat list, the set of
rule ids that were reported as non-auto-fixable, and a suggested subagent partition
(by project, which is a coarse boundary — see the skill's Lane A / Lane B notes;
cross-project renames still need care). Also prints a human-readable summary.

Stdlib only. No network.
"""

import argparse
import json
import os
import re
import subprocess
import sys
from collections import defaultdict, OrderedDict

# path(line,col): severity IDExxxx: message [optional-project.csproj]
DIAG_RE = re.compile(
    r"""^\s*
        (?P<path>[^(]+?)                       # file path (no '(' )
        \((?P<line>\d+),(?P<col>\d+)\)         # (line,col)
        :\s*
        (?P<sev>error|warning|info|message)\s+ # severity
        (?P<id>[A-Z]{2,}\d+)                   # rule id: IDE1006, CA1801, CS0219...
        :\s*(?P<msg>.*?)                        # message
        (?:\s*\[(?P<proj>[^\]]+)\])?           # optional [proj.csproj]
        \s*$""",
    re.VERBOSE,
)

# dotnet format's "can't fix" lines, e.g.:
#   Unable to fix IDE1006. Code fix NamingStyleCodeFixProvider doesn't support Fix All in Solution.
#   Unable to fix IDE0060. No associated code fix found.
UNABLE_RE = re.compile(r"Unable to fix\s+(?P<id>[A-Z]{2,}\d+)\.\s*(?P<reason>.*)$")

RUN_COMMANDS = {
    "build": ["dotnet", "build", "-nologo", "-v", "normal"],
    # style + analyzers, report but don't change; prints diagnostics it can't fix
    "format": ["dotnet", "format", "--verify-no-changes", "--verbosity", "diagnostic"],
}


def run_command(kind, solution):
    cmd = list(RUN_COMMANDS[kind])
    if solution:
        cmd.append(solution)
    sys.stderr.write("Running: " + " ".join(cmd) + "\n")
    try:
        proc = subprocess.run(cmd, capture_output=True, text=True)
    except FileNotFoundError:
        sys.exit("error: 'dotnet' not found on PATH. Install the .NET SDK or use --log.")
    return (proc.stdout or "") + "\n" + (proc.stderr or "")


def normalize_project(path, proj):
    if proj:
        return os.path.basename(proj)
    # Fall back to the top-level folder under the repo as a coarse project proxy.
    norm = path.replace("\\", "/")
    parts = [p for p in norm.split("/") if p not in ("", ".")]
    return parts[0] if parts else "(unknown)"


def parse(text):
    diags = []
    unfixable = OrderedDict()
    seen = set()
    for raw in text.splitlines():
        m = DIAG_RE.match(raw)
        if m:
            d = m.groupdict()
            key = (d["path"], d["line"], d["col"], d["id"], d["msg"])
            if key in seen:  # MSBuild repeats diagnostics per referencing project
                continue
            seen.add(key)
            diags.append({
                "id": d["id"],
                "severity": d["sev"],
                "path": d["path"].strip().replace("\\", "/"),
                "line": int(d["line"]),
                "col": int(d["col"]),
                "message": d["msg"].strip(),
                "project": normalize_project(d["path"], d["proj"]),
            })
            continue
        u = UNABLE_RE.search(raw)
        if u:
            unfixable.setdefault(u.group("id"), u.group("reason").strip())
    return diags, unfixable


def suggest_partition(diags, buckets=4):
    """Coarse partition by project so each subagent owns whole projects.
    Projects are a safe-ish Lane-A boundary; cross-project renames (Lane B) still
    need serialization or worktree isolation — see SKILL.md."""
    by_proj = defaultdict(int)
    for d in diags:
        by_proj[d["project"]] += 1
    # Greedy longest-processing-time bin packing on diagnostic counts.
    order = sorted(by_proj.items(), key=lambda kv: kv[1], reverse=True)
    bins = [{"projects": [], "count": 0} for _ in range(max(1, buckets))]
    for proj, cnt in order:
        b = min(bins, key=lambda x: x["count"])
        b["projects"].append(proj)
        b["count"] += cnt
    return [b for b in bins if b["projects"]]


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    src = ap.add_mutually_exclusive_group(required=True)
    src.add_argument("--run", choices=sorted(RUN_COMMANDS), help="run this dotnet command and parse its output")
    src.add_argument("--log", help="parse an already-captured log file")
    ap.add_argument("--solution", help="solution/project path passed to the dotnet command")
    ap.add_argument("--out", default="diagnostics.json", help="output JSON path (default: diagnostics.json)")
    ap.add_argument("--only", nargs="*", help="restrict to these rule ids, e.g. --only IDE1006 IDE0060")
    ap.add_argument("--buckets", type=int, default=4, help="how many subagent buckets to suggest (default 4)")
    args = ap.parse_args()

    if args.log:
        with open(args.log, "r", encoding="utf-8", errors="replace") as fh:
            text = fh.read()
    else:
        text = run_command(args.run, args.solution)

    diags, unfixable = parse(text)
    if args.only:
        wanted = {x.upper() for x in args.only}
        diags = [d for d in diags if d["id"] in wanted]

    by_rule = defaultdict(list)
    by_project = defaultdict(list)
    for d in diags:
        by_rule[d["id"]].append(d)
        by_project[d["project"]].append(d)

    result = {
        "total": len(diags),
        "counts_by_rule": {k: len(v) for k, v in sorted(by_rule.items())},
        "counts_by_project": {k: len(v) for k, v in sorted(by_project.items())},
        "reported_non_auto_fixable": unfixable,
        "diagnostics": diags,
        "suggested_partition": suggest_partition(diags, args.buckets),
    }

    with open(args.out, "w", encoding="utf-8") as fh:
        json.dump(result, fh, indent=2)

    # Human-readable summary to stderr so stdout stays pipe-friendly.
    w = sys.stderr.write
    w("\n=== Diagnostic summary ===\n")
    w(f"Total diagnostics: {result['total']}\n")
    if result["counts_by_rule"]:
        w("By rule:\n")
        for rid, n in result["counts_by_rule"].items():
            flag = "  (reported NON-auto-fixable)" if rid in unfixable else ""
            w(f"  {rid:<10} {n}{flag}\n")
    if unfixable:
        w("Rules dotnet reported it could not auto-fix:\n")
        for rid, reason in unfixable.items():
            w(f"  {rid}: {reason}\n")
    if result["suggested_partition"]:
        w(f"Suggested {len(result['suggested_partition'])} subagent bucket(s) (by project):\n")
        for i, b in enumerate(result["suggested_partition"], 1):
            w(f"  bucket {i}: {b['count']} diags across {len(b['projects'])} project(s): "
              f"{', '.join(b['projects'])}\n")
    w(f"\nFull structured list written to {args.out}\n")
    w("Reminder: partition by INDEPENDENCE, not just file count — see SKILL.md Lane A/Lane B.\n")


if __name__ == "__main__":
    main()
