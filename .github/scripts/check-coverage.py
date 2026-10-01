"""Fails the build unless unit-test coverage is above the minimum (default 90%).

Both overall line coverage and overall branch coverage must be above the minimum.
Classes below the minimum are listed to show where to add tests.

Usage: python3 check-coverage.py <directory containing coverage.cobertura.xml files> [minimum percent]
"""
import sys
import xml.etree.ElementTree as ET
from collections import defaultdict
from pathlib import Path

minimum = float(sys.argv[2]) if len(sys.argv) > 2 else 90.0

reports = list(Path(sys.argv[1]).rglob("coverage.cobertura.xml"))
if not reports:
    sys.exit("No coverage.cobertura.xml found. Did the tests run with coverage?")

# class name -> [lines, covered lines, branches, covered branches]
totals = defaultdict(lambda: [0, 0, 0, 0])

for report in reports:
    for cls in ET.parse(report).iter("class"):
        name = cls.get("name").split("<")[0].split("/")[0]
        entry = totals[name]
        for line in cls.iter("line"):
            entry[0] += 1
            entry[1] += int(line.get("hits")) > 0
            if line.get("branch") == "True":
                covered, total = line.get("condition-coverage").split("(")[1].rstrip(")").split("/")
                entry[2] += int(total)
                entry[3] += int(covered)


def percent(covered, total):
    return 100 * covered / total if total else 100.0


lines = sum(x[0] for x in totals.values())
covered_lines = sum(x[1] for x in totals.values())
branches = sum(x[2] for x in totals.values())
covered_branches = sum(x[3] for x in totals.values())

line_pct = percent(covered_lines, lines)
branch_pct = percent(covered_branches, branches)

print(f"Line coverage:   {line_pct:.1f}% ({covered_lines}/{lines})")
print(f"Branch coverage: {branch_pct:.1f}% ({covered_branches}/{branches})")

low = [
    f"  {name}: line {percent(l, lt):.1f}%, branch {percent(b, bt):.1f}%"
    for name, (lt, l, bt, b) in sorted(totals.items())
    if percent(l, lt) <= minimum or percent(b, bt) <= minimum
]
if low:
    print(f"Classes at or below {minimum:g}%:")
    print("\n".join(low))

if line_pct <= minimum or branch_pct <= minimum:
    sys.exit(f"Coverage must be above {minimum:g}% for both lines and branches.")

print(f"Coverage is above {minimum:g}%.")
