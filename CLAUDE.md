# Claude Code compatibility

@AGENTS.md

## Skills

This repo's skills live under `.agents/skills/` (not `.claude/skills/`), so they don't appear in Claude Code's Skill tool listing or work as slash commands. Treat them as reference documentation instead: when a task touches one of their topics, read the relevant `.agents/skills/<name>/SKILL.md` directly and follow it, the same as AGENTS.md's inline references to specific skills (e.g. `playwright-blazor`) already imply.
