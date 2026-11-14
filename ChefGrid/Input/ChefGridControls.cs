namespace OC2DIYChef
{
    internal struct ChefGridControlEvents
    {
        internal bool Accept, Cancel;
        internal int Direction;
    }

    // Observe raw mapped button state even for waiting players. Claimed UI events cannot
    // swallow confirmation, and a held shared key cannot confirm the following turn.
    internal sealed class ChefGridControls
    {
        bool acceptWasDown, cancelWasDown, needsNeutral;
        int previousDirection = -1;
        float repeatAt;
        bool hasBinding;
        int boundPad, boundSide;

        // Native usersChanged refreshes recreate PlayerGameInput after chef selection.
        // Preserve held-button edges and repeat deadlines for the same device and side.
        internal bool Bind(int pad, int side)
        {
            if (hasBinding && boundPad == pad && boundSide == side) return false;
            hasBinding = true;
            boundPad = pad;
            boundSide = side;
            acceptWasDown = cancelWasDown = needsNeutral = false;
            previousDirection = -1;
            repeatAt = 0;
            return true;
        }

        internal ChefGridControlEvents Poll(int direction, bool accept, bool cancel, float now, bool active)
        {
            var result = new ChefGridControlEvents {
                Accept = active && accept && !acceptWasDown,
                Cancel = active && cancel && !cancelWasDown,
                Direction = -1 };
            acceptWasDown = accept;
            cancelWasDown = cancel;
            if (!active)
            {
                needsNeutral = direction >= 0;
                previousDirection = -1;
                return result;
            }
            if (needsNeutral)
            {
                if (direction >= 0) return result;
                needsNeutral = false;
            }
            if (direction >= 0 && (direction != previousDirection || now >= repeatAt))
            {
                repeatAt = now + (direction != previousDirection ? 0.35f : 0.1f);
                result.Direction = direction;
            }
            previousDirection = direction;
            return result;
        }
    }
}
