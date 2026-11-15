namespace OC2DIYChef
{
    internal struct ChefGridControlEvents
    {
        internal bool Accept, Cancel;
        internal int Direction;
    }

    // Own binding changes, first observation, press edges and directional repeat.
    // Callers supply raw mapped state even while disabled; opening/held input is
    // consumed here rather than requiring a second observation flag in each menu.
    internal sealed class ChefGridControls
    {
        bool acceptWasDown, cancelWasDown, needsNeutral;
        int previousDirection = -1;
        float repeatAt;
        bool hasBinding, observed;
        int boundPad, boundSide;

        // Native usersChanged refreshes recreate PlayerGameInput after chef selection.
        // Preserve held-button edges and repeat deadlines for the same device and side.
        internal void Bind(int pad, int side)
        {
            if (hasBinding && boundPad == pad && boundSide == side) return;
            hasBinding = true;
            observed = false;
            boundPad = pad;
            boundSide = side;
            acceptWasDown = cancelWasDown = needsNeutral = false;
            previousDirection = -1;
            repeatAt = 0;
        }

        internal ChefGridControlEvents Poll(int direction, bool accept, bool cancel, float now, bool active)
        {
            active &= observed;
            observed = true;
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
