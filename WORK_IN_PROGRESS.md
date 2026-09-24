# Live Agent Work Tracker (`WORK_IN_PROGRESS.md`)

This board coordinates multiple AI agents (Claude Opus, Gemini, etc.) working simultaneously across different conversations to prevent conflicting edits.

---

## Active Agent Locks

| Agent / Model | Current Task | Locked Files | Status | Last Updated |
| :--- | :--- | :--- | :--- | :--- |
| *(None)* | Idle | None | Available | Initialized |

---

## Guidelines for Agents
1. **Acquire Lock**: Before modifying code, add an entry above with your model name, task, and the exact files you are editing (`IN PROGRESS`).
2. **Handle Conflicts**: If a file you need is locked by another agent, work on other pending scripts first. If no other work can be done, wait and do NOT edit the locked file.
3. **Release Lock**: As soon as your edits are finished, clear your row or set status back to `Available` so other agents can proceed.
