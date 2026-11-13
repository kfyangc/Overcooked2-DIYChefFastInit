namespace OC2DIYChef
{
    // Keep the modal's input ownership through the closing frame and held keys.
    internal sealed class ChefGridLobbySession
    {
        internal bool Open { get; private set; }
        internal bool Blocking { get; private set; }
        int closedFrame;

        internal void Begin() { Open = Blocking = true; }
        internal void End(int frame)
        {
            if (!Open) return;
            Open = false;
            closedFrame = frame;
        }

        internal void Advance(int frame, bool held)
        {
            if (!Open && frame > closedFrame && !held) Blocking = false;
        }

        internal void Reset() { Open = Blocking = false; }
    }
}
