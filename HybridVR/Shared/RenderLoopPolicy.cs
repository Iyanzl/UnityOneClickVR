namespace HybridVR.Shared
{
    // Latch the phase on the first callback of a frame. A live menu change must
    // never render both phases or skip a frame halfway through its callbacks.
    public sealed class RenderLoopPolicy
    {
        private int _frame;
        private bool _hasFrame, _entered;
        public bool InUpdate { get; private set; }

        public bool TryEnter(int frame, bool updateCallback, bool requestedUpdate)
        {
            if (!_hasFrame || _frame != frame)
            {
                _hasFrame = true;
                _frame = frame;
                _entered = false;
                // A manager enabled after Update can still draw in LateUpdate.
                InUpdate = requestedUpdate && updateCallback;
            }
            if (_entered || updateCallback != InUpdate) return false;
            _entered = true;
            return true;
        }
    }
}
