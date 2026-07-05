# Lessons Log — Unity-Skills harness

Append when: an AI agent misreads a SKILL.md, docs drift from C# reality,
or a documented workflow fails. One lesson = 3 lines, newest at top:

```
## YYYY-MM-DD <short title>
TRAP: <what went wrong, concrete — file:line>
RULE: <what to do instead — imperative, checkable>
```

## 2026-07-05 counts and index rows drift from disk
TRAP: agent.md said 39 *Skills.cs (disk: 41); skills index omitted
ugui-builder and mislabeled uitoolkit batch support — added code never
propagated to docs.
RULE: When adding a *Skills.cs or module, update agent.md tree/counts and
skills/SKILL.md index row in the same commit; verify counts with a glob,
never by memory.
