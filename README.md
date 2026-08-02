# EventManager — Project Guide

This is the guide for **you, the person running this project**. It explains how EventManager gets
planned, built, reviewed, and shipped using three cooperating pieces:

- **AI-DLC** (in this repo) — a structured method that turns your ideas into requirements, user
  stories, and features. You drive it interactively.
- **Jira project AH** ("Ascendant Hammer") — the board where all work lives: features, deliverables,
  and bugs. It's your control panel.
- **loop-agent** (`C:\repos\loop-agent`) — an autonomous build harness that pulls a piece of work
  off the board, builds it, has it reviewed, and hands it back to you for sign-off.

> **This project is greenfield.** Nothing about *what* EventManager is — the language, the
> framework, the database, the features — is decided yet. **You** make those decisions in the
> planning inputs below. The tools never decide the product for you.

You are always in control. The agent proposes and builds; **you** approve the plan and **you** do
the final acceptance. The agent can never mark work "Done" — only you can.

---

## The big picture

```
   YOU ──describe the product──►  AI-DLC Inception  ──► features + deliverables
                                        │
                                        ▼  (seed)
                                 ┌──────────────────────┐
   YOU ──add features/bugs────► │   Jira board (AH)     │ ◄── you watch progress here
   directly, any time           │  Epic = Feature       │
                                 │   Story/Task = work   │
                                 │   Bug = defect        │
                                 └──────────┬────────────┘
                                            │ loop pulls the next "To Do" item
                                            ▼
                                    loop-agent builds it
                                            │
                                            ▼
                            YOU ──review & accept──► Done
```

### The work hierarchy on the board

| Jira type | Means | Who creates it |
|-----------|-------|----------------|
| **Epic** | A **feature** — a coherent capability of EventManager | AI-DLC seeding, or you |
| **Story** / **Task** | A **deliverable** — one UAT-able piece of work the agent builds | AI-DLC seeding, or you |
| **Bug** | A defect to reproduce, fix, and regression-test | You |
| **Sub-task** | The agent's own breakdown of a deliverable | The agent, while building |

### The status workflow (every item moves through these)

```
To Do ──► In Progress ──► Agent Review ──► Human Review ──► Done
  ▲            ▲                │              (YOU do          (YOU
  │            └── found gaps ──┘               UAT here)        set this)
 you or                                          HARD STOP
 AI-DLC
```

- **To Do** — queued. The loop builds the top-ranked To Do item next.
- **In Progress** — the loop is actively building it.
- **Agent Review** — a *separate* AI session reviews the build (independent of the one that wrote
  it). If it finds gaps, the item goes back to In Progress automatically.
- **Human Review** — **a hard stop for you.** The item is complete and ready for your acceptance
  testing. The agent halts here and waits.
- **Done** — **only you** set this, after your UAT passes.

---

## Getting started: plan the project (AI-DLC Inception)

Do this once at the start (and again whenever you want to plan a new batch of features).

1. **Write your inputs.** Fill in the two skeleton files in [`aidlc-inputs/`](aidlc-inputs/):
   - [`aidlc-inputs/vision.md`](aidlc-inputs/vision.md) — what EventManager is, who it's for, and
     the MVP feature list. **Each feature here becomes an Epic.**
   - [`aidlc-inputs/tech-env.md`](aidlc-inputs/tech-env.md) — the stack: language, framework,
     database, how it runs, and the test tooling.

   These are blank on purpose. Be specific — allow-lists and disallow-lists stop the AI from
   guessing. (Guidance links are inside each file.)

2. **Run Inception.** Open Claude Code in this folder (`C:\repos\EventManager`) and say:

   > **Using AI-DLC, build EventManager** — using my `aidlc-inputs/vision.md` and
   > `aidlc-inputs/tech-env.md`.

   Answer its structured questions. It works through requirements → user stories → features/units
   of work. **Review and approve each stage** — you can request changes at any gate. Everything it
   produces is written to `aidlc-docs/` in this repo.

3. **Lock the build stack.** Once `tech-env.md` fixes the stack, configure the loop's checker:
   open `C:\repos\loop-agent\verify.ps1` and set its Build / Lint / Test commands for your stack
   (one-time). This is what the loop uses to know a deliverable is genuinely "green."

---

## Putting work on the board

You have two ways to create work — use whichever fits.

### A. Seed from your AI-DLC plan (bulk)

After Inception, mirror everything it planned onto the board in one step. From
`C:\repos\loop-agent`:

```powershell
pwsh -File jira-loop.ps1 -Seed
```

This creates an **Epic per feature** and the **Stories/Tasks** under each, with acceptance criteria
already filled in. It's safe to re-run after a later Inception pass — it only adds what's new, never
duplicates.

