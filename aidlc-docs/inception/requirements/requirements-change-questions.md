# Requirements Change — Multi-Organizer Support

You asked for P1 to support more than one Organizer per event. This touches several approved requirements artifacts:
- Persona summary (§3): currently describes Organizer as "sole administrative authority"
- FR-2.1/FR-2.5/FR-3.4/FR-6.4: currently assume a single Organizer acting on an event
- NFR-2.5: object-level ownership check is currently "organizer owns event" (singular)
- NFR-5.1: scale envelope explicitly states "single organizer per event"

Please answer the questions below so the change is scoped correctly before I update requirements.md (and the not-yet-approved stories.md/personas.md).

## Question 1
When an event has multiple Organizers, what should their permission model be?

A) Fully symmetric — every Organizer on an event has identical full admin rights, no owner/creator distinction

B) Owner + co-organizers — the creator remains a permanent "owner" with some rights co-organizers don't get (e.g., deleting the event, removing other organizers, transferring ownership); co-organizers otherwise have full event admin rights

C) Role-based — co-organizers can be granted a restricted role (e.g., view-only or day-of-only) in addition to full admin, configurable per person

D) Other (please describe after [Answer]: tag below)

[Answer]: C. Ensure that is a full admin and a co-organizer role. The co-organizer role will resemble what is mentioned in B. We should architect around the RBAC method but provide default roles to given personas depending upon how they register.

## Question 2
How does an event gain an additional Organizer?

A) The existing Organizer invites by email; invited person must have (or create) an Organizer account before the invite is accepted

B) The existing Organizer adds any existing Organizer-role account directly by lookup (no email invite/accept flow)

C) Both — email invite for new people, direct add for existing known accounts

D) Other (please describe after [Answer]: tag below)

[Answer]: C

## Question 3
Is there a cap on how many Organizers a single event can have in the MVP?

A) No cap — any number of co-organizers allowed

B) Small fixed cap (e.g., 2-5) suitable for a dojo/co-hosted event scenario

C) Other (please describe after [Answer]: tag below)

[Answer]: A

## Question 4
NFR-5.1's scale envelope currently states "single organizer per event" as a scale assumption. How should this be revised?

A) Remove the "single organizer" language entirely and replace with a small typical count (e.g., "1-3 organizers per event, typically co-located at the venue")

B) Keep a numeric scale assumption but raise it (e.g., "up to N organizers per event") tied to your answer to Question 3

C) Other (please describe after [Answer]: tag below)

[Answer]: A
