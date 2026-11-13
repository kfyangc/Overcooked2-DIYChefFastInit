namespace OC2DIYChef
{
    internal sealed class ChefGridDoubleClick
    {
        const float Interval = 0.3f, DistanceSquared = 64f;
        bool pending;
        long player;
        int chef;
        float time, x, y;

        internal bool Click(long playerKey, int chefIndex, float pointerX, float pointerY, float now)
        {
            float dx = pointerX - x, dy = pointerY - y;
            bool confirm = pending && player == playerKey && chef == chefIndex &&
                now >= time && now - time <= Interval && dx * dx + dy * dy <= DistanceSquared;
            pending = !confirm;
            player = playerKey;
            chef = chefIndex;
            time = now;
            x = pointerX;
            y = pointerY;
            return confirm;
        }

        internal void Reset() { pending = false; }
    }
}
