using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Badger.Classes
{
    public class RaidTracker
    {
        private sealed class UserActivity
        {
            public readonly object Lock = new();
            public readonly List<(ulong ChannelId, ulong MessageId, DateTimeOffset Timestamp)> Entries = new();
            public bool Tripped;
            public DateTimeOffset LastSeen;
        }

        private readonly ConcurrentDictionary<ulong, UserActivity> _users = new();
        private readonly int _channelThreshold;
        private readonly TimeSpan _window;
        private readonly Timer _pruneTimer;

        public RaidTracker(int channelThreshold, TimeSpan window)
        {
            _channelThreshold = channelThreshold;
            _window = window;
            _pruneTimer = new Timer(_ => PruneStale(), null, 300000, 300000);
        }

        /// Returns true exactly once — on the message that crosses the threshold.
        public bool RecordMessage(ulong userId, ulong channelId, ulong messageId, DateTimeOffset timestamp)
        {
            UserActivity activity = _users.GetOrAdd(userId, _ => new UserActivity());
            lock (activity.Lock)
            {
                // A ban is already in flight for this user; don't trigger again.
                if (activity.Tripped)
                    return false;

                activity.LastSeen = timestamp;
                DateTimeOffset cutoff = timestamp - _window;
                activity.Entries.RemoveAll(e => e.Timestamp < cutoff);
                activity.Entries.Add((channelId, messageId, timestamp));

                if (activity.Entries.Select(e => e.ChannelId).Distinct().Count() >= _channelThreshold)
                {
                    activity.Tripped = true;
                    return true;
                }
                return false;
            }
        }

        /// The messages that tripped the threshold. Only meaningful after RecordMessage returns true —
        /// entries are frozen from that point until Forget.
        public IReadOnlyList<(ulong ChannelId, ulong MessageId)> GetRaidMessages(ulong userId)
        {
            if (!_users.TryGetValue(userId, out UserActivity activity))
                return Array.Empty<(ulong, ulong)>();
            lock (activity.Lock)
            {
                return activity.Entries.Select(e => (e.ChannelId, e.MessageId)).ToList();
            }
        }

        /// Drops all state for a user (after a ban, or when an exempt user trips the threshold).
        public void Forget(ulong userId)
        {
            _users.TryRemove(userId, out _);
        }

        private void PruneStale()
        {
            DateTimeOffset cutoff = DateTimeOffset.UtcNow - _window - _window;
            foreach (KeyValuePair<ulong, UserActivity> user in _users)
            {
                if (!user.Value.Tripped && user.Value.LastSeen < cutoff)
                    _users.TryRemove(user.Key, out _);
            }
        }
    }
}
