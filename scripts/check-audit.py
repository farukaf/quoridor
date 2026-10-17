"""Fail CI when NuGet reports a known direct or transitive vulnerability."""
import json
import sys
with open(sys.argv[1], encoding='utf-8-sig') as stream:
    report = json.load(stream)
findings = [p for project in report.get('projects', []) for framework in project.get('frameworks', [])
            for kind in ('topLevelPackages', 'transitivePackages') for p in framework.get(kind, [])
            if p.get('vulnerabilities')]
for finding in findings:
    print(finding)
assert not findings, 'NuGet vulnerability findings require remediation'
