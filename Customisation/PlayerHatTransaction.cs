using System;
using System.Collections.Generic;
using Team17.Online;

namespace OC2DIYChef
{
    internal sealed class PlayerHatTransaction : IDisposable
    {
        sealed class Choice { internal User User; internal string Hat; }
        readonly Dictionary<string, Choice> choices = new Dictionary<string, Choice>();
        bool committed;
        internal void Begin() { Dispose(); committed = false; }
        internal void Capture(User user)
        {
            string key = PlayerHats.Key(user);
            if (!choices.ContainsKey(key)) choices[key] = new Choice { User = user, Hat = PlayerHats.Get(user) };
        }
        internal void Commit() { committed = true; }
        public void Dispose()
        {
            var restore = new List<Choice>(choices.Values);
            choices.Clear();
            bool cancel = !committed;
            committed = true;
            if (cancel)
                foreach (var choice in restore)
                    if (PlayerHats.Get(choice.User) != choice.Hat) PlayerHats.Set(choice.User, choice.Hat);
        }
    }
}
