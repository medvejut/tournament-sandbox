using System;
using NUnit.Framework;
using TournamentSandbox.Core;
using TournamentSandbox.Net;
using UnityEngine;

namespace TournamentSandbox.Tests
{
    // The JSON below is captured from the real server, nulls included.
    public sealed class DtoTests
    {
        private const string PlayingView =
            "{\"matchId\":\"e1bb6e6d-cc83-4188-95de-074d656a5981\",\"tournamentId\":\"gold\",\"seed\":4294967295,\"durationMs\":120000," +
            "\"startedAt\":1791127002547,\"deadline\":1791127722547,\"multiplier\":1,\"status\":\"playing\",\"score\":null," +
            "\"opponentScore\":null,\"result\":null,\"payout\":0,\"balance\":900,\"rejectedReason\":null,\"flags\":[]}";

        private const string CompletedView =
            "{\"matchId\":\"fadf\",\"tournamentId\":\"bronze\",\"seed\":93587114,\"durationMs\":120000,\"startedAt\":1791127002625," +
            "\"deadline\":1791127722625,\"multiplier\":1.5,\"status\":\"completed\",\"score\":3576,\"opponentScore\":3366," +
            "\"result\":\"win\",\"payout\":27,\"balance\":1017,\"rejectedReason\":null,\"flags\":[\"client-score-mismatch\"]}";

        [Test]
        public void MatchView_Playing_ReadsNullsAsDefaults()
        {
            var view = JsonUtility.FromJson<MatchView>(PlayingView);
            Assert.That(view.status, Is.EqualTo(MatchStatus.Playing));
            Assert.That(view.Seed, Is.EqualTo(uint.MaxValue));
            Assert.That(view.startedAt, Is.EqualTo(1791127002547L));
            Assert.That(view.deadline, Is.EqualTo(1791127722547L));
            Assert.That(view.score, Is.EqualTo(0));
            Assert.That(view.opponentScore, Is.EqualTo(0));
            Assert.That(string.IsNullOrEmpty(view.result));
            Assert.That(string.IsNullOrEmpty(view.rejectedReason));
            Assert.That(view.flags, Is.Empty);
            Assert.That(view.IsFinal, Is.False);
        }

        [Test]
        public void MatchView_Completed_ReadsEveryField()
        {
            var view = JsonUtility.FromJson<MatchView>(CompletedView);
            Assert.That(view.IsFinal);
            Assert.That(view.result, Is.EqualTo(MatchResult.Win));
            Assert.That(view.score, Is.EqualTo(3576));
            Assert.That(view.opponentScore, Is.EqualTo(3366));
            Assert.That(view.multiplier, Is.EqualTo(1.5f));
            Assert.That(view.HasFlag("client-score-mismatch"));
        }

        [Test]
        public void SubmitRequest_SerializesToTheServerShape()
        {
            var json = JsonUtility.ToJson(new SubmitRequest { moves = new[] { Move.DaubAt(3, 2500), Move.BingoAt(4000) }, clientScore = 625 });
            Assert.That(json, Is.EqualTo("{\"moves\":[{\"kind\":\"daub\",\"cell\":3,\"t\":2500},{\"kind\":\"bingo\",\"cell\":-1,\"t\":4000}],\"clientScore\":625}"));
        }

        [Test]
        public void LiveOpsEventIsActive_FromStartUntilEndExclusive()
        {
            var response = JsonUtility.FromJson<LiveOpsResponse>(
                "{\"serverTime\":1000,\"events\":[{\"id\":\"ev_1\",\"type\":\"double_rewards\",\"startsAt\":2000,\"endsAt\":5000,\"multiplier\":2}]}");
            var liveOpsEvent = response.events[0];
            Assert.That(liveOpsEvent.IsActive(1999), Is.False);
            Assert.That(liveOpsEvent.IsActive(2000), Is.True);
            Assert.That(liveOpsEvent.IsActive(4999), Is.True);
            Assert.That(liveOpsEvent.IsActive(5000), Is.False);
        }

        [Test]
        public void ErrorResponse_ReadsTheCode()
        {
            var response = JsonUtility.FromJson<ErrorResponse>("{\"error\":{\"code\":\"already-submitted\",\"message\":\"nope\"}}");
            Assert.That(response.error.code, Is.EqualTo(ErrorCodes.AlreadySubmitted));
        }

        [Test]
        public void ApiException_ClassifiesByStatus()
        {
            Assert.That(ApiException.Timeout().IsNoResponse);
            Assert.That(new ApiException(503, ErrorCodes.ChaosFail).IsServerError);
            Assert.That(new ApiException(409, ErrorCodes.AlreadySubmitted).IsClientError);
        }

        [Test]
        public void FromJson_InvalidJson_ThrowsArgumentException() =>
            Assert.Throws<ArgumentException>(() => JsonUtility.FromJson<ErrorResponse>("<html>502</html>"));
    }
}