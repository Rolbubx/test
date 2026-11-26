#!/usr/bin/env python3
"""Decides whether CI may publish a release, from the TRX reports of `dotnet test`.

    test_gate.py <all.trx> <critical.trx> [--min-rate 97]

Publish only if at least --min-rate % of all the tests pass AND every critical test passes
(tests marked [Trait("Category", "Critical")], run on their own with --filter Category=Critical).
Writes publish=true|false to $GITHUB_OUTPUT and a short table to $GITHUB_STEP_SUMMARY.
Exit code: 0 = publish, 1 = do not publish.
"""
import argparse
import os
import sys
import xml.etree.ElementTree as ET

NS = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}


def read(path):
    """(total, passed, names of the tests that did not pass) of a TRX file; None if it is missing or unreadable."""
    try:
        root = ET.parse(path).getroot()
    except (OSError, ET.ParseError) as e:
        print(f"::warning::Cannot read {path}: {e}")
        return None
    results = root.findall(".//t:UnitTestResult", NS)
    failed = [r.get("testName", "?") for r in results if r.get("outcome") != "Passed"]
    return len(results), len(results) - len(failed), failed


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("all")
    ap.add_argument("critical")
    ap.add_argument("--min-rate", type=float, default=97.0)
    a = ap.parse_args()

    everything, critical = read(a.all), read(a.critical)
    reasons = []
    if everything is None or everything[0] == 0:
        reasons.append("no test results (the tests did not build or run)")
        everything = everything or (0, 0, [])
    if critical is None or critical[0] == 0:
        reasons.append("no critical test ran (is a test still marked Category=Critical?)")
        critical = critical or (0, 0, [])

    total, passed, failed = everything
    rate = 100.0 * passed / total if total else 0.0
    if total and rate < a.min_rate:
        reasons.append(f"pass rate {rate:.1f}% < {a.min_rate:g}%")
    if critical[2]:
        reasons.append(f"{len(critical[2])} critical test(s) failed")

    publish = not reasons
    lines = [
        "## Test gate",
        "",
        "| | Passed | Total |",
        "|---|---|---|",
        f"| All tests | {passed} ({rate:.1f}%) | {total} |",
        f"| Critical tests | {critical[1]} | {critical[0]} |",
        "",
        f"**{'Publishing the release' if publish else 'Not publishing'}**" + ("" if publish else ": " + "; ".join(reasons)),
    ]
    if failed:
        lines += ["", "Failed tests:"] + [f"- {'**critical** ' if n in critical[2] else ''}`{n}`" for n in failed]
    text = "\n".join(lines)
    print(text)

    if os.environ.get("GITHUB_STEP_SUMMARY"):
        with open(os.environ["GITHUB_STEP_SUMMARY"], "a", encoding="utf-8") as f:
            f.write(text + "\n")
    if os.environ.get("GITHUB_OUTPUT"):
        with open(os.environ["GITHUB_OUTPUT"], "a", encoding="utf-8") as f:
            f.write(f"publish={'true' if publish else 'false'}\n")
    return 0 if publish else 1


if __name__ == "__main__":
    sys.exit(main())