### B. Add work by hand (any time)

You don't have to route everything through AI-DLC. On the [AH board](https://ascendanthammer.atlassian.net/jira/software/projects/KAN/boards/2)
you can directly:

- **Add a feature** → create an **Epic**, then add **Stories/Tasks** under it for each deliverable.
  Put clear **acceptance criteria** in each Story's description — those become the automated tests
  that define "done."
- **Add a single deliverable** to an existing feature → create a **Story** or **Task** under that
  Epic.
- **Report a bug** → create a **Bug** in **To Do**. Describe how to reproduce it and what the
  correct behavior should be. The loop treats a bug as "reproduce, fix, and add a regression test."

Anything you leave in **To Do** gets picked up automatically on the next run — whether it came from
AI-DLC or from you.

> **Write good acceptance criteria.** They are the definition of done and become real tests. Prefer
> concrete GIVEN / WHEN / THEN statements over vague wishes. Anything that can't be checked
> automatically belongs in a comment for your manual UAT, not in the acceptance criteria.

---

## Building work

The loop builds **one deliverable at a time**, top-ranked To Do item first. From
`C:\repos\loop-agent`:

```powershell
pwsh -File jira-loop.ps1            # pull the next To-Do item, plan it, then stop for your review
```

Review the generated plan and the acceptance tests it wrote (paths are printed). If the tests
captured your intent, start the build:

```powershell
pwsh -File jira-loop.ps1 -Approve   # build it green, run Agent Review, move to Human Review, stop
```

While this runs, watch the item on the board move `In Progress → Agent Review → Human Review`. Each
run posts a **comment** on the item with what it did and which commits it made.

**Hands-off option:** `pwsh -File jira-loop.ps1 -AutoApprove` does plan + build in one shot. It skips
only the plan-review gate — it can **never** skip your Human Review.

To check what's active at any time: `pwsh -File jira-loop.ps1 -Status`.

---

## Reviewing and accepting work (your job)

When an item reaches **Human Review**, it's ready for you:

1. Read the agent's comment on the Jira item (outcome + commits). The code is in the EventManager
   repo.
2. Do your acceptance testing — run it, exercise the feature, check the things a test can't.
3. Then:
   - **It's good** → move the item to **Done** in Jira. Run `jira-loop.ps1` again for the next item.
   - **It's not** → move it back to **In Progress** (or To Do) and add a comment explaining what's
     wrong. The next run reads your comment and continues. If it's a *new* problem you found, file a
     **Bug** instead.

If a run ends **stalled** (the agent got stuck), the item keeps a `blocked` label and a comment
explaining where it stopped — that's your cue to look, adjust, and re-run.

---

## Watching progress / reporting

Your Jira board is the live report:

- **Board columns** = the five statuses, so a glance shows what's queued, building, in review, and
  done.
- **Each item's comments** = the agent's running log: what it built, commits, and any blockers.
- **Epics** = feature-level rollup of how much of each feature is complete.

Useful board filters (JQL):

- Everything awaiting *your* acceptance: `project = AH AND status = "Human Review"`
- What's blocked: `project = AH AND labels = blocked`
- What the loop will pick up next: `project = AH AND issuetype in (Story, Task, Bug) AND statusCategory = "To Do" ORDER BY Rank ASC`

---

## Where things live

| Thing | Location |
|-------|----------|
| Your planning inputs | `aidlc-inputs/vision.md`, `aidlc-inputs/tech-env.md` (this repo) |
| AI-DLC's generated plan | `aidlc-docs/` (this repo, after Inception) |
| The product code | this repo (built by the loop) |
| The build harness + commands | `C:\repos\loop-agent` (`jira-loop.ps1`) |
| Full mechanical runbook | `C:\repos\loop-agent\docs\JIRA-AIDLC-RUNBOOK.md` |
| The board | Jira project **AH** on `ascendanthammer.atlassian.net` |

---

## First-time checklist

- [ ] loop-agent authenticated for headless use, and the Atlassian connection authorized once
      interactively (see the runbook's one-time setup).
- [ ] `aidlc-inputs/vision.md` and `tech-env.md` filled in.
- [ ] Ran `Using AI-DLC, build EventManager…` through to features/stories.
- [ ] Configured `verify.ps1` for the chosen stack.
- [ ] `jira-loop.ps1 -Seed` populated the board.
- [ ] Built the first deliverable and did your UAT.

For exact commands, flags, and troubleshooting, see the runbook:
`C:\repos\loop-agent\docs\JIRA-AIDLC-RUNBOOK.md`.
