using System;
using System.Collections.Generic;
using VividWorld.Core.Presentation;
using Xunit;

namespace VividWorld.Core.Tests
{
    public class FactTextSubstitutionTests
    {
        private static string Echo(string v) => v;

        [Fact]
        public void Apply_SubstitutesEveryPlaceholder()
        {
            var vars = new Dictionary<string, string>
            {
                ["HOST"] = "Bersag",
                ["GUEST"] = "Corein",
            };

            Assert.Equal(
                "Bersag shared a simple meal with Corein",
                FactTextSubstitution.Apply("{HOST} shared a simple meal with {GUEST}", vars, Echo));
        }

        /// <summary>帳本 L-23：磁碟上已經有一批鍵被舊序列化器改成小寫，那些分片也要救得回來。</summary>
        [Fact]
        public void Apply_MatchesPlaceholdersCaseInsensitively()
        {
            var vars = new Dictionary<string, string>
            {
                ["host"] = "Bersag",
                ["guest"] = "Corein",
            };

            Assert.Equal(
                "Bersag shared a simple meal with Corein",
                FactTextSubstitution.Apply("{HOST} shared a simple meal with {GUEST}", vars, Echo));
        }

        [Fact]
        public void Apply_RunsTheResolverOnTheValue()
        {
            var vars = new Dictionary<string, string> { ["WHO"] = "hero:lord_5_11" };

            Assert.Equal(
                "Corein was there",
                FactTextSubstitution.Apply("{WHO} was there", vars, v => v == "hero:lord_5_11" ? "Corein" : v));
        }

        /// <summary>
        /// 代換不掉就原樣留著。悄悄拿掉會變成把半句話送給玩家，
        /// 而遊戲的文字引擎會再把留著的 {GUEST} 吃成空字串（「shared a simple meal with.」）。
        /// </summary>
        [Fact]
        public void Apply_LeavesUnknownPlaceholdersInPlace()
        {
            var vars = new Dictionary<string, string> { ["HOST"] = "Bersag" };

            string result = FactTextSubstitution.Apply("{HOST} shared a simple meal with {GUEST}", vars, Echo);

            Assert.Equal("Bersag shared a simple meal with {GUEST}", result);
            Assert.Equal(new[] { "GUEST" }, FactTextSubstitution.UnresolvedPlaceholders(result));
        }

        [Fact]
        public void UnresolvedPlaceholders_IsEmptyWhenEverythingResolved()
        {
            Assert.Empty(FactTextSubstitution.UnresolvedPlaceholders("both parted in peaceful spirits"));
        }

        [Fact]
        public void UnresolvedPlaceholders_IgnoresTheGameOwnMarkers()
        {
            Assert.Empty(FactTextSubstitution.UnresolvedPlaceholders("{=!}plain text"));
            Assert.Equal(new[] { "WHO" }, FactTextSubstitution.UnresolvedPlaceholders("{=!}{WHO} was there"));
        }

        [Fact]
        public void Apply_WithNoVars_ReturnsTextUnchanged()
        {
            Assert.Equal("at Pravend", FactTextSubstitution.Apply("at Pravend", null, Echo));
            Assert.Equal("at Pravend", FactTextSubstitution.Apply("at Pravend", new Dictionary<string, string>(), Echo));
        }

        [Fact]
        public void Apply_WithEmptyText_ReturnsEmpty()
        {
            Assert.Equal(string.Empty, FactTextSubstitution.Apply(null, null, Echo));
            Assert.Equal(string.Empty, FactTextSubstitution.Apply(string.Empty, null, Echo));
        }
    }
}
