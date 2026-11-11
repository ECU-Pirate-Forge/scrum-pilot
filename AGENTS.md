# Copilot Agent Instructions

This file defines the agent workflow for ScrumPilot. Project-wide architecture
and coding rules live in the [project instructions](.github/copilot-instructions.md).

---

## 🤖 Agent Role
You are a helpful development assistant for this project.  
Your responsibilities include:
- Explaining code changes before making them
- Following project conventions
- Keeping modifications small and reviewable

---

## 🧠 Project Knowledge
Use the [architecture](docs/architecture.md) and
[contribution guide](CONTRIBUTING.md) to understand the API, SQL Server control
and organization databases, shared client transport, Aspire, and both web clients.
Read the [operations runbook](docs/multi-tenancy-operations.md) before changing
lifecycle or recovery behavior. Do not use shared installations as test fixtures.

---

## 🛠️ Allowed Actions
The agent may:
- Create or modify files when asked
- Suggest improvements
- Run tasks or commands that the student explicitly approves

An approved implementation plan authorizes its described changes and validation.
Use the [testing guide](docs/codebase/TESTING.md) for commands, isolated SQL
fixtures, and the required distinction between automated and live UI evidence.

---

## 🚫 Restricted Actions
The agent should NOT:
- Delete files without confirmation
- Introduce new frameworks without approval
- Make large refactors unless requested

Preserve unrelated working changes and existing data. Stop when the user pauses
work and resume only on explicit instruction. Do not claim full parity from
route inventories or passing unit tests alone.
- Performance constraints  

---

## 📋 Workflow Expectations
When performing tasks, the agent should:
1. Explain the plan  
2. Wait for confirmation  
3. Apply changes in small steps  
4. Summarize what changed  

Report the actual validation results, unresolved failures, and limitations.
Keep changes small, maintain documentation, and follow the contribution guide.
