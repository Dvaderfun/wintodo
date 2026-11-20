"""Fail closed on private scanner reports; publish counts, never finding text."""

import argparse
import json
from pathlib import Path, PureWindowsPath


SOLUTION_PROJECTS = {
    "Wintodo.csproj", "Wintodo.Core.csproj", "Wintodo.Data.csproj",
    "Wintodo.Sync.csproj", "Wintodo.Import.csproj", "Wintodo.Core.Tests.csproj",
    "Wintodo.Data.Tests.csproj", "Wintodo.Sync.Tests.csproj", "Wintodo.Import.Tests.csproj",
}


def secret_summary(report, exit_code):
    if not isinstance(report, list) or any(not isinstance(item, dict) for item in report):
        raise ValueError("invalid report")
    if exit_code not in (0, 1) or (exit_code == 0) != (len(report) == 0):
        raise ValueError("incomplete scan")
    pin = json.loads(Path(__file__).with_name("gitleaks-pin.json").read_text(encoding="utf-8"))
    return {"scanner": "gitleaks", "version": pin["version"], "findings": len(report),
            "status": "FAIL" if report else "PASS"}


def dependency_summary(report, exit_code, project_set="solution"):
    if exit_code != 0 or not isinstance(report, dict) or report.get("version") != 1:
        raise ValueError("incomplete audit")
    if report.get("problems") or report.get("errors"):
        raise ValueError("audit problems")
    parameters = report.get("parameters", "")
    if not isinstance(parameters, str) or "--vulnerable" not in parameters or "--include-transitive" not in parameters:
        raise ValueError("incomplete scope")
    projects = report.get("projects")
    if not isinstance(projects, list):
        raise ValueError("missing projects")
    names = [PureWindowsPath(item["path"]).name for item in projects]
    expected = SOLUTION_PROJECTS if project_set == "solution" else {"Wintodo.csproj"}
    if set(names) != expected or len(names) != len(expected):
        raise ValueError("incomplete project set")
    packages = advisories = 0
    for project in projects:
        if project.get("problems") or project.get("errors"):
            raise ValueError("project problems")
        # NuGet omits frameworks for a project with no known vulnerabilities.
        # An explicitly supplied malformed collection is never a clean result.
        frameworks = project.get("frameworks", [])
        if not isinstance(frameworks, list) or any(not isinstance(item, dict) for item in frameworks):
            raise ValueError("invalid frameworks")
        for framework in frameworks:
            if framework.get("problems") or framework.get("errors"):
                raise ValueError("framework problems")
            for kind in ("topLevelPackages", "transitivePackages"):
                values = framework.get(kind, [])
                if not isinstance(values, list) or any(not isinstance(item, dict) for item in values):
                    raise ValueError("invalid package collection")
                for package in values:
                    vulnerabilities = package.get("vulnerabilities")
                    if not isinstance(vulnerabilities, list) or not vulnerabilities:
                        raise ValueError("invalid vulnerability")
                    packages += 1
                    advisories += len(vulnerabilities)
    return {"scanner": "NuGet", "projects": len(projects), "vulnerable_packages": packages,
            "advisories": advisories, "status": "FAIL" if advisories else "PASS"}


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("kind", choices=("secrets", "dependencies"))
    parser.add_argument("report", type=Path)
    parser.add_argument("--exit-code", type=int, required=True)
    parser.add_argument("--project-set", choices=("solution", "app"), default="solution")
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args(argv)
    try:
        # Never retain yesterday's successful summary after a failed scan.
        args.output.unlink(missing_ok=True)
        report = json.loads(args.report.read_text(encoding="utf-8-sig"))
        summary = (secret_summary(report, args.exit_code) if args.kind == "secrets"
                   else dependency_summary(report, args.exit_code, args.project_set))
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(json.dumps(summary, indent=2) + "\n", encoding="utf-8")
        print(json.dumps(summary))
        return 0 if summary["status"] == "PASS" else 1
    except (OSError, ValueError, TypeError, KeyError, AttributeError):
        print(json.dumps({"scanner": args.kind, "status": "FAIL", "category": "incomplete_report"}))
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
