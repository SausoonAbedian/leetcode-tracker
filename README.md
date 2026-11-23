# DSA Tracker

A web app, run with Docker, for practising the NeetCode 250 problems from your `C:\interview\dsa` project. It does the following:
- tracks every attempt: time, hints and pass/fail
- builds a priority queue of what to solve next, overall and per topic
- keeps the code you submitted, so you can download it again
- shows statistics and your weak and strong topics
- runs the tests on your code and asks an AI for hints and reviews

You edit code in your IDE. The web app does the rest.

## Setup

```powershell
cd C:\interview\leetcode-tracker
copy .env.example .env      # then fill in the AI key(s) you want to use
docker compose up -d --build
```

Open http://localhost:8080.

1. **Import**: open the Import page and pick `C:\interview\dsa`. The browser zips the project, skipping `bin`, `obj` and `.git`, and uploads it. The server stores all 250 problems, their tests and clean stubs in Postgres.
   - You only need to do this once. The dsa project is never modified.
   - Importing again is safe. Problems are matched by their NC number, so nothing is duplicated and your attempts are kept.

## Practice loop

1. **Download**: on the dashboard, queue or a problem page, click **Download exercise**. You get `NC004-TwoSum.zip`, a small standalone solution with the stub, its tests and the shared `DSA.Common` helpers.
2. Unzip it anywhere and open the `.slnx` in Rider or VS.
3. **Start**: click **Start** when you begin. If you forgot, enter the time you actually started. **Pause** and **Resume** stop and restart the clock.
4. **Run tests**: select the unzipped folder once (Chrome and Edge remember it), then:
   - **Run tests** checks your code. Nothing is saved.
   - **Hint 1/2/3** gives a nudge, the approach, or pseudo-code. Using a hint flags the attempt.
   - **Review** sends your current code for an AI code review.
5. **Submit**: the browser zips the folder and the server runs the tests from the database, so editing `SolutionTests.cs` can't fake a pass.
   - If every test passes, the attempt is **Passed**. Otherwise it is **Failed**.
   - Either way the attempt is finished and you can delete the folder.
6. **Give up**: records the attempt as failed. You can save your code with it.

**Flags** don't make an attempt fail:
- 💡 means you used at least one hint.
- ⏱ means you went over the time limit. The defaults are Easy 30, Medium 45 and Hard 60 minutes, and you can change them in Settings.

## Queue

Each problem goes into the first enabled rule it matches, based on its latest finished attempt. Default order:

1. **New**: never attempted.
2. **Failed**: once the 24-hour cooldown has passed. Problems still cooling down are listed separately.
3. **Solved with hints**.
4. **Solved over the time limit**.
5. **Review due**: spaced repetition, most overdue first.
6. **Upcoming reviews**: not due yet, listed last and dimmed.

**Review stages.** Each clean pass in a row moves a problem up a stage. A clean pass is one with no hints and within the time limit.
- Stage 1 comes back after **2 days**, stage 2 after **4 days**, and from then on every **8 days**.
- A hint, a fail or going over time **resets** the stages.
  - A hinted or over-time pass goes straight back into its own group.
  - A fail waits out the cooldown first.

You can reorder the rules, turn them off, and change the cooldown and the review intervals in **Settings**. The Queue page has an **All topics** tab and one tab per topic.

## AI providers

Pick the provider in **Settings**, then put its key in `.env` and run `docker compose up -d` again.

| Provider | `.env` |
|---|---|
| Anthropic API | `ANTHROPIC_API_KEY` (default model `claude-opus-5-5`, which you can change in Settings) |
| DeepSeek API | `DEEPSEEK_API_KEY` (`deepseek-chat` or `deepseek-reasoner`) |
| Claude Code CLI | `CLAUDE_CODE_OAUTH_TOKEN`, from running `claude setup-token` on your PC. This uses your Claude subscription. |

## Development

```powershell
cd backend
dotnet test                       # unit tests + integration tests (Postgres via Testcontainers, real `dotnet test` judging)
dotnet ef migrations add <Name> -p Tracker.Api -o Data/Migrations
```

The frontend lives in `frontend/` (Vite, React and Tailwind). To work on it, run `npm install` and then `npm run dev`. API requests are proxied to `localhost:8080`.

Data lives in the `pgdata` Docker volume. `docker compose down` keeps it. `docker compose down -v` deletes it.
