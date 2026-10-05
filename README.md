# Tournament Sandbox

A 1v1 Speed Bingo tournament with a Unity 6 client and a Node/TypeScript server, built as a pet project to get hands-on with server-authoritative tournaments.\
I implemented the client's networking layer (`client/Assets/Scripts/Net`); the rest, from the server and game rules to the UI and tests, was written by Claude, with me directing and reviewing.

Both players get the same card from a shared seed, and the server replays each move log for the real score.\
Entering and submitting a match are idempotent, so the client retries safely when a response is lost, and a match survives the app being killed.\
EditMode tests for the Unity Test Runner cover the rules and the networking classes.

To try it, run `npm install` and `npm start` in `server/` (Node 24+).\
A match needs a second player: `npm run second-player` adds a bot.\
The server can also run a live-ops event and inject network failures.

<details>
<summary>Live-ops event</summary>

Double rewards starting in 30 seconds, for 90. The lobby banner follows the server's clock, not the device's.

```bash
curl -X POST localhost:8080/v1/dev/liveops -H "content-type: application/json" -d '{"startsInSec":30,"durationSec":90,"multiplier":2}'
```
</details>

<details>
<summary>Chaos</summary>

30% of requests fail with 503, 20% lose their response after the server has handled them. Send zeros to turn it off. In the Editor, turn off Error Pause in the Console first: Unity logs a dropped response as an error.

```bash
curl -X POST localhost:8080/v1/dev/chaos -H "content-type: application/json" -d '{"failRate":0.3,"dropRate":0.2}'
```
</details>
